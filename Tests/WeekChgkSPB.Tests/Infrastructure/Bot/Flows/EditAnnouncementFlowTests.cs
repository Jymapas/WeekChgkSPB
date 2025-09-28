using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

public class EditAnnouncementFlowTests
{
    [Fact]
    public async Task HandleEditWaitingName_UpdatesAnnouncementAndFinishes()
    {
        using var tempDb = new SqliteTempFile();
        var repo = new AnnouncementsRepository(tempDb.Path);
        var posts = new PostsRepository(tempDb.Path);
        var footers = new FootersRepository(tempDb.Path);

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

        var flow = new EditAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal("Новое имя", repo.Get(5)!.TournamentName);
        Assert.Equal(AddStep.Done, state.Step);
        Assert.Null(state.Existing);
        Assert.False(stateStore.TryGet(userId, out _));
    }

    [Fact]
    public async Task HandleEditWaitingDateTime_InvalidFormat_KeepsWaiting()
    {
        using var tempDb = new SqliteTempFile();
        var repo = new AnnouncementsRepository(tempDb.Path);
        var posts = new PostsRepository(tempDb.Path);
        var footers = new FootersRepository(tempDb.Path);

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

        var flow = new EditAnnouncementFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.EditWaitingDateTime, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        Assert.Equal(announcement.DateTimeUtc, repo.Get(8)!.DateTimeUtc);
    }

}
