namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementPreParseResult(
    bool Success,
    string? FailureCode,
    string CompactEventText,
    int? Cost,
    string? CostEvidence,
    DateTime? LocalDateTime,
    string? Place,
    int SourceLength)
{
    public static AnnouncementPreParseResult Failed(
        string failureCode,
        int sourceLength,
        string compactEventText = "",
        int? cost = null,
        string? costEvidence = null) =>
        new(false, failureCode, compactEventText, cost, costEvidence, null, null, sourceLength);
}
