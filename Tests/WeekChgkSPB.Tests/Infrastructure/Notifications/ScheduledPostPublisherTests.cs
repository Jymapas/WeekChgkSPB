using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public class ScheduledPostPublisherTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public ScheduledPostPublisherTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryPublishAsync_SendsMessageOnce_WhenScheduleMatches()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        posts.Insert(new Post { Id = 101, Title = "Title", Link = "https://example.com", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 101,
            TournamentName = "City Cup",
            Place = "Downtown",
            DateTimeUtc = new DateTime(2025, 1, 8, 15, 0, 0, DateTimeKind.Utc),
            Cost = 500
        });
        footers.Insert("footer");

        var sent = new List<string>();
        var botMock = new Mock<ITelegramBotClient>();
        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<Message> request, CancellationToken _) =>
            {
                var textProp = request.GetType().GetProperty("Text");
                var sentText = textProp?.GetValue(request) as string ?? string.Empty;
                sent.Add(sentText);
                return new Message { Text = sentText };
            });

        var options = new ChannelPostScheduleOptions(
            postsPerWeek: 2,
            days: new[] { DayOfWeek.Monday, DayOfWeek.Thursday },
            timeOfDay: new TimeSpan(12, 0, 0),
            lookaheadDays: 14);
        var tz = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var publisher = new ScheduledPostPublisher(
            announcements,
            footers,
            history,
            botMock.Object,
            channelId: "-100123",
            options,
            tz);

        var beforeUtc = new DateTime(2025, 1, 6, 6, 59, 0, DateTimeKind.Utc);
        await publisher.TryPublishAsync(beforeUtc, CancellationToken.None);
        Assert.Empty(sent);

        var dueUtc = new DateTime(2025, 1, 6, 7, 0, 0, DateTimeKind.Utc);
        await publisher.TryPublishAsync(dueUtc, CancellationToken.None);
        Assert.Single(sent);
        Assert.Contains("City Cup", sent[0]);

        await publisher.TryPublishAsync(dueUtc.AddMinutes(5), CancellationToken.None);
        Assert.Single(sent);
    }

    [Fact]
    public async Task TryPublishAsync_SkipsWhenNoAnnouncements()
    {
        _fixture.Reset();
        _ = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        var botMock = new Mock<ITelegramBotClient>();
        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Should not send"));

        var options = new ChannelPostScheduleOptions(
            postsPerWeek: 1,
            days: new[] { DayOfWeek.Monday },
            timeOfDay: new TimeSpan(12, 0, 0),
            lookaheadDays: 14);
        var tz = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var publisher = new ScheduledPostPublisher(
            announcements,
            footers,
            history,
            botMock.Object,
            channelId: "-100123",
            options,
            tz);

        var dueUtc = new DateTime(2025, 1, 6, 7, 0, 0, DateTimeKind.Utc);
        await publisher.TryPublishAsync(dueUtc, CancellationToken.None);
    }
}
