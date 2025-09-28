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
<a href="https://example.com/1">First - Club1 (18:00) 100 р.</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200 р.</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150 р.</a>
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
<a href="https://example.com/1">First - Club1 (18:00) 100 р.</a>
<a href="https://example.com/2">Second - Club2 (20:30) 200 р.</a>

<b>11 января (сб)</b>
<a href="https://example.com/3">Third - Club3 (15:00) 150 р.</a>

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
