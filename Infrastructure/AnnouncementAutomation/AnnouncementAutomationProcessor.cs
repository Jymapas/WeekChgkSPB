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

    public bool ShouldProcessPost(long postId, bool isNewPost, bool isVenueChange = false)
    {
        if (announcements.IsAutomationSuppressed(postId)) return false;
        if (options.Mode == AnnouncementAutomationMode.Off)
        {
            return isNewPost;
        }

        if (isVenueChange)
        {
            return !attempts.HasOutcomePrefix(postId, "venue_change_");
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
        if (announcements.IsAutomationSuppressed(post.Id, post.Link)) return;
        if (options.Mode == AnnouncementAutomationMode.Off)
        {
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            return;
        }

        var preParse = preParser.Parse(post, nowUtc);
        if (preParse.IsVenueChange)
        {
            await ProcessVenueChangeAsync(post, preParse, nowUtc, cancellationToken);
            return;
        }

        var existingDraft = reviewDrafts.Get(post.Id);
        if (existingDraft is not null &&
            existingDraft.Status == AnnouncementReviewStatuses.Pending)
        {
            await reviewHandler.EnsureNotificationAsync(post, existingDraft, cancellationToken);
            return;
        }

        if (!preParse.CanCallApi)
        {
            if (preParse.FailureCode == "cost_ambiguous")
            {
                await CreateReviewAsync(post, preParse,
                    AnnouncementExtractionResult.Failed("cost_ambiguous"),
                    preParse.FailureCode, null, null, cancellationToken);
                return;
            }

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
        if (!announcements.TryInsertAutomatically(validation.Announcement, post.Link))
        {
            var deleted = announcements.IsAutomationSuppressed(post.Id, post.Link);
            SaveAttempt(post.Id, preParse, extraction, deleted ? "suppressed" : "duplicate",
                deleted ? "announcement_deleted" : "announcement_exists", extraction.Candidate);
            return;
        }
        attempts.MarkSaved(post.Id);
        await channelPostUpdater.UpdateLastPostAsync(cancellationToken);
        attempts.MarkChannelUpdated(post.Id);
        await notifier.NotifyAutomationSavedAsync(post, validation.Announcement, cancellationToken);
        attempts.MarkNotified(post.Id);
    }

    public async Task ReconcileVenueChangesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (options.Mode != AnnouncementAutomationMode.Active)
        {
            return;
        }

        foreach (var post in posts.GetVenueChangePosts())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldProcessPost(post.Id, isNewPost: false, isVenueChange: true))
            {
                await ProcessAsync(post, nowUtc, cancellationToken);
            }
        }
    }

    private async Task ProcessVenueChangeAsync(
        Post post,
        AnnouncementPreParseResult preParse,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!preParse.CanCallApi)
        {
            SaveAttempt(post.Id, preParse, null, "venue_change_unrecognized", preParse.FailureCode, null);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        var examples = posts.FindNameExamples(post.Title, 3);
        var today = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            moscowTimeZone).Date;
        var extraction = await extractionClient.ExtractAsync(post, preParse, examples, today, cancellationToken);
        if (!extraction.Success || extraction.Candidate is null)
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_extraction_failed", extraction.FailureCode, null);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        AnnouncementValidationResult validation;
        try
        {
            validation = validator.Validate(post, preParse, extraction.Candidate);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_validation_failed", "validation_error", extraction.Candidate);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        if (!validation.Success || validation.Announcement is null)
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_validation_failed", validation.FailureCode, extraction.Candidate);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        if (options.Mode == AnnouncementAutomationMode.Shadow)
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_shadow", null, extraction.Candidate);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        var matches = announcements.FindByTournamentNameAndDateTimeUtcExceptId(
            validation.Announcement.TournamentName,
            validation.Announcement.DateTimeUtc,
            post.Id);
        if (matches.Count > 1)
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_ambiguous", "multiple_announcements", extraction.Candidate);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        var sourceId = matches.Count == 1 ? matches[0].Id : post.Id;
        if (matches.Count == 0 && !announcements.Exists(post.Id))
        {
            SaveAttempt(post.Id, preParse, extraction, "venue_change_unmatched", "announcement_not_found", extraction.Candidate);
            await NotifyVenueChangeOnceAsync(post, cancellationToken);
            return;
        }

        var changed = announcements.ApplyVenueChange(sourceId, validation.Announcement);
        SaveAttempt(
            post.Id,
            preParse,
            extraction,
            changed ? "venue_change_applied" : "venue_change_already_applied",
            null,
            extraction.Candidate);
        if (changed)
        {
            attempts.MarkSaved(post.Id);
            await channelPostUpdater.UpdateLastPostAsync(cancellationToken);
            attempts.MarkChannelUpdated(post.Id);
        }
    }

    private async Task NotifyVenueChangeOnceAsync(Post post, CancellationToken cancellationToken)
    {
        if (attempts.WasNotified(post.Id))
        {
            return;
        }

        await notifier.NotifyNewPostAsync(post, cancellationToken);
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
            DateTimeUtc = preParse.FailureCode == "datetime_conflict" ? null :
                          validatedAnnouncement?.DateTimeUtc ??
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
