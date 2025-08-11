namespace WeekChgkSPB;

public sealed record AnnouncementRow(
    long Id,
    string TournamentName,
    string Place,
    DateTime DateTimeUtc,
    int Cost,
    string Link);