using System;
using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class EditCostCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public EditCostCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InlineValue_UpdatesCostAndClearsState()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 13, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 13,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 3, 1, 15, 0, 0, DateTimeKind.Utc),
            Cost = 150
        });

        var existingState = stateStore.AddOrUpdate(1);
        existingState.Step = AddStep.EditWaitingCost;
        existingState.Existing = announcements.Get(13);

        var handler = new EditCostCommandHandler();
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditCost} 13 250",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        var updated = announcements.Get(13);
        Assert.NotNull(updated);
        Assert.Equal(250, updated!.Cost);
        Assert.Single(sentMessages);
        Assert.Equal("Стоимость обновлена", sentMessages[0]);
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

        posts.Insert(new Post { Id = 14, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 14,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 4, 1, 15, 0, 0, DateTimeKind.Utc),
            Cost = 300
        });

        var handler = new EditCostCommandHandler();
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditCost} 14 notanumber",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        Assert.Equal(2, sentMessages.Count);
        Assert.Contains("Нужно целое число", sentMessages[0]);
        Assert.Contains("Редактирование анонса", sentMessages[1]);

        Assert.True(stateStore.TryGet(1, out var state));
        Assert.NotNull(state);
        Assert.Equal(AddStep.EditWaitingCost, state!.Step);
        Assert.NotNull(state.Existing);
        Assert.Equal(14, state.Existing!.Id);
        Assert.Equal(300, announcements.Get(14)!.Cost);
    }
}
