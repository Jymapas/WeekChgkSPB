using System.Text.Json;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementAutomationProcessor(
    AnnouncementAutomationOptions options,
    AnnouncementPreParser preParser,
    IAnnouncementExtractionClient extractionClient,
    AnnouncementCandidateValidator validator,
    AnnouncementParseAttemptsRepository attempts,
    AnnouncementReviewDraftRepository reviewDrafts,
    AnnouncementReviewHandler reviewHandler,
    PostsRepository posts,
    AnnouncementsRepository announcements,
    IChannelPostUpdater channelPostUpdater,
    INotifier notifier,
    TimeZoneInfo moscowTimeZone)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool ShouldProcessPost(long postId, bool isNewPost)
    {
        if (options.Mode == AnnouncementAutomationMode.Off)
        {
            return isNewPost;
        }

        var existingDraft = reviewDrafts.Get(postId);
        if (existingDraft is not null &&
            existingDraft.Status == AnnouncementReviewStatuses.Pending &&
            (!existingDraft.SourceMessageId.HasValue || !existingDraft.ReviewMessageId.HasValue))
        {
            return true;
        }

        return !attempts.Exists(postId);
    }

    public async Task ProcessAsync(Post post, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (options.Mode == AnnouncementAutomationMode.Off)
        {
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            return;
        }

        var existingDraft = reviewDrafts.Get(post.Id);
        if (existingDraft is not null &&
            existingDraft.Status == AnnouncementReviewStatuses.Pending)
        {
            await reviewHandler.EnsureNotificationAsync(post, existingDraft, cancellationToken);
            return;
        }

        var preParse = preParser.Parse(post, nowUtc);
        if (!preParse.CanCallApi)
        {
            SaveAttempt(post.Id, preParse, null, "fallback", preParse.FailureCode, null);
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            attempts.MarkNotified(post.Id);
            return;
        }

        var examples = posts.FindNameExamples(post.Title, 3);
        var today = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            moscowTimeZone).Date;
        var extraction = await extractionClient.ExtractAsync(
            post,
            preParse,
            examples,
            today,
            cancellationToken);
        if (!extraction.Success || extraction.Candidate is null)
        {
            await CreateReviewAsync(
                post,
                preParse,
                extraction,
                extraction.FailureCode,
                null,
                null,
                cancellationToken);
            return;
        }

        AnnouncementValidationResult validation;
        try
        {
            validation = validator.Validate(post, preParse, extraction.Candidate);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await CreateReviewAsync(
                post,
                preParse,
                extraction,
                "validation_error",
                extraction.Candidate,
                null,
                cancellationToken);
            return;
        }

        if (options.Mode == AnnouncementAutomationMode.Shadow ||
            !validation.Success ||
            validation.Announcement is null)
        {
            await CreateReviewAsync(
                post,
                preParse,
                extraction,
                validation.Success
                    ? null
                    : validation.FailureCode == "preparse_invalid"
                        ? preParse.FailureCode
                        : validation.FailureCode ?? preParse.FailureCode,
                extraction.Candidate,
                validation.Announcement,
                cancellationToken);
            return;
        }

        if (announcements.Exists(post.Id) ||
            (!string.IsNullOrWhiteSpace(post.Link) && announcements.GetByLink(post.Link) is not null))
        {
            SaveAttempt(post.Id, preParse, extraction, "duplicate", "announcement_exists", extraction.Candidate);
            return;
        }

        SaveAttempt(post.Id, preParse, extraction, "accepted", null, extraction.Candidate);
        announcements.Insert(validation.Announcement);
        attempts.MarkSaved(post.Id);
        await channelPostUpdater.UpdateLastPostAsync(cancellationToken);
        attempts.MarkChannelUpdated(post.Id);
        await notifier.NotifyAutomationSavedAsync(post, validation.Announcement, cancellationToken);
        attempts.MarkNotified(post.Id);
    }

    private async Task CreateReviewAsync(
        Post post,
        AnnouncementPreParseResult preParse,
        AnnouncementExtractionResult extraction,
        string? failureCode,
        AnnouncementExtractionCandidate? candidate,
        Announcement? validatedAnnouncement,
        CancellationToken cancellationToken)
    {
        var draft = new AnnouncementReviewDraft
        {
            PostId = post.Id,
            TournamentName = validatedAnnouncement?.TournamentName ??
                             NullIfWhiteSpace(candidate?.TournamentName),
            Place = validatedAnnouncement?.Place ??
                    NullIfWhiteSpace(preParse.Place) ??
                    NullIfWhiteSpace(candidate?.Place),
            DateTimeUtc = validatedAnnouncement?.DateTimeUtc ??
                          ResolveDateTimeUtc(preParse.LocalDateTime, candidate?.LocalDateTime),
            Cost = preParse.Cost,
            FailureCode = failureCode,
            Status = AnnouncementReviewStatuses.Pending
        };
        reviewDrafts.Upsert(draft);
        SaveAttempt(
            post.Id,
            preParse,
            extraction,
            "review_pending",
            failureCode,
            candidate);
        await reviewHandler.EnsureNotificationAsync(post, draft, cancellationToken);
    }

    private DateTime? ResolveDateTimeUtc(DateTime? localDateTime, string? candidateLocalDateTime)
    {
        DateTime local;
        if (localDateTime.HasValue)
        {
            local = DateTime.SpecifyKind(localDateTime.Value, DateTimeKind.Unspecified);
        }
        else if (!DateTime.TryParseExact(
                     candidateLocalDateTime,
                     ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
                     System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None,
                     out local))
        {
            return null;
        }

        if (moscowTimeZone.IsInvalidTime(local) || moscowTimeZone.IsAmbiguousTime(local))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, moscowTimeZone);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SaveAttempt(
        long postId,
        AnnouncementPreParseResult preParse,
        AnnouncementExtractionResult? extraction,
        string outcome,
        string? failureCode,
        AnnouncementExtractionCandidate? candidate)
    {
        attempts.Upsert(new AnnouncementParseAttempt(
            postId,
            options.Mode.ToString().ToLowerInvariant(),
            outcome,
            failureCode,
            preParse.Cost,
            preParse.CostEvidence,
            preParse.SourceLength,
            extraction?.PayloadLength ?? 0,
            "alibaba_model_studio",
            options.Model,
            extraction?.InputTokens,
            extraction?.OutputTokens,
            candidate is null ? null : JsonSerializer.Serialize(candidate, JsonOptions)));
    }
}
