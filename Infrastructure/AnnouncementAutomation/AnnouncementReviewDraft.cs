namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementReviewDraft
{
    public long PostId { get; init; }
    public string? TournamentName { get; set; }
    public string? Place { get; set; }
    public DateTime? DateTimeUtc { get; set; }
    public int? Cost { get; set; }
    public string? FailureCode { get; set; }
    public string Status { get; set; } = AnnouncementReviewStatuses.Pending;
    public int? SourceMessageId { get; set; }
    public int? ReviewMessageId { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(TournamentName) &&
        !string.IsNullOrWhiteSpace(Place) &&
        DateTimeUtc.HasValue &&
        Cost.HasValue;
}

internal static class AnnouncementReviewStatuses
{
    public const string Pending = "pending";
    public const string Added = "added";
    public const string Skipped = "skipped";
}
