using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

public class AddAnnouncementFlowTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public AddAnnouncementFlowTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleWaitingId_WhenPostExists_MovesToWaitingName()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        posts.Insert(new Post { Id = 42, Title = "t", Link = "l", Description = "d" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 123;
        const long chatId = 999;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingId;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "42",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new AddAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingName, state.Step);
        Assert.Equal(42, state.Draft.Id);
    }

    [Fact]
    public async Task HandleWaitingLines_ValidPayload_InsertsAnnouncementAndCompletes()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 7, Title = "title", Link = "link", Description = "desc" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 200;
        const long chatId = 1000;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingLines;

        var botClient = TelegramBotClientStub.Create();
        var payload = string.Join('\n', new[]
        {
            "7",
            "Чемпионат",
            "Клуб",
            "2025-08-10T19:30",
            "150"
        });

        var context = FlowTestContextFactory.CreateContext(
            botClient,
            payload,
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new AddAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Null(state.Existing);
        Assert.True(announcements.Exists(7));
        Assert.False(stateStore.TryGet(userId, out _));
    }

    [Fact]
    public async Task HandleWaitingDateTime_InvalidFormat_DoesNotAdvance()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 10, Title = "t", Link = "l", Description = "d" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 301;
        const long chatId = 1001;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingDateTime;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "не дата",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new AddAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingDateTime, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
    }

    [Fact]
    public async Task HandleWaitingLines_InvalidPayload_KeepsState()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 11, Title = "title", Link = "link", Description = "desc" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 302;
        const long chatId = 1002;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingLines;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "только одна строка",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new AddAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingLines, state.Step);
        Assert.False(announcements.Exists(11));
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
    }

    [Fact]
    public async Task HandleWaitingCost_InvalidNumber_KeepsWaiting()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 12, Title = "title", Link = "link", Description = "desc" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 303;
        const long chatId = 1003;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingCost;
        state.Draft.Id = 12;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "не число",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new AddAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingCost, state.Step);
        Assert.False(announcements.Exists(12));
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
    }

}
