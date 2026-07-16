namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementValidationResult(
    bool Success,
    string? FailureCode,
    Announcement? Announcement)
{
    public static AnnouncementValidationResult Failed(string code) => new(false, code, null);
}
