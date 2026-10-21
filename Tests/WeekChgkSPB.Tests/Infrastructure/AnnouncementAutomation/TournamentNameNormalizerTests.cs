using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class TournamentNameNormalizerTests
{
    [Theory]
    [InlineData("ОВСЧ 1", "ОВСЧ-1")]
    [InlineData("ОВСЧ. Сезон 2026/2027. 1 этап", "ОВСЧ-1")]
    [InlineData("ОВСЧ-1", "ОВСЧ-1")]
    [InlineData("ОВСЧ. Сезон 2026/2027. 2 этап", "ОВСЧ-2")]
    [InlineData("ОВСЧ № 3 (синхрон)", "ОВСЧ-3")]
    [InlineData("ОВСЧ", "ОВСЧ")]
    public void Normalize_PreservesOvsChStageAndRemovesSeason(string rawName, string expected)
    {
        Assert.Equal(expected, new TournamentNameNormalizer().Normalize(rawName));
    }

    [Theory]
    [InlineData("Sims To Be: Vol. 25", "Sims To Be-25")]
    [InlineData("Ночь кошмаров — 2026 (синхрон)", "Ночь кошмаров")]
    [InlineData("Топ-1000 XXVI. Тайный Топтышка (Финал ЛВ)", "Топ-1000 XXVI. Тайный Топтышка")]
    [InlineData("Второй этап отбора на Чемпионат России среди студентов по интеллектуальным играм", "Отбор на СтудЧР-2")]
    [InlineData("Holiday Cup: часть первая (синхрон)", "Holiday Cup-1")]
    [InlineData("Holiday Cup: часть третья (синхрон)", "Holiday Cup-3")]
    [InlineData("Holiday Cup: часть четвёртая", "Holiday Cup-4")]
    [InlineData("Кубок Бесконечности: третий этап", "Кубок Бесконечности-3")]
    [InlineData("Кубок Бесконечности: четвёртый этап", "Кубок Бесконечности-4")]
    public void Normalize_ConvertsRussianPartSuffixToNumber(string rawName, string expected)
    {
        var result = new TournamentNameNormalizer().Normalize(rawName);

        Assert.Equal(expected, result);
    }
}
