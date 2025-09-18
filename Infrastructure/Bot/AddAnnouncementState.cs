namespace WeekChgkSPB.Infrastructure.Bot;

internal enum AddStep
{
    None,
    WaitingId,
    WaitingName,
    WaitingPlace,
    WaitingDateTime,
    WaitingCost,
    Done,
    FooterWaitingText
}

internal class AddAnnouncementState
{
    public AddStep Step { get; set; } = AddStep.None;
    public Announcement Draft { get; } = new();
}