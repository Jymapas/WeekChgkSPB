namespace WeekChgkSPB.Infrastructure.Bot;

internal enum AddStep
{
    None,
    WaitingId,
    WaitingName,
    WaitingPlace,
    WaitingDateTime,
    WaitingCost,
    WaitingLines,
    EditWaitingName,
    EditWaitingPlace,
    EditWaitingDateTime,
    EditWaitingCost,
    Done,
    FooterWaitingText,
    FooterWaitingExpiry,
    FooterEditWaitingText,
    FooterEditWaitingExpiry
}

internal class AddAnnouncementState
{
    public AddStep Step { get; set; } = AddStep.None;
    public string DraftLink { get; set; } = string.Empty;
    public Announcement Draft { get; } = new()
    {
        TournamentName = string.Empty,
        Place = string.Empty
    };
    public Announcement? Existing { get; set; }
    public string FooterDraftText { get; set; } = string.Empty;
    public long FooterEditId { get; set; }
}
