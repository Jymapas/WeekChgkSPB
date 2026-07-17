namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal interface IAnnouncementExtractionClient
{
    Task<AnnouncementExtractionResult> ExtractAsync(
        Post post,
        AnnouncementPreParseResult preParse,
        IReadOnlyList<AnnouncementNameExample> examples,
        DateTime moscowToday,
        CancellationToken cancellationToken);
}
