using System;
using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class EditDateTimeCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public EditDateTimeCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InlineValue_UpdatesDateTimeAndClearsState()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 9, Title = "Title", Link = "link", Description = "desc" });
        var originalUtc = new DateTime(2025, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        announcements.Insert(new Announcement
        {
            Id = 9,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = originalUtc,
            Cost = 100
        });

        var existingState = stateStore.AddOrUpdate(1);
        existingState.Step = AddStep.EditWaitingDateTime;
        existingState.Existing = announcements.Get(9);

        var handler = new EditDateTimeCommandHandler();
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditDateTime} 9 2025-08-10T19:30:00Z",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        var updated = announcements.Get(9);
        Assert.NotNull(updated);
        Assert.Equal(DateTimeKind.Utc, updated!.DateTimeUtc.Kind);
        Assert.Equal(new DateTime(2025, 8, 10, 19, 30, 0, DateTimeKind.Utc), updated.DateTimeUtc);

        Assert.Single(sentMessages);
        Assert.Equal("Дата и время обновлены", sentMessages[0]);
        Assert.False(stateStore.TryGet(1, out _));
    }

    [Fact]
    public async Task HandleAsync_InvalidInlineValue_SetsStateAndPrompts()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 11, Title = "Title", Link = "link", Description = "desc" });
        var originalUtc = new DateTime(2025, 2, 1, 15, 0, 0, DateTimeKind.Utc);
        announcements.Insert(new Announcement
        {
            Id = 11,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = originalUtc,
            Cost = 200
        });

        var handler = new EditDateTimeCommandHandler();
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditDateTime} 11 не-дата",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Equal(2, sentMessages.Count);
        Assert.Contains("Неверный формат", sentMessages[0]);
        Assert.Contains("Редактирование анонса", sentMessages[1]);

        Assert.True(stateStore.TryGet(1, out var state));
        Assert.NotNull(state);
        Assert.Equal(AddStep.EditWaitingDateTime, state!.Step);
        Assert.NotNull(state.Existing);
        Assert.Equal(11, state.Existing!.Id);
        Assert.Equal(originalUtc, announcements.Get(11)!.DateTimeUtc);
    }
}
