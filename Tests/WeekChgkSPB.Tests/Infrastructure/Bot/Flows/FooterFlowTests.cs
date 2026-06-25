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

public class FooterFlowTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public FooterFlowTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    private static (BotCommandContext ctx, AddAnnouncementState state, BotConversationState store, FootersRepository footers)
        SetupFlow(SqliteFixture fixture, string messageText, long userId = 321, long chatId = 654)
    {
        fixture.Reset();
        var footers = fixture.CreateFootersRepository();
        var announcements = fixture.CreateAnnouncementsRepository();
        var posts = fixture.CreatePostsRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var state = stateStore.AddOrUpdate(userId);
        state.Step = AddStep.FooterWaitingText;

        var botClient = TelegramBotClientStub.Create();
        var ctx = FlowTestContextFactory.CreateContext(
            botClient, messageText, chatId, userId,
            announcements, posts, footers, stateStore, helper);

        return (ctx, state, stateStore, footers);
    }

    [Fact]
    public async Task HandleAsync_TextStep_SetsExpiryStepAndDoesNotInsert()
    {
        var (ctx, state, _, footers) = SetupFlow(_fixture, "<b>footer</b>");
        var flow = new FooterFlow();

        var handled = await flow.HandleAsync(ctx, state);

        Assert.True(handled);
        Assert.Equal(AddStep.FooterWaitingExpiry, state.Step);
        Assert.Equal("<b>footer</b>", state.FooterDraftText);
        Assert.Empty(footers.ListAllDesc());
    }

    [Fact]
    public async Task HandleAsync_EmptyText_KeepsWaiting()
    {
        var (ctx, state, stateStore, footers) = SetupFlow(_fixture, "   ", userId: 700, chatId: 701);
        var flow = new FooterFlow();

        var handled = await flow.HandleAsync(ctx, state);

        Assert.True(handled);
        Assert.Equal(AddStep.FooterWaitingText, state.Step);
        Assert.True(stateStore.TryGet(700, out var storedState));
        Assert.Same(state, storedState);
        Assert.Empty(footers.ListAllDesc());
    }

    [Fact]
    public async Task HandleAsync_WithSkip_InsertsWithoutExpiry()
    {
        var (ctx, state, stateStore, footers) = SetupFlow(_fixture, "<b>footer</b>");
        state.FooterDraftText = "<b>footer</b>";
        state.Step = AddStep.FooterWaitingExpiry;

        // Replace context message with /skip
        var footers2 = footers;
        var announcements2 = _fixture.CreateAnnouncementsRepository();
        var posts2 = _fixture.CreatePostsRepository();
        var helper2 = new BotCommandHelper(PostFormatter.Moscow);
        var skipCtx = FlowTestContextFactory.CreateContext(
            TelegramBotClientStub.Create(), "/skip", 654, 321,
            announcements2, posts2, footers2, stateStore, helper2);

        var flow = new FooterFlow();
        var handled = await flow.HandleAsync(skipCtx, state);

        Assert.True(handled);
        Assert.Equal(AddStep.Done, state.Step);
        Assert.False(stateStore.TryGet(321, out _));
        var items = footers.ListAllDesc();
        Assert.Single(items);
        Assert.Equal("<b>footer</b>", items[0].Text);
        Assert.Null(items[0].ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_WithValidDate_InsertsWithExpiry()
    {
        _fixture.Reset();
        var footers = _fixture.CreateFootersRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var state = stateStore.AddOrUpdate(321);
        state.Step = AddStep.FooterWaitingExpiry;
        state.FooterDraftText = "<i>timed</i>";

        var ctx = FlowTestContextFactory.CreateContext(
            TelegramBotClientStub.Create(), "31.12.2030", 654, 321,
            announcements, posts, footers, stateStore, helper);

        var flow = new FooterFlow();
        var handled = await flow.HandleAsync(ctx, state);

        Assert.True(handled);
        Assert.Equal(AddStep.Done, state.Step);
        var items = footers.ListAllDesc();
        Assert.Single(items);
        Assert.Equal("<i>timed</i>", items[0].Text);
        Assert.NotNull(items[0].ExpiresAt);
        Assert.Equal(2030, items[0].ExpiresAt!.Value.ToUniversalTime().Year);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidDate_KeepsExpiryStep()
    {
        _fixture.Reset();
        var footers = _fixture.CreateFootersRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var state = stateStore.AddOrUpdate(321);
        state.Step = AddStep.FooterWaitingExpiry;
        state.FooterDraftText = "<i>timed</i>";

        var ctx = FlowTestContextFactory.CreateContext(
            TelegramBotClientStub.Create(), "not-a-date", 654, 321,
            announcements, posts, footers, stateStore, helper);

        var flow = new FooterFlow();
        var handled = await flow.HandleAsync(ctx, state);

        Assert.True(handled);
        Assert.Equal(AddStep.FooterWaitingExpiry, state.Step);
        Assert.Empty(footers.ListAllDesc());
    }

    [Fact]
    public async Task HandleAsync_FullFlow_TwoStepsWithDate()
    {
        _fixture.Reset();
        var footers = _fixture.CreateFootersRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        var state = stateStore.AddOrUpdate(321);
        state.Step = AddStep.FooterWaitingText;

        var flow = new FooterFlow();

        // Step 1: send HTML
        var ctx1 = FlowTestContextFactory.CreateContext(
            TelegramBotClientStub.Create(), "<b>footer</b>", 654, 321,
            announcements, posts, footers, stateStore, helper);
        await flow.HandleAsync(ctx1, state);

        Assert.Equal(AddStep.FooterWaitingExpiry, state.Step);
        Assert.Empty(footers.ListAllDesc());

        // Step 2: send date
        var ctx2 = FlowTestContextFactory.CreateContext(
            TelegramBotClientStub.Create(), "15.06.2030", 654, 321,
            announcements, posts, footers, stateStore, helper);
        await flow.HandleAsync(ctx2, state);

        Assert.Equal(AddStep.Done, state.Step);
        var items = footers.ListAllDesc();
        Assert.Single(items);
        Assert.Equal("<b>footer</b>", items[0].Text);
        Assert.NotNull(items[0].ExpiresAt);
    }
}
