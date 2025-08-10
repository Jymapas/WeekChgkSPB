namespace WeekChgkSPB.Infrastructure.Bot;

internal enum AddStep
{
    None,
    WaitingId,
    WaitingName,
    WaitingPlace,
    WaitingDateTime,
    WaitingCost,
    Done
}

internal class AddAnnouncementState
{
    public AddStep Step { get; set; } = AddStep.None;
    public Announcement Draft { get; } = new();
}