namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementPreParseResult(
    bool Success,
    string? FailureCode,
    string CompactEventText,
    int? Cost,
    string? CostEvidence,
    DateTime? LocalDateTime,
    string? Place,
    int SourceLength,
    bool IsVenueChange = false)
{
    public bool CanCallApi =>
        Cost.HasValue &&
        !string.IsNullOrWhiteSpace(CompactEventText);

    public static AnnouncementPreParseResult Failed(
        string failureCode,
        int sourceLength,
        string compactEventText = "",
        int? cost = null,
        string? costEvidence = null,
        DateTime? localDateTime = null,
        string? place = null,
        bool isVenueChange = false) =>
        new(false, failureCode, compactEventText, cost, costEvidence, localDateTime, place, sourceLength, isVenueChange);
}
