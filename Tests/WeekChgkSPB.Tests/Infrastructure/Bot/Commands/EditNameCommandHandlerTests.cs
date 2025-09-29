using System;
using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class EditNameCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public EditNameCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InlineValue_UpdatesNameAndClearsState()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 5, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 5,
            TournamentName = "Old Name",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });

        var existingState = stateStore.AddOrUpdate(1);
        existingState.Step = AddStep.EditWaitingName;
        existingState.Existing = announcements.Get(5);

        var handler = new EditNameCommandHandler();
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditName} 5 Новое имя",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        var updated = announcements.Get(5);
        Assert.NotNull(updated);
        Assert.Equal("Новое имя", updated!.TournamentName);

        Assert.Single(sentMessages);
        Assert.Equal("Название обновлено", sentMessages[0]);
        Assert.False(stateStore.TryGet(1, out _));
    }
}
