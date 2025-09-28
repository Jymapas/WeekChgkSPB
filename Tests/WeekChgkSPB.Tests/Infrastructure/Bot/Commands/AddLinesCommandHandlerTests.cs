using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class AddLinesCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public AddLinesCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_SetsWaitingIdAndSendsPrompt()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new AddLinesCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.AddLines,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        Assert.True(handler.CanHandle(context));

        await handler.HandleAsync(context);

        Assert.True(stateStore.TryGet(1, out var state));
        Assert.Equal(AddStep.WaitingId, state!.Step);
        Assert.Equal(0, state.Draft.Id);
        Assert.Single(sent);
        Assert.Contains("id поста", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_NoUser_DoesNothing()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new AddLinesCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.AddLines,
            announcements,
            posts,
            footers,
            helper,
            stateStore,
            userId: null);

        await handler.HandleAsync(context);

        Assert.False(stateStore.TryGet(1, out _));
        Assert.Empty(sent);
    }
}
