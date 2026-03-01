using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class HelpCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public HelpCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_SendsUserHelp()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new HelpCommandHandler();
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.Help,
            announcements,
            posts,
            footers,
            helper,
            stateStore,
            isAdminChat: false);

        Assert.True(handler.CanHandle(context));

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("/help", sent[0]);
        Assert.Contains("/add", sent[0]);
        Assert.Contains("/delete", sent[0]);
        Assert.DoesNotContain("/makepost", sent[0]);
        Assert.DoesNotContain("/footer", sent[0]);
    }
}
