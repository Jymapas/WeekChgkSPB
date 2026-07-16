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
    FooterEditWaitingExpiry,
    PendingEditWaitingName,
    PendingEditWaitingPlace,
    PendingEditWaitingDateTime,
    PendingEditWaitingCost
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
    public long? PendingEditId { get; set; }
    public long? PendingEditChatId { get; set; }
    public int? PendingEditMessageId { get; set; }
}
