using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class CancelCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public CancelCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_RemovesStateAndSendsConfirmation()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();
        stateStore.AddOrUpdate(1).Step = AddStep.WaitingName;

        var handler = new CancelCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.Cancel,
            announcements,
            posts,
            footers,
            helper,
            stateStore,
            isAdminChat: false);

        Assert.True(handler.CanHandle(context));

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Equal("Текущее действие отменено", sent[0]);
        Assert.False(stateStore.TryGet(1, out _));
    }
}
