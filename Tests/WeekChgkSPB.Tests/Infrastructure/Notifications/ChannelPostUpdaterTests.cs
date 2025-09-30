using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public class ChannelPostUpdaterTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public ChannelPostUpdaterTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpdateLastPostAsync_KeepsPastAnnouncements()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        posts.Insert(new Post { Id = 1, Title = "mon", Link = "https://example.com/m", Description = "desc" });
        posts.Insert(new Post { Id = 2, Title = "tue", Link = "https://example.com/t", Description = "desc" });

        var mondayLocal = new DateTime(2025, 1, 6, 18, 0, 0, DateTimeKind.Unspecified);
        var mondayUtc = TimeZoneInfo.ConvertTimeToUtc(mondayLocal, PostFormatter.Moscow);
        announcements.Insert(new Announcement
        {
            Id = 1,
            TournamentName = "Monday Event",
            Place = "Place",
            DateTimeUtc = mondayUtc,
            Cost = 100
        });

        var tuesdayLocal = new DateTime(2025, 1, 7, 18, 0, 0, DateTimeKind.Unspecified);
        var tuesdayUtc = TimeZoneInfo.ConvertTimeToUtc(tuesdayLocal, PostFormatter.Moscow);
        announcements.Insert(new Announcement
        {
            Id = 2,
            TournamentName = "Tuesday Event",
            Place = "Place",
            DateTimeUtc = tuesdayUtc,
            Cost = 200
        });

        var scheduledLocal = new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Unspecified);
        var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, PostFormatter.Moscow);
        history.MarkPosted(scheduledUtc, scheduledUtc, messageId: 42);

        var botMock = new Mock<ITelegramBotClient>();
        string? edited = null;
        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<Message> request, CancellationToken _) =>
            {
                var textProp = request.GetType().GetProperty("Text");
                edited = textProp?.GetValue(request) as string;
                return new Message { Text = edited ?? string.Empty };
            });

        var updater = new ChannelPostUpdater(
            announcements,
            footers,
            history,
            botMock.Object,
            "@test");

        await updater.UpdateLastPostAsync(CancellationToken.None);

        Assert.NotNull(edited);
        Assert.Contains("Monday Event", edited!, StringComparison.Ordinal);
        Assert.Contains("Tuesday Event", edited!, StringComparison.Ordinal);
    }
}
