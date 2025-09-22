namespace WeekChgkSPB;

public class Announcement
{
    public long Id { get; set; }
    public required string TournamentName { get; set; }
    public required string Place { get; set; }
    public DateTime DateTimeUtc { get; set; }
    public int Cost { get; set; }
}
