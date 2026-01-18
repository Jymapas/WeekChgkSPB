using WeekChgkSPB;

namespace WeekChgkSPB.Tests.Infrastructure;

public class LinkNormalizerTests
{
    [Fact]
    public void Normalize_LiveJournalId_ReturnsCanonicalUrl()
    {
        var normalized = LinkNormalizer.Normalize("123");

        Assert.Equal("https://chgk-spb.livejournal.com/123.html", normalized);
    }

    [Fact]
    public void Normalize_RemovesTrackingParamsAndFragment()
    {
        var input = "https://Example.com/Post/?utm_source=aaa&b=1&gclid=2#section";

        var normalized = LinkNormalizer.Normalize(input);

        Assert.Equal("https://example.com/Post?b=1", normalized);
    }
}
