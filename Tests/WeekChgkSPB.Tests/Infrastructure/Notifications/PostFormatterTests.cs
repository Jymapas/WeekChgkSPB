using System;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public class PostFormatterTests
{
    private static readonly AnnouncementRow[] SampleRows =
    [
        new(1, "First", "Club1", new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc), 100, "https://example.com/1"),
        new(2, "Second", "Club2", new DateTime(2025, 1, 10, 17, 30, 0, DateTimeKind.Utc), 200, "https://example.com/2"),
        new(3, "Third", "Club3", new DateTime(2025, 1, 11, 12, 0, 0, DateTimeKind.Utc), 150, "https://example.com/3"),
    ];

    [Fact]
    public void BuildScheduleMessage_ProducesExpectedSnapshot()
    {
        var result = PostFormatter.BuildScheduleMessage(SampleRows);

        const string expected = """
Продолжаем вести список синхронов в Санкт-Петербурге.

<b>10 января (пт)</b>
<a href="https://example.com/1">First - Club1 (18:00) 100₽</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200₽</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150₽</a>
""";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildScheduleHtml_UsesLiveJournalHeader()
    {
        var result = PostFormatter.BuildScheduleHtml(SampleRows);

        const string expected = """
Копия поста выкладывается в <a href="https://t.me/WeekChgkSPB" rel="nofollow">Телеграм-канал</a>.

<b>10 января (пт)</b>
<a href="https://example.com/1">First - Club1 (18:00) 100₽</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200₽</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150₽</a>
""";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildScheduleMessage_WithUpdate_AddsUpdatedLine()
    {
        var updated = new DateTime(2025, 9, 19);

        var result = PostFormatter.BuildScheduleMessage(SampleRows, updatedAtLocal: updated);

        const string expected = """
Продолжаем вести список синхронов в Санкт-Петербурге.
Пост обновлён 19.09.2025

<b>10 января (пт)</b>
<a href="https://example.com/1">First - Club1 (18:00) 100₽</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200₽</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150₽</a>
""";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildScheduleMessage_IncludesFooters()
    {
        var footers = new[] { "<i>Footer1</i>", "Footer2" };
        var result = PostFormatter.BuildScheduleMessage(SampleRows, footers);

        const string expected = """
Продолжаем вести список синхронов в Санкт-Петербурге.

<b>10 января (пт)</b>
<a href="https://example.com/1">First - Club1 (18:00) 100₽</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200₽</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150₽</a>

<i>Footer1</i>
Footer2
""";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void WrapAsCodeForTelegram_EscapesHtml()
    {
        var text = "<b>Test & text</b>";

        var wrapped = PostFormatter.WrapAsCodeForTelegram(text);

        Assert.Equal("<code>&lt;b&gt;Test &amp; text&lt;/b&gt;</code>", wrapped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Schedule_EscapesAnnouncementTextAndLinkButPreservesAdminFooter(bool liveJournal)
    {
        AnnouncementRow[] rows = [new(1, "Cup </a><b>fake</b>", "Bar & <Hall>",
            new DateTime(2026, 10, 22, 15, 0, 0, DateTimeKind.Utc), 0,
            "https://example.org/?q=\"&x=<tag>", "<i>free</i>")];
        string[] footers = ["<i>Admin footer</i>"];

        var result = liveJournal ? PostFormatter.BuildScheduleHtml(rows, footers) :
            PostFormatter.BuildScheduleMessage(rows, footers);

        Assert.Contains("href=\"https://example.org/?q=&quot;&amp;x=&lt;tag&gt;\"", result);
        Assert.Contains("Cup &lt;/a&gt;&lt;b&gt;fake&lt;/b&gt; - Bar &amp; &lt;Hall&gt; (18:00) &lt;i&gt;free&lt;/i&gt;</a>", result);
        Assert.Contains("<i>Admin footer</i>", result);
        Assert.DoesNotContain("<b>fake</b>", result);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//example.org/path")]
    public void Schedule_UnsafeLinksRenderAsPlainText(string link)
    {
        var row = SampleRows[0] with { Link = link };
        foreach (var result in new[] { PostFormatter.BuildScheduleMessage([row]), PostFormatter.BuildScheduleHtml([row]) })
        {
            Assert.Contains("First - Club1 (18:00) 100₽", result);
            Assert.DoesNotContain(link, result);
            Assert.DoesNotContain(">First", result);
        }
    }

    [Fact]
    public void WrapAsCodeForTelegram_TruncatesWhenTooLong()
    {
        var text = new string('a', 50);

        var wrapped = PostFormatter.WrapAsCodeForTelegram(text, tgLimit: 20);

        var budget = 20 - "</code>".Length - 1;
        var expected = "<code>" + new string('a', budget) + "…</code>";
        Assert.Equal(expected, wrapped);
    }
}
