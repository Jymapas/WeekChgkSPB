namespace WeekChgkSPB;

internal sealed record AnnouncementMutationResult(
    AnnouncementMutationStatus Status,
    string? Message = null,
    Announcement? Announcement = null);
