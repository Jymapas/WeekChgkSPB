using System;
using System.Threading.Tasks;
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

        var handler = new DeleteAnnouncementCommandHandler();
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

        var handler = new DeleteAnnouncementCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.Delete} 42",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("не найден", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_ValidId_DeletesAnnouncement()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 5, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 5,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow,
            Cost = 100
        });

        var handler = new DeleteAnnouncementCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.Delete} 5",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("удален", sent[0]);
        Assert.False(announcements.Exists(5));
    }
}
