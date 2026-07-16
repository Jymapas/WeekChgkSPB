namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementExtractionResult(
    bool Success,
    string? FailureCode,
    AnnouncementExtractionCandidate? Candidate,
    int? InputTokens,
    int? OutputTokens,
    int PayloadLength)
{
    public static AnnouncementExtractionResult Failed(string failureCode, int payloadLength = 0) =>
        new(false, failureCode, null, null, null, payloadLength);
}
