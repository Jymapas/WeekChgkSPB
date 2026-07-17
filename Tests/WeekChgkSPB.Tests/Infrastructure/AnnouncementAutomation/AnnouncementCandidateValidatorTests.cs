using System;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementCandidateValidatorTests
{
    [Theory]
    [InlineData("Carrot", "кафе \"Carrot\"", "кафе \"Carrot\"")]
    [InlineData("Тегеран", "кафе \"Тегеран\", Коломенская улица, 29", "в кафе \"Тегеран\", Коломенская улица, 29")]
    [InlineData("БарБоссов", "БарБоссов, Владимирский пр. 15", "\"БарБоссов\", Владимирский пр. 15")]
    [InlineData("Rossi's", "Клубе \"Rossi`s\"", "в Клубе \"Rossi`s\"")]
    public void Validate_AcceptsModelPlaceDecoratedWithTypeOrAddress(
        string expectedPlace,
        string modelPlace,
        string placeEvidence)
    {
        var source = $"Кубок знаний 26 июля в 19:30 {placeEvidence}";
        var preParse = new AnnouncementPreParseResult(
            true,
            null,
            source,
            1800,
            "1800 рублей с команды до 6ти человек",
            new DateTime(2026, 7, 26, 19, 30, 0),
            expectedPlace,
            source.Length);
        var candidate = new AnnouncementExtractionCandidate(
            "Кубок знаний",
            "Кубок знаний",
            modelPlace,
            "2026-07-26T19:30",
            new AnnouncementExtractionEvidence(
                "Кубок знаний",
                placeEvidence,
                "26 июля в 19:30"));
        var validator = new AnnouncementCandidateValidator(
            new TournamentNameNormalizer(),
            PostFormatter.Moscow);

        var result = validator.Validate(new Post { Id = 1 }, preParse, candidate);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(expectedPlace, result.Announcement!.Place);
    }

    [Fact]
    public void Normalize_PreservesHistoricalDecimalTournamentNumberFormat()
    {
        var normalizer = new TournamentNameNormalizer();

        var result = normalizer.Normalize("Кубок Эквестрии — 47,5 (синхрон)");

        Assert.Equal("Кубок Эквестрии-47,5", result);
    }
}
