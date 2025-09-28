using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class BotRunnerSplitFlowsTests
{
    [Fact]
    public void SplitFlows_GroupsByStep()
    {
        var flow1 = new TestFlow(new[] { AddStep.WaitingName });
        var flow2 = new TestFlow(new[] { AddStep.WaitingName, AddStep.WaitingPlace });
        var flow3 = new TestFlow(new[] { AddStep.FooterWaitingText });

        var map = BotRunner.SplitFlows(new[] { flow1, flow2, flow3 });

        Assert.True(map.TryGetValue(AddStep.WaitingName, out var waitingName));
        Assert.Collection(waitingName!, f => Assert.Same(flow1, f), f => Assert.Same(flow2, f));

        Assert.True(map.TryGetValue(AddStep.WaitingPlace, out var waitingPlace));
        Assert.Collection(waitingPlace!, f => Assert.Same(flow2, f));

        Assert.True(map.TryGetValue(AddStep.FooterWaitingText, out var footer));
        Assert.Collection(footer!, f => Assert.Same(flow3, f));
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
    public async Task HandleUpdate_Command_UsesFirstMatchingHandler()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        var botMock = new Mock<ITelegramBotClient>();
        botMock.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler1 = new TrackingHandler(canHandle: false);
        var handler2 = new TrackingHandler(canHandle: true);

        var runner = new BotRunner(
            botMock.Object,
            allowedChatId: 1,
            posts,
            announcements,
            footers,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler1, handler2 },
            Array.Empty<IConversationFlowHandler>());

        var update = CreateUpdate("/cmd", 1, 10);
        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(1, handler1.CanHandleCalls);
        Assert.Equal(1, handler2.HandleCalls);
    }

    [Fact]
    public async Task HandleUpdate_StateMessage_InvokesFlow()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();

        var botMock = new Mock<ITelegramBotClient>();
        botMock.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new TrackingHandler(false);
        var flow1 = new TrackingFlow(AddStep.WaitingName) { HandleResult = true };
        var flow2 = new TrackingFlow(AddStep.WaitingName) { HandleResult = false };

        stateStore.AddOrUpdate(10).Step = AddStep.WaitingName;

        var runner = new BotRunner(
            botMock.Object,
            1,
            posts,
            announcements,
            footers,
            helper,
            stateStore,
            new IBotCommandHandler[] { handler },
            new IConversationFlowHandler[] { flow1, flow2 });

        var update = CreateUpdate("ответ", 1, 10);
        await runner.HandleUpdate(botMock.Object, update, CancellationToken.None);

        Assert.Equal(0, handler.HandleCalls);
        Assert.Equal(1, flow1.HandleCalls);
        Assert.Equal(0, flow2.HandleCalls);
    }

    private static Update CreateUpdate(string text, long chatId, long? userId)
    {
        var payload = new
        {
            update_id = 1,
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

    private sealed class TrackingHandler : IBotCommandHandler
    {
        private readonly bool _canHandle;

        public TrackingHandler(bool canHandle)
        {
            _canHandle = canHandle;
        }

        public int CanHandleCalls { get; private set; }
        public int HandleCalls { get; private set; }

        public bool CanHandle(BotCommandContext context)
        {
            CanHandleCalls++;
            return _canHandle;
        }

        public Task HandleAsync(BotCommandContext context)
        {
            HandleCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingFlow : IConversationFlowHandler
    {
        private readonly AddStep _step;

        public TrackingFlow(AddStep step)
        {
            _step = step;
        }

        public int HandleCalls { get; private set; }
        public bool HandleResult { get; set; }

        public bool CanHandle(AddStep step) => step == _step;

        public Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
        {
            HandleCalls++;
            return Task.FromResult(HandleResult);
        }
    }
}
