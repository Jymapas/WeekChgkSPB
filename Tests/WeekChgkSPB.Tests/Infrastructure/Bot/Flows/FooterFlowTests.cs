using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

public class FooterFlowTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public FooterFlowTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InsertsFooterAndCompletes()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 321;
        const long chatId = 654;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.FooterWaitingText;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "<b>footer</b>",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new FooterFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.Done, state.Step);
        Assert.False(stateStore.TryGet(userId, out _));
        var items = footers.ListAllDesc();
        Assert.Single(items);
        Assert.Equal("<b>footer</b>", items[0].Text);
    }

    [Fact]
    public async Task HandleAsync_EmptyText_KeepsWaiting()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        const long userId = 700;
        const long chatId = 701;
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.FooterWaitingText;

        var botClient = TelegramBotClientStub.Create();
        var context = FlowTestContextFactory.CreateContext(
            botClient,
            "   ",
            chatId,
            userId,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        var flow = new FooterFlow();

        var handled = await flow.HandleAsync(context, state);

        Assert.True(handled);
        Assert.Equal(AddStep.FooterWaitingText, state.Step);
        Assert.True(stateStore.TryGet(userId, out var storedState));
        Assert.Same(state, storedState);
        Assert.Empty(footers.ListAllDesc());
    }
}
