using System.Globalization;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementCandidateValidator(
    TournamentNameNormalizer nameNormalizer,
    TimeZoneInfo moscowTimeZone)
{
    private static readonly string[] RegistrationMarkers = ["регистрац", "разминк", "сбор команд"];

    public AnnouncementValidationResult Validate(
        Post post,
        AnnouncementPreParseResult preParse,
        AnnouncementExtractionCandidate candidate)
    {
        if (!preParse.Success || preParse.Cost is null || preParse.LocalDateTime is null || preParse.Place is null)
        {
            return AnnouncementValidationResult.Failed("preparse_invalid");
        }

        if (!DateTime.TryParseExact(
                candidate.LocalDateTime,
                ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var candidateLocal))
        {
            return AnnouncementValidationResult.Failed("model_datetime_invalid");
        }

        candidateLocal = DateTime.SpecifyKind(candidateLocal, DateTimeKind.Unspecified);
        if (candidateLocal != preParse.LocalDateTime.Value ||
            !ContainsEvidence(preParse.CompactEventText, candidate.Evidence.LocalDateTime))
        {
            return AnnouncementValidationResult.Failed("datetime_mismatch");
        }

        if (RegistrationMarkers.Any(marker =>
                candidate.Evidence.LocalDateTime.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return AnnouncementValidationResult.Failed("non_game_time");
        }

        var expectedPlace = AnnouncementPreParser.NormalizePlace(preParse.Place);
        var actualPlace = AnnouncementPreParser.NormalizePlace(candidate.Place);
        if (!string.Equals(expectedPlace, actualPlace, StringComparison.OrdinalIgnoreCase) ||
            !ContainsEvidence(preParse.CompactEventText, candidate.Evidence.Place))
        {
            return AnnouncementValidationResult.Failed("place_mismatch");
        }

        if (!ContainsEvidence(preParse.CompactEventText, candidate.Evidence.TournamentName) ||
            !ContainsEvidence(preParse.CompactEventText, candidate.RawTournamentName))
        {
            return AnnouncementValidationResult.Failed("name_not_evidenced");
        }

        var normalized = nameNormalizer.Normalize(candidate.RawTournamentName);
        if (string.IsNullOrWhiteSpace(normalized) ||
            !string.Equals(normalized, candidate.TournamentName.Trim(), StringComparison.Ordinal))
        {
            return AnnouncementValidationResult.Failed("name_normalization_mismatch");
        }

        if (moscowTimeZone.IsInvalidTime(candidateLocal) || moscowTimeZone.IsAmbiguousTime(candidateLocal))
        {
            return AnnouncementValidationResult.Failed("datetime_timezone_invalid");
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(candidateLocal, moscowTimeZone);
        return new AnnouncementValidationResult(true, null, new Announcement
        {
            Id = post.Id,
            TournamentName = normalized,
            Place = expectedPlace,
            DateTimeUtc = utc,
            Cost = preParse.Cost.Value
        });
    }

    private static bool ContainsEvidence(string source, string? evidence) =>
        !string.IsNullOrWhiteSpace(evidence) &&
        source.Contains(evidence.Trim(), StringComparison.OrdinalIgnoreCase);
}
