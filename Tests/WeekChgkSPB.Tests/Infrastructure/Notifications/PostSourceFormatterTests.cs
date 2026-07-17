using System;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public sealed class PostSourceFormatterTests
{
    [Fact]
    public void Format_CombinesIdTitleAndCleanBodyInOnePlainTextMessage()
    {
        var post = new Post
        {
            Id = 42,
            Title = "<b>Турнир &amp; игра</b>",
            Description = "Первая строка<br><script>не команда</script>Вторая строка",
            Link = "https://example.com/42"
        };

        var result = PostSourceFormatter.Format(post);

        Assert.Contains("ID: <code>42</code>", result);
        Assert.Contains("Заголовок: <code>Турнир &amp; игра</code>", result);
        Assert.Contains("Первая строка", result);
        Assert.Contains("Вторая строка", result);
        Assert.DoesNotContain("<b>", result);
        Assert.DoesNotContain("<script>", result);
        Assert.Contains(post.Link, result);
    }

    [Fact]
    public void Format_TruncatesOversizedPostWithinTelegramLimit()
    {
        var post = new Post
        {
            Id = 43,
            Title = "Большой пост",
            Description = new string('я', 10_000),
            Link = "https://example.com/43"
        };

        var result = PostSourceFormatter.Format(post);

        Assert.True(result.Length <= 4_000);
        Assert.Contains("[Текст сокращён — полный текст доступен по ссылке]", result);
        Assert.EndsWith(post.Link, result);
    }
}
