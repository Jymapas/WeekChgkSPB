using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class TournamentNameNormalizerTests
{
    [Theory]
    [InlineData("Holiday Cup: часть первая (синхрон)", "Holiday Cup-1")]
    [InlineData("Holiday Cup: часть третья (синхрон)", "Holiday Cup-3")]
    [InlineData("Holiday Cup: часть четвёртая", "Holiday Cup-4")]
    public void Normalize_ConvertsRussianPartSuffixToNumber(string rawName, string expected)
    {
        var result = new TournamentNameNormalizer().Normalize(rawName);

        Assert.Equal(expected, result);
    }
}
