using System.Text.Json;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementAutomationProcessor(
    AnnouncementAutomationOptions options,
    AnnouncementPreParser preParser,
    IAnnouncementExtractionClient extractionClient,
    AnnouncementCandidateValidator validator,
    AnnouncementParseAttemptsRepository attempts,
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

        return !attempts.Exists(postId);
    }

    public async Task ProcessAsync(Post post, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (options.Mode == AnnouncementAutomationMode.Off)
        {
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            return;
        }

        var preParse = preParser.Parse(post, nowUtc);
        if (!preParse.Success)
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
            SaveAttempt(post.Id, preParse, extraction, "fallback", extraction.FailureCode, null);
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            attempts.MarkNotified(post.Id);
            return;
        }

        AnnouncementValidationResult validation;
        try
        {
            validation = validator.Validate(post, preParse, extraction.Candidate);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            SaveAttempt(post.Id, preParse, extraction, "fallback", "validation_error", extraction.Candidate);
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            attempts.MarkNotified(post.Id);
            return;
        }
        if (!validation.Success || validation.Announcement is null)
        {
            SaveAttempt(post.Id, preParse, extraction, "fallback", validation.FailureCode, extraction.Candidate);
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            attempts.MarkNotified(post.Id);
            return;
        }

        if (options.Mode == AnnouncementAutomationMode.Shadow)
        {
            SaveAttempt(post.Id, preParse, extraction, "shadow_candidate", null, extraction.Candidate);
            await notifier.NotifyAutomationCandidateAsync(post, validation.Announcement, cancellationToken);
            await notifier.NotifyNewPostAsync(post, cancellationToken);
            attempts.MarkNotified(post.Id);
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
