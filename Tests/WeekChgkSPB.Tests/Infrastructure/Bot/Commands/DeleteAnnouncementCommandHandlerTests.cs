using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class DeleteAnnouncementCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public DeleteAnnouncementCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_SendsUsage()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var updater = new Mock<IChannelPostUpdater>();

        var handler = new DeleteAnnouncementCommandHandler(updater.Object);
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.Delete,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("Используй", sent[0]);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NotFound_SendsNotFoundMessage()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var updater = new Mock<IChannelPostUpdater>();

        var handler = new DeleteAnnouncementCommandHandler(updater.Object);
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.Delete} https://example.com/42",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("не найден", sent[0]);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidLink_DeletesAnnouncement()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var updater = new Mock<IChannelPostUpdater>();

        posts.Insert(new Post { Id = 5, Title = "Title", Link = "https://example.com/post", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 5,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow,
            Cost = 100
        });

        var handler = new DeleteAnnouncementCommandHandler(updater.Object);
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.Delete} https://example.com/post",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("удален", sent[0]);
        Assert.False(announcements.Exists(5));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
