using System.Text.Json.Serialization;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementExtractionCandidate(
    [property: JsonPropertyName("rawTournamentName")] string RawTournamentName,
    [property: JsonPropertyName("tournamentName")] string TournamentName,
    [property: JsonPropertyName("place")] string Place,
    [property: JsonPropertyName("localDateTime")] string LocalDateTime,
    [property: JsonPropertyName("evidence")] AnnouncementExtractionEvidence Evidence);
