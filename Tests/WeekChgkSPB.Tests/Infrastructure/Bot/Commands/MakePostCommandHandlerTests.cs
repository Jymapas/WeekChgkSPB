using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class MakePostCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public MakePostCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_BuildsSchedule()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 1, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 1,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 5, 15, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });
        footers.Insert("footer");

        var handler = new MakePostCommandHandler(BotCommands.MakePost, false);
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.MakePost} 2025-01-01 2025-01-10",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("Tournament", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenEmptyRange_SendsNotice()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new MakePostCommandHandler(BotCommands.MakePost, false);
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.MakePost,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("анонсов нет", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_AsLiveJournal_WrapsHtmlAndDisablesPreview()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 2, Title = "Title", Link = "https://example.com", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 2,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 6, 16, 0, 0, DateTimeKind.Utc),
            Cost = 200
        });
        footers.Insert("<i>footer</i>");

        var commandText = $"{BotCommands.MakePostLJ} 2025-01-01 2025-01-10";
        var handler = new MakePostCommandHandler(BotCommands.MakePostLJ, true);
        var (context, sent, botMock) = CommandTestContextFactory.Create(
            commandText,
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);

        var (fromUtc, toUtc) = helper.ResolveDateRangeOrDefault(commandText);
        var rows = announcements.GetWithLinksInRange(fromUtc, toUtc);
        var expected = PostFormatter.WrapAsCodeForTelegram(PostFormatter.BuildScheduleHtml(rows, footers.GetAllTextsDesc()));
        Assert.Equal(expected, sent[0]);

        var invocation = Assert.Single(botMock.Invocations);
        var request = Assert.IsType<SendMessageRequest>(invocation.Arguments[0]);
        Assert.Equal(ParseMode.Html, request.ParseMode);
        Assert.NotNull(request.LinkPreviewOptions);
        Assert.True(request.LinkPreviewOptions!.IsDisabled);
    }
}
