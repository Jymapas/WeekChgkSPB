namespace WeekChgkSPB;

public class Announcement
{
    public long Id { get; set; }
    public string TournamentName { get; set; } = "";
    public string Place { get; set; } = "";
    public DateTime DateTimeUtc { get; set; }
    public int Cost { get; set; }
}