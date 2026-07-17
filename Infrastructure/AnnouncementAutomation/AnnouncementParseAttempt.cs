namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementParseAttempt(
    long PostId,
    string Mode,
    string Outcome,
    string? FailureCode,
    int? Cost,
    string? CostEvidence,
    int SourceLength,
    int PayloadLength,
    string Provider,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    string? CandidateJson);
