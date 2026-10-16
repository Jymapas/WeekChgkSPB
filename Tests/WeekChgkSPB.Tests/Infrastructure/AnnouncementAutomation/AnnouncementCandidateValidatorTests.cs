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

    [Theory]
    [InlineData("27 октября в клубе «Rossi's». Начало регистрации в 19:15, первый вопрос в 19:30", true)]
    [InlineData("27 октября ... в 19:30", false)]
    [InlineData("Начало регистрации в 19:15", false)]
    public void Validate_RequiresVerbatimEvidenceAndAllowsRegistrationAlongsideGameTime(string evidence, bool expected)
    {
        const string source = "Турнир 27 октября в клубе «Rossi's». Начало регистрации в 19:15, первый вопрос в 19:30";
        var preParse = new AnnouncementPreParseResult(true, null, source, 1800, "1800 ₽",
            new DateTime(2026, 10, 27, 19, 30, 0), "Rossi's", source.Length);
        var candidate = new AnnouncementExtractionCandidate("Турнир", "Турнир", "Rossi's",
            "2026-10-27T19:30", new AnnouncementExtractionEvidence("Турнир", "Rossi's", evidence));
        var result = new AnnouncementCandidateValidator(new TournamentNameNormalizer(), PostFormatter.Moscow)
            .Validate(new Post { Id = 1 }, preParse, candidate);
        Assert.Equal(expected, result.Success);
    }

    [Theory]
    [InlineData("Ночь кошмаров — 2026 (синхрон)", "Ночь кошмаров — 2026", "Ночь кошмаров")]
    [InlineData("Sims To Be: Vol. 25", "Sims To Be-25", "Sims To Be-25")]
    [InlineData("Второй этап отбора на Чемпионат России среди студентов по интеллектуальным играм",
        "Второй этап отбора на Чемпионат России среди студентов по интеллектуальным играм", "Отбор на СтудЧР-2")]
    public void Validate_SavesCanonicalLocalName(string raw, string model, string expected)
    {
        var source = $"{raw} 27 октября 19:30 в клубе «Rossi's»";
        var preParse = new AnnouncementPreParseResult(true, null, source, 1800, "1800 ₽",
            new DateTime(2026, 10, 27, 19, 30, 0), "Rossi's", source.Length);
        var candidate = new AnnouncementExtractionCandidate(raw, model, "Rossi's",
            "2026-10-27T19:30", new AnnouncementExtractionEvidence(raw, "Rossi's", "27 октября 19:30"));
        var result = new AnnouncementCandidateValidator(new TournamentNameNormalizer(), PostFormatter.Moscow)
            .Validate(new Post { Id = 1 }, preParse, candidate);
        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(expected, result.Announcement!.TournamentName);
    }

    [Fact]
    public void Normalize_PreservesHistoricalDecimalTournamentNumberFormat()
    {
        var normalizer = new TournamentNameNormalizer();

        var result = normalizer.Normalize("Кубок Эквестрии — 47,5 (синхрон)");

        Assert.Equal("Кубок Эквестрии-47,5", result);
    }
}
