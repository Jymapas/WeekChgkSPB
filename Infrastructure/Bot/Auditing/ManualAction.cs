namespace WeekChgkSPB.Infrastructure.Bot.Auditing;

internal sealed class ManualAction
{
    internal long Id { get; set; }
    internal int UpdateId { get; init; }
    internal long? UserId { get; init; }
    internal long? ChatId { get; init; }
    internal int? MessageId { get; init; }
    internal string? CallbackQueryId { get; init; }
    internal string Kind { get; init; } = "input";
    internal string Name { get; init; } = "input";
    internal string? Input { get; set; }
    internal string? Handler { get; set; }
    internal string Outcome { get; set; } = "ignored";
    internal string? EntityType { get; set; }
    internal long? EntityId { get; set; }
    internal string? SourceLink { get; set; }
    internal string StepBefore { get; set; } = "None";
    internal string StepAfter { get; set; } = "None";
    internal string? BeforeJson { get; set; }
    internal string? AfterJson { get; set; }
    internal AddAnnouncementState? State { get; set; }
    internal bool Denied { get; set; }
}
