using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Rss;

public sealed class RssFetcherTests
{
    [Fact]
    public void Parse_ReadsLiveJournalAuthorAndExistingPostFields()
    {
        var post = Parse("<author>alfakentavra</author><lj:poster>alfakentavra</lj:poster><lj:posterid>102839315</lj:posterid>");

        Assert.Equal(3417488, post.Id);
        Assert.Equal("Турнир", post.Title);
        Assert.Equal("Описание", post.Description);
        Assert.Equal(102839315, post.LjAuthorId);
        Assert.Equal("alfakentavra", post.LjAuthorUsername);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<lj:poster>alfakentavra</lj:poster>")]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:posterid>0</lj:posterid>")]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:posterid>-1</lj:posterid>")]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:posterid>999999999999999999999</lj:posterid>")]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:posterid>1</lj:posterid><lj:posterid>2</lj:posterid>")]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:poster>other</lj:poster><lj:posterid>1</lj:posterid>")]
    [InlineData("<author>other</author><lj:poster>alfakentavra</lj:poster><lj:posterid>1</lj:posterid>")]
    [InlineData("<lj:poster>https://example.org</lj:poster><lj:posterid>1</lj:posterid>")]
    [InlineData("<lj:poster><name>alfakentavra</name></lj:poster><lj:posterid>1</lj:posterid>")]
    public void Parse_MissingInvalidOrConflictingAuthor_DoesNotGrantAuthorship(string metadata)
    {
        var post = Parse(metadata);

        Assert.Null(post.LjAuthorId);
        Assert.Null(post.LjAuthorUsername);
        Assert.Equal(3417488, post.Id);
    }

    [Fact]
    public void Parse_IdenticalRepeatedMetadataAndCase_AreAccepted()
    {
        var post = Parse("<lj:poster> ALFAKENTAVRA </lj:poster><lj:poster>alfakentavra</lj:poster>" +
                         "<lj:posterid>102839315</lj:posterid><lj:posterid>102839315</lj:posterid>");

        Assert.Equal(102839315, post.LjAuthorId);
        Assert.Equal("alfakentavra", post.LjAuthorUsername);
    }

    [Theory]
    [InlineData("<lj:poster>alfakentavra</lj:poster><lj:posterid>999</lj:posterid>")]
    [InlineData("<lj:poster>other</lj:poster><lj:posterid>102839315</lj:posterid>")]
    [InlineData("")]
    [InlineData("<lj:poster>alfakentavra</lj:poster>")]
    public void Parse_DuplicateSourceWithConflictingOrMissingAuthor_ClearsEveryOccurrence(string otherMetadata)
    {
        const string metadata = "<lj:poster>alfakentavra</lj:poster><lj:posterid>102839315</lj:posterid>";
        var posts = ParseItems(
            (metadata, "http://chgk-spb.livejournal.com/3417488.html?utm_source=test#comments"),
            (otherMetadata, "https://chgk-spb.livejournal.com/3417488.html"),
            (metadata, "https://CHGK-SPB.livejournal.com/3417488.html/"));

        Assert.Equal(3, posts.Count);
        Assert.All(posts, post =>
        {
            Assert.Null(post.LjAuthorId);
            Assert.Null(post.LjAuthorUsername);
        });
    }

    [Fact]
    public void Parse_IdenticalSourceAuthorsAndOtherHosts_DoNotConflict()
    {
        const string metadata = "<lj:poster>alfakentavra</lj:poster><lj:posterid>102839315</lj:posterid>";
        var posts = ParseItems(
            (metadata, "https://chgk-spb.livejournal.com/3417488.html"),
            (metadata, "http://chgk-spb.livejournal.com/3417488.html?tracking=1"),
            ("<lj:poster>other</lj:poster><lj:posterid>999</lj:posterid>", "https://example.org/3417488.html"));

        Assert.All(posts.Take(2), post =>
        {
            Assert.Equal(102839315, post.LjAuthorId);
            Assert.Equal("alfakentavra", post.LjAuthorUsername);
        });
    }

    private static Post Parse(string metadata)
    {
        return Assert.Single(ParseItems((metadata, "https://chgk-spb.livejournal.com/3417488.html")));
    }

    private static IReadOnlyList<Post> ParseItems(params (string Metadata, string Link)[] items)
    {
        var xml = """
            <rss version="2.0" xmlns:lj="http://www.livejournal.org/rss/lj/1.0/">
              <channel><title>Community</title><link>https://chgk-spb.livejournal.com</link><description>Feed</description>
            """ + string.Join("", items.Select(item =>
                $"<item><title>Турнир</title><link>{item.Link}</link><description>Описание</description>{item.Metadata}</item>")) +
            "</channel></rss>";
        using var reader = XmlReader.Create(new StringReader(xml));
        return RssFetcher.Parse(reader);
    }
}
