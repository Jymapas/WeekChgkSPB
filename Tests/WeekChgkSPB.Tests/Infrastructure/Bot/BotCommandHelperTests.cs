using System;
using System.Globalization;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class BotCommandHelperTests
{
    private readonly BotCommandHelper _helper = new(PostFormatter.Moscow);

    [Theory]
    [InlineData("2025-08-10T19:30")]
    [InlineData("2025-08-10 19:30")]
    [InlineData("2025-08-10T19:30:00")]
    public void TryParseDateTime_IsoFormats_ReturnsUtc(string input)
    {
        var success = _helper.TryParseDateTime(input, out var utc);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void TryParseDateTime_HumanSeparatedLines_ReturnsUtc()
    {
        var input = "22 сентября\n19:30";
        var success = _helper.TryParseDateTime(input, out var utc);

        Assert.True(success);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, PostFormatter.Moscow);
        Assert.Equal(22, local.Day);
        Assert.Equal(19, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Fact]
    public void TryParseDateTime_RussianSingleLine_ReturnsUtc()
    {
        var input = "22 сентября 19:30";
        var success = _helper.TryParseDateTime(input, out var utc);

        Assert.True(success);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, PostFormatter.Moscow);
        Assert.Equal(22, local.Day);
        Assert.Equal(19, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не дата")]
    [InlineData("32 декабря 25:61")]
    public void TryParseDateTime_InvalidFormats_ReturnsFalse(string input)
    {
        var success = _helper.TryParseDateTime(input, out var utc);

        Assert.False(success);
        Assert.Equal(default, utc);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_FiveLines_Succeeds()
    {
        var lines = string.Join('\n', new[]
        {
            "100",
            "Турнир",
            "Клуб",
            "2025-08-10T19:30",
            "150"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out var announcement, out var error);

        Assert.True(result);
        Assert.Equal(100, announcement.Id);
        Assert.Equal("Турнир", announcement.TournamentName);
        Assert.Equal("Клуб", announcement.Place);
        Assert.Equal(150, announcement.Cost);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_SixLines_Succeeds()
    {
        var lines = string.Join('\n', new[]
        {
            "101",
            "Турнир",
            "Клуб",
            "22 сентября",
            "19:30",
            "200"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out var announcement, out var error);

        Assert.True(result);
        Assert.Equal(101, announcement.Id);
        Assert.Equal(200, announcement.Cost);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_TooManyLines_Fails()
    {
        var lines = string.Join('\n', new[]
        {
            "1",
            "a",
            "b",
            "c",
            "d",
            "e",
            "f"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out _, out var error);

        Assert.False(result);
        Assert.Contains("Ожидаю 5 или 6 строк", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_EmptyName_Fails()
    {
        var lines = string.Join('\n', new[]
        {
            "1",
            " ",
            "place",
            "2025-08-10T19:30",
            "100"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out _, out var error);

        Assert.False(result);
        Assert.Contains("название", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_InvalidCost_Fails()
    {
        var lines = string.Join('\n', new[]
        {
            "1",
            "name",
            "place",
            "2025-08-10T19:30",
            "abc"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out _, out var error);

        Assert.False(result);
        Assert.Contains("стоимость", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildAnnouncementFromLines_LessThanFiveLines_Fails()
    {
        var lines = string.Join('\n', new[]
        {
            "1",
            "name",
            "place",
            "2025-08-10T19:30"
        });

        var result = _helper.TryBuildAnnouncementFromLines(lines, out _, out var error);

        Assert.False(result);
        Assert.Contains("Нужно передать 5 или 6 строк", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDateRangeOrDefault_ParsesExplicitStart()
    {
        var nowUtc = DateTime.UtcNow;
        var start = nowUtc.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = nowUtc.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var command = $"/makepost {start} {end}";

        var (fromUtc, toUtc) = _helper.ResolveDateRangeOrDefault(command);

        Assert.Null(toUtc);
        Assert.Equal(DateTime.Parse(start, CultureInfo.InvariantCulture), TimeZoneInfo.ConvertTimeFromUtc(fromUtc, PostFormatter.Moscow).Date);
    }

    [Fact]
    public void ResolveDateRangeOrDefault_FallbackToToday()
    {
        var command = "/makepost";

        var (fromUtc, toUtc) = _helper.ResolveDateRangeOrDefault(command);

        var moscowNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow).Date;

        Assert.Equal(moscowNow, TimeZoneInfo.ConvertTimeFromUtc(fromUtc, PostFormatter.Moscow).Date);
        Assert.Null(toUtc);
    }

    [Theory]
    [InlineData("/cmd", "/cmd", true)]
    [InlineData("/cmd", "/cmd арг", true)]
    [InlineData("/cmd", "/cmd@MyBot арг", true)]
    [InlineData("/cmd", "/cmd@MyBot", true)]
    [InlineData("/cmd", "/cmdExtra", false)]
    [InlineData("/cmd", "/c md", false)]
    [InlineData("/cmd", null, false)]
    public void IsCommand_HandlesTelegramFormats(string command, string? text, bool expected)
    {
        var result = _helper.IsCommand(text, command);

        Assert.Equal(expected, result);
    }
}
