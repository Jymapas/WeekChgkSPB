using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingName, state.Step);
        Assert.Equal(42, state.Draft.Id);
    }

    [Fact]
    public async Task HandleWaitingId_NonNumericId_KeepsWaitingId()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 130;
        const long chatId = 910;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingId;

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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingId, state.Step);
        Assert.Equal(0, state.Draft.Id);
    }

    [Fact]
    public async Task HandleWaitingId_PostMissing_KeepsWaitingId()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 131;
        const long chatId = 911;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingId;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "123",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingId, state.Step);
        Assert.False(announcements.Exists(123));
    }

    [Fact]
    public async Task HandleWaitingId_DuplicateAnnouncement_ResetsState()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 200, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 200,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 132;
        const long chatId = 912;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingId;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "200",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.None, state.Step);
        Assert.True(stateStore.TryGet(userId, out var stored));
        Assert.Same(state, stored);
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Null(state.Existing);
        Assert.True(announcements.Exists(7));
        Assert.False(stateStore.TryGet(userId, out _));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleWaitingLines_PostMissing_KeepsState()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 400;
        const long chatId = 1100;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingLines;

        var botClient = TelegramBotClientStub.Create();
        var payload = string.Join('\n', new[]
        {
            "100",
            "Турнир",
            "Место",
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.False(announcements.Exists(100));
        Assert.Equal(AddStep.WaitingLines, state.Step);
        Assert.True(stateStore.TryGet(userId, out var stored));
        Assert.Same(state, stored);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HandleWaitingLines_DuplicateAnnouncement_ReportsAndKeepsState()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 101, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 101,
            TournamentName = "Existing",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 401;
        const long chatId = 1101;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingLines;

        var botClient = TelegramBotClientStub.Create();
        var payload = string.Join('\n', new[]
        {
            "101",
            "Новый",
            "Место",
            "2025-08-10T19:30",
            "200"
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingLines, state.Step);
        Assert.True(stateStore.TryGet(userId, out var stored));
        Assert.Same(state, stored);
        var storedAnnouncement = announcements.Get(101);
        Assert.NotNull(storedAnnouncement);
        Assert.Equal("Existing", storedAnnouncement!.TournamentName);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingDateTime, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HandleWaitingDateTime_ValidInput_MovesToWaitingCost()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 310, Title = "t", Link = "l", Description = "d" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 304;
        const long chatId = 1004;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingDateTime;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "2025-08-10T19:30",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingCost, state.Step);
        Assert.NotEqual(default, state.Draft.DateTimeUtc);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingLines, state.Step);
        Assert.False(announcements.Exists(11));
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
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

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.WaitingCost, state.Step);
        Assert.False(announcements.Exists(12));
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HandleWaitingCost_ValidNumber_SavesAndClearsState()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 15, Title = "title", Link = "link", Description = "desc" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 305;
        const long chatId = 1005;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.WaitingCost;
        state.Draft.Id = 15;
        state.Draft.TournamentName = "Tournament";
        state.Draft.Place = "Place";
        state.Draft.DateTimeUtc = new DateTime(2025, 9, 1, 18, 0, 0, DateTimeKind.Utc);

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "350",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new AddAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.True(announcements.Exists(15));
        Assert.Equal(AddStep.Done, state.Step);
        Assert.False(stateStore.TryGet(userId, out _));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

}
