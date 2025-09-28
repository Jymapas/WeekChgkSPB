using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class AddCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public AddCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_SetsWaitingLinesAndSendsPrompt()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new AddCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.Add,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.True(stateStore.TryGet(1, out var state));
        Assert.Equal(AddStep.WaitingLines, state!.Step);
        Assert.Single(sent);
        Assert.Contains("5 или 6 строк", sent[0]);
    }
}
