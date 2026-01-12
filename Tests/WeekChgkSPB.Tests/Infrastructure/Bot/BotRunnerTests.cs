using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class BotRunnerSplitFlowsTests
{
    [Fact]
    public void SplitFlows_GroupsHandlersByStep()
    {
        var flow1 = new TestFlow(new[] { AddStep.WaitingName, AddStep.WaitingPlace });
        var flow2 = new TestFlow(new[] { AddStep.WaitingPlace, AddStep.WaitingDateTime });
        var flow3 = new TestFlow(new[] { AddStep.FooterWaitingText });

        var map = BotRunner.SplitFlows(new[] { flow1, flow2, flow3 });

        Assert.True(map.TryGetValue(AddStep.WaitingName, out var waitingName));
        Assert.Equal(new[] { flow1 }, waitingName);

        Assert.True(map.TryGetValue(AddStep.WaitingPlace, out var waitingPlace));
        Assert.Equal(new IConversationFlowHandler[] { flow1, flow2 }, waitingPlace);

        Assert.True(map.TryGetValue(AddStep.WaitingDateTime, out var waitingDate));
        Assert.Equal(new[] { flow2 }, waitingDate);

        Assert.True(map.TryGetValue(AddStep.FooterWaitingText, out var footer));
        Assert.Equal(new[] { flow3 }, footer);

        Assert.False(map.ContainsKey(AddStep.None));
        Assert.False(map.ContainsKey(AddStep.Done));
    }

    [Fact]
    public void SplitFlows_SkipsFlowsWithoutSteps()
    {
        var emptyFlow = new TestFlow(Array.Empty<AddStep>());
        var targetFlow = new TestFlow(new[] { AddStep.EditWaitingCost });

        var map = BotRunner.SplitFlows(new[] { emptyFlow, targetFlow });

        Assert.True(map.TryGetValue(AddStep.EditWaitingCost, out var list));
        Assert.Equal(new[] { targetFlow }, list);
        Assert.DoesNotContain(AddStep.WaitingCost, map.Keys);
    }

    private sealed class TestFlow : IConversationFlowHandler
    {
        private readonly HashSet<AddStep> _steps;

        public TestFlow(IEnumerable<AddStep> steps)
        {
            _steps = new HashSet<AddStep>(steps);
        }

        public bool CanHandle(AddStep step) => _steps.Contains(step);

        public Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state) => Task.FromResult(true);
    }
}

public class BotRunnerHandleUpdateTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public BotRunnerHandleUpdateTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleUpdate_CommandMessage_InvokesFirstMatchingHandler()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var botMock = new Mock<ITelegramBotClient>();
        var channelPostUpdaterMock = new Mock<IChannelPostUpdater>();
        var moderationHandler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            channelPostUpdaterMock.Object,
            adminChatId: 1);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler1 = new TestCommandHandler(canHandle: false);
        var handler2 = new TestCommandHandler(canHandle: true);

        var flows = new[] { new TrackingFlow(new[] { AddStep.WaitingName }) };

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            userManagement,
            moderationHandler,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler1, handler2 },
            flows);

        var update = CreateUpdate("/test", chatId: 1, userId: 10);

        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(1, handler1.CanHandleCallCount);
        Assert.Equal(1, handler2.CanHandleCallCount);
        Assert.Equal(1, handler2.HandleCallCount);
        Assert.Equal(0, flows[0].HandleCallCount);
    }

    [Fact]
    public async Task HandleUpdate_StateMessage_InvokesMatchingFlow()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var botMock = new Mock<ITelegramBotClient>();
        var channelPostUpdaterMock = new Mock<IChannelPostUpdater>();
        var moderationHandler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            channelPostUpdaterMock.Object,
            adminChatId: 1);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new TestCommandHandler(canHandle: false);
        var handlingFlow = new TrackingFlow(new[] { AddStep.WaitingName }) { HandleResult = true };
        var skippedFlow = new TrackingFlow(new[] { AddStep.WaitingName }) { HandleResult = true };

        stateStore.AddOrUpdate(10).Step = AddStep.WaitingName;

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            userManagement,
            moderationHandler,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler },
            new IConversationFlowHandler[] { handlingFlow, skippedFlow });

        var update = CreateUpdate("ответ", chatId: 1, userId: 10);

        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(0, handler.HandleCallCount);
        Assert.Equal(1, handlingFlow.HandleCallCount);
        Assert.Equal(0, skippedFlow.HandleCallCount);
    }

    [Fact]
    public async Task HandleUpdate_IgnoresMessagesFromOtherChats()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var botMock = new Mock<ITelegramBotClient>();
        var channelPostUpdaterMock = new Mock<IChannelPostUpdater>();
        var moderationHandler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            channelPostUpdaterMock.Object,
            adminChatId: 1);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new TestCommandHandler(canHandle: true);
        var flow = new TrackingFlow(new[] { AddStep.WaitingName });

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            userManagement,
            moderationHandler,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler },
            new IConversationFlowHandler[] { flow });

        var update = CreateUpdate("/test", chatId: 999, userId: 10);

        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(0, handler.CanHandleCallCount);
        Assert.Equal(0, handler.HandleCallCount);
        Assert.Equal(0, flow.HandleCallCount);
    }

    [Fact]
    public async Task HandleUpdate_IgnoresMessageWithoutText()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var botMock = new Mock<ITelegramBotClient>();
        var channelPostUpdaterMock = new Mock<IChannelPostUpdater>();
        var moderationHandler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            channelPostUpdaterMock.Object,
            adminChatId: 1);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new TestCommandHandler(canHandle: true);
        var flow = new TrackingFlow(new[] { AddStep.WaitingName });

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            userManagement,
            moderationHandler,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler },
            new IConversationFlowHandler[] { flow });

        var update = CreateUpdate(null, chatId: 1, userId: 10);

        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(0, handler.CanHandleCallCount);
        Assert.Equal(0, handler.HandleCallCount);
        Assert.Equal(0, flow.HandleCallCount);
    }

    [Fact]
    public async Task HandleUpdate_FlowHandlers_ContinuesUntilHandled()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var botMock = new Mock<ITelegramBotClient>();
        var channelPostUpdaterMock = new Mock<IChannelPostUpdater>();
        var moderationHandler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            channelPostUpdaterMock.Object,
            adminChatId: 1);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new TestCommandHandler(canHandle: false);
        var firstFlow = new TrackingFlow(new[] { AddStep.WaitingName }) { HandleResult = false };
        var secondFlow = new TrackingFlow(new[] { AddStep.WaitingName }) { HandleResult = true };

        stateStore.AddOrUpdate(10).Step = AddStep.WaitingName;

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            userManagement,
            moderationHandler,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler },
            new IConversationFlowHandler[] { firstFlow, secondFlow });

        var update = CreateUpdate("ответ", chatId: 1, userId: 10);

        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(1, firstFlow.HandleCallCount);
        Assert.Equal(1, secondFlow.HandleCallCount);
    }

    private static Update CreateUpdate(string text, long chatId, long? userId)
    {
        var payload = new
        {
            update_id = 123,
            message = new
            {
                message_id = 1,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                text,
                chat = new { id = chatId, type = "private" },
                from = userId.HasValue ? new { id = userId.Value, is_bot = false, first_name = "user" } : null
            }
        };

        return JsonSerializer.Deserialize<Update>(JsonSerializer.Serialize(payload), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    private sealed class TestCommandHandler : IBotCommandHandler
    {
        private readonly bool _canHandle;

        public TestCommandHandler(bool canHandle)
        {
            _canHandle = canHandle;
        }

        public int CanHandleCallCount { get; private set; }
        public int HandleCallCount { get; private set; }

        public bool CanHandle(BotCommandContext context)
        {
            CanHandleCallCount++;
            return _canHandle;
        }

        public Task HandleAsync(BotCommandContext context)
        {
            HandleCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingFlow : IConversationFlowHandler
    {
        private readonly HashSet<AddStep> _steps;

        public TrackingFlow(IEnumerable<AddStep> steps)
        {
            _steps = new HashSet<AddStep>(steps);
        }

        public int HandleCallCount { get; private set; }
        public bool HandleResult { get; set; } = true;

        public bool CanHandle(AddStep step) => _steps.Contains(step);

        public Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
        {
            HandleCallCount++;
            return Task.FromResult(HandleResult);
        }
    }
}
