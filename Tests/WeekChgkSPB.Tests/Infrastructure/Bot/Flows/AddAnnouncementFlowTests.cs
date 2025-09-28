using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

public class AddAnnouncementFlowTests
{
    [Fact]
    public async Task HandleWaitingId_WhenPostExists_MovesToWaitingName()
    {
        using var tempDb = new SqliteTempFile();
        var posts = new PostsRepository(tempDb.Path);
        var announcements = new AnnouncementsRepository(tempDb.Path);
        var footers = new FootersRepository(tempDb.Path);
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
        using var tempDb = new SqliteTempFile();
        var posts = new PostsRepository(tempDb.Path);
        var announcements = new AnnouncementsRepository(tempDb.Path);
        var footers = new FootersRepository(tempDb.Path);

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

}
