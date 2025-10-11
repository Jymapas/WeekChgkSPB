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
            timeOfDay: new TimeSpan(12, 0, 0));
        var tz = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var publisher = new ScheduledPostPublisher(
            announcements,
            footers,
            history,
            posts,
            botMock.Object,
            channelId: "-100123",
            options,
            tz,
            announcementRetentionDays: 7);

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
        var posts = _fixture.CreatePostsRepository();
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
            timeOfDay: new TimeSpan(12, 0, 0));
        var tz = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var publisher = new ScheduledPostPublisher(
            announcements,
            footers,
            history,
            posts,
            botMock.Object,
            channelId: "-100123",
            options,
            tz,
            announcementRetentionDays: 7);

        var dueUtc = new DateTime(2025, 1, 6, 7, 0, 0, DateTimeKind.Utc);
        await publisher.TryPublishAsync(dueUtc, CancellationToken.None);
    }

    [Fact]
    public async Task TryPublishAsync_CleansUpStaleDataAfterPosting()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        var staleId = 201;
        var currentId = 202;
        var orphanId = 303;

        posts.Insert(new Post { Id = staleId, Title = "Old", Link = "https://example.com/old", Description = "old desc" });
        posts.Insert(new Post { Id = currentId, Title = "New", Link = "https://example.com/new", Description = "new desc" });
        posts.Insert(new Post { Id = orphanId, Title = "Orphan", Link = "https://example.com/orphan", Description = "orphan desc" });

        announcements.Insert(new Announcement
        {
            Id = staleId,
            TournamentName = "Old Cup",
            Place = "Somewhere",
            DateTimeUtc = new DateTime(2024, 12, 20, 10, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });
        announcements.Insert(new Announcement
        {
            Id = currentId,
            TournamentName = "Fresh Cup",
            Place = "Downtown",
            DateTimeUtc = new DateTime(2025, 1, 7, 10, 0, 0, DateTimeKind.Utc),
            Cost = 200
        });
        footers.Insert("footer");

        var botMock = new Mock<ITelegramBotClient>();
        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Text = "sent" });

        var options = new ChannelPostScheduleOptions(
            postsPerWeek: 1,
            days: new[] { DayOfWeek.Monday },
            timeOfDay: new TimeSpan(12, 0, 0));
        var tz = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var publisher = new ScheduledPostPublisher(
            announcements,
            footers,
            history,
            posts,
            botMock.Object,
            channelId: "-100123",
            options,
            tz,
            announcementRetentionDays: 7);

        var dueUtc = new DateTime(2025, 1, 6, 7, 0, 0, DateTimeKind.Utc);
        await publisher.TryPublishAsync(dueUtc, CancellationToken.None);

        Assert.False(announcements.Exists(staleId));
        Assert.True(announcements.Exists(currentId));
        Assert.False(posts.Exists(staleId));
        Assert.True(posts.Exists(currentId));
        Assert.False(posts.Exists(orphanId));
    }
}
