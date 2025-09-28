using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
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
    public async Task HandleAsync_BuildsScheduleAndSendsMessage()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var footersRepo = _fixture.CreateFootersRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
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
        footersRepo.Insert("footer");

        var handler = new MakePostCommandHandler(BotCommands.MakePost, asLiveJournal: false);
        var (context, sent, _) = CommandTestContextFactory.Create(
            $"{BotCommands.MakePost} 2025-01-01 2025-01-14",
            announcements,
            posts,
            footersRepo,
            helper,
            stateStore);

        var canHandle = handler.CanHandle(context);
        Assert.True(canHandle);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.False(string.IsNullOrWhiteSpace(sent[0]));
        Assert.DoesNotContain("анонсов нет", sent[0]);
    }

    [Fact]
    public async Task HandleAsync_NoAnnouncements_SendsWarning()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var footersRepo = _fixture.CreateFootersRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        var handler = new MakePostCommandHandler(BotCommands.MakePost, asLiveJournal: false);
        var (context, sent, _) = CommandTestContextFactory.Create(
            BotCommands.MakePost,
            announcements,
            posts,
            footersRepo,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Single(sent);
        Assert.Contains("анонсов нет", sent[0]);
    }

}
