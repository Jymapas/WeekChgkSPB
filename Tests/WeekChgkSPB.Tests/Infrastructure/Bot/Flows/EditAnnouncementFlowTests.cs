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

public class EditAnnouncementFlowTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public EditAnnouncementFlowTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleEditWaitingName_UpdatesAnnouncementAndFinishes()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 5, Title = "Title", Link = "Link", Description = "Desc" });
        var announcement = new Announcement
        {
            Id = 5,
            TournamentName = "Old Name",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow,
            Cost = 100
        };
        repo.Insert(announcement);

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 555;
        const long chatId = 42;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.EditWaitingName;
        state.Existing = repo.Get(5);

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "Новое имя",
            chatId,
            userId,
            repo,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        updater
            .Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var flow = new EditAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal("Новое имя", repo.Get(5)!.TournamentName);
        Assert.Equal(AddStep.Done, state.Step);
        Assert.Null(state.Existing);
        Assert.False(stateStore.TryGet(userId, out _));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleEditWaitingDateTime_InvalidFormat_KeepsWaiting()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 8, Title = "T", Link = "L", Description = "D" });
        var announcement = new Announcement
        {
            Id = 8,
            TournamentName = "Name",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow,
            Cost = 77
        };
        repo.Insert(announcement);

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 556;
        const long chatId = 43;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.EditWaitingDateTime;
        state.Existing = repo.Get(8);

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "неверная дата",
            chatId,
            userId,
            repo,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new EditAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.EditWaitingDateTime, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        Assert.Equal(announcement.DateTimeUtc, repo.Get(8)!.DateTimeUtc);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleEditWaitingCost_InvalidNumber_KeepsWaiting()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 9, Title = "T", Link = "L", Description = "D" });
        var announcement = new Announcement
        {
            Id = 9,
            TournamentName = "Name",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow,
            Cost = 50
        };
        repo.Insert(announcement);

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 557;
        const long chatId = 44;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.EditWaitingCost;
        state.Existing = repo.Get(9);

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "не число",
            chatId,
            userId,
            repo,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new EditAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.EditWaitingCost, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        Assert.Equal(50, repo.Get(9)!.Cost);
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleEdit_NoExistingAnnouncement_NotifiesAndResets()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        posts.Insert(new Post { Id = 10, Title = "T", Link = "L", Description = "D" });

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 600;
        const long chatId = 700;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.EditWaitingName;
        state.Existing = null;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "Новое имя",
            chatId,
            userId,
            repo,
            posts,
            footers,
            stateStore,
            helper);

        var updater = new Mock<IChannelPostUpdater>();
        var flow = new EditAnnouncementFlow(updater.Object);

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.None, state.Step);
        Assert.False(stateStore.TryGet(userId, out _));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

}
