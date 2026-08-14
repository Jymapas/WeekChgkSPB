using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.ReplyMarkups;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class ForcePostCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public ForcePostCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_WithAnnouncements_SendsPreviewWithConfirmButtons()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 1, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 1,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow.AddHours(1),
            Cost = 100
        });

        var handler = new ForcePostCommandHandler("@channel");
        var (context, sent, botMock) = CommandTestContextFactory.Create(
            BotCommands.ForcePost,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("Tournament", sent[0]);

        var invocation = Assert.Single(botMock.Invocations);
        var request = Assert.IsType<SendMessageRequest>(invocation.Arguments[0]);
        var keyboard = Assert.IsType<InlineKeyboardMarkup>(request.ReplyMarkup);
        var callbackData = keyboard.InlineKeyboard.SelectMany(row => row).Select(b => b.CallbackData).ToList();
        Assert.Contains("forcepost_confirm", callbackData);
        Assert.Contains("forcepost_cancel", callbackData);
    }

    [Fact]
    public async Task HandleAsync_WhenNoAnnouncements_SendsNotice()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new ForcePostCommandHandler("@channel");
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.ForcePost,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("анонсов нет", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenChannelNotConfigured_SendsNotice()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new ForcePostCommandHandler(null);
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.ForcePost,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("не настроен", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenNotAdminChat_SendsAccessDenied()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new ForcePostCommandHandler("@channel");
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.ForcePost,
            announcements,
            posts,
            footers,
            helper,
            stateStore,
            isAdminChat: false);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("Доступ запрещен", sent[0]);
    }
}
