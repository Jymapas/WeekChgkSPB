using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public class EditPlaceCommandHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public EditPlaceCommandHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_InlineValue_UpdatesPlaceAndClearsState()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var posts = _fixture.CreatePostsRepository();
        var footers = _fixture.CreateFootersRepository();
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var stateStore = new BotConversationState();

        posts.Insert(new Post { Id = 7, Title = "Title", Link = "https://example.com/post", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 7,
            TournamentName = "Tournament",
            Place = "Old Place",
            DateTimeUtc = new DateTime(2025, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });

        var existingState = stateStore.AddOrUpdate(1);
        existingState.Step = AddStep.EditWaitingPlace;
        existingState.Existing = announcements.Get(7);

        var updater = new Mock<IChannelPostUpdater>();
        updater
            .Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new EditPlaceCommandHandler(updater.Object);
        var (context, sentMessages, _) = CommandTestContextFactory.Create(
            $"{BotCommands.EditPlace} https://example.com/post Новый клуб",
            announcements,
            posts,
            footers,
            helper,
            stateStore);

        await handler.HandleAsync(context);

        var updated = announcements.Get(7);
        Assert.NotNull(updated);
        Assert.Equal("Новый клуб", updated!.Place);
        Assert.Single(sentMessages);
        Assert.Equal("Место обновлено", sentMessages[0]);
        Assert.False(stateStore.TryGet(1, out _));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
