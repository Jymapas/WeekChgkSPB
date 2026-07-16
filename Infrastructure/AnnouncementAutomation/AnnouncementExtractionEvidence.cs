using System.Text.Json.Serialization;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementExtractionEvidence(
    [property: JsonPropertyName("tournamentName")] string TournamentName,
    [property: JsonPropertyName("place")] string Place,
    [property: JsonPropertyName("localDateTime")] string LocalDateTime);
