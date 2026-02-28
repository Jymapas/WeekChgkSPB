using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class ModerationHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public ModerationHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleCallbackQuery_Approve_InsertsAnnouncementAndDeletesPending()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        posts.Insert(new Post
        {
            Id = 42,
            Title = "Title",
            Link = "https://example.com/post-42",
            Description = "desc"
        });

        var pending = new PendingAnnouncement
        {
            TournamentName = "Турнир",
            Place = "Клуб",
            DateTimeUtc = new DateTime(2025, 8, 10, 16, 30, 0, DateTimeKind.Utc),
            Cost = 300,
            UserId = 555,
            Link = "https://example.com/post-42",
            CreatedAt = new DateTime(2025, 8, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        pending.Id = userManagement.AddPending(pending);

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);
        var updater = new Mock<IChannelPostUpdater>();
        updater
            .Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            updater.Object,
            adminChatId: 1);

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery($"mod_approve_{pending.Id}", chatId: 1, messageId: 99),
            CancellationToken.None);

        Assert.True(handled);

        var inserted = announcements.Get(42);
        Assert.NotNull(inserted);
        Assert.Equal("Турнир", inserted!.TournamentName);
        Assert.Equal("Клуб", inserted.Place);
        Assert.Equal(pending.DateTimeUtc, inserted.DateTimeUtc);
        Assert.Equal(300, inserted.Cost);
        Assert.Equal(555, inserted.UserId);

        Assert.Null(userManagement.GetPending(pending.Id));
        Assert.Contains(sentMessages, text => text.Contains("✅ Пост одобрен"));
        Assert.Contains(sentMessages, text => text.Contains("Ваш анонс \"Турнир\" был одобрен и добавлен"));
        Assert.Contains(callbackAnswers, text => text == "Пост одобрен");
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackQuery_Allow_InsertsAnnouncementAndMarksUserAllowed()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        var pending = new PendingAnnouncement
        {
            TournamentName = "Ночной турнир",
            Place = "Лофт",
            DateTimeUtc = new DateTime(2025, 9, 1, 17, 0, 0, DateTimeKind.Utc),
            Cost = 450,
            UserId = 777,
            Link = "https://example.com/external-777",
            CreatedAt = new DateTime(2025, 8, 20, 11, 0, 0, DateTimeKind.Utc)
        };
        pending.Id = userManagement.AddPending(pending);

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);
        var updater = new Mock<IChannelPostUpdater>();
        updater
            .Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            updater.Object,
            adminChatId: 1);

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery($"mod_allow_{pending.Id}", chatId: 1, messageId: 100),
            CancellationToken.None);

        Assert.True(handled);

        var inserted = announcements.GetByLink("https://example.com/external-777");
        Assert.NotNull(inserted);
        Assert.Equal("Ночной турнир", inserted!.TournamentName);
        Assert.Equal("Лофт", inserted.Place);
        Assert.Equal(pending.DateTimeUtc, inserted.DateTimeUtc);
        Assert.Equal(450, inserted.Cost);
        Assert.Equal(777, inserted.UserId);

        Assert.True(userManagement.IsAllowed(777));
        Assert.Null(userManagement.GetPending(pending.Id));
        Assert.Contains(sentMessages, text => text.Contains("✅ Пользователь получил разрешение"));
        Assert.Contains(sentMessages, text => text.Contains("Теперь вы можете добавлять анонсы без модерации."));
        Assert.Contains(callbackAnswers, text => text == "Пользователь получил разрешение");
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("approve", "Ошибка: отсутствует ссылка", false)]
    [InlineData("allow", "Ошибка: отсутствует ссылка", true)]
    public async Task HandleCallbackQuery_KnownPostPendingWithoutLink_CannotBeModerated(
        string action,
        string expectedAnswer,
        bool userBecomesAllowed)
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var userManagement = _fixture.CreateUserManagementRepository();

        posts.Insert(new Post
        {
            Id = 99,
            Title = "Known Post",
            Link = "https://chgk-spb.livejournal.com/99.html",
            Description = "desc"
        });

        var pending = new PendingAnnouncement
        {
            TournamentName = "Пошаговый анонс",
            Place = "Бар",
            DateTimeUtc = new DateTime(2025, 10, 1, 15, 0, 0, DateTimeKind.Utc),
            Cost = 500,
            UserId = 888,
            Link = null,
            CreatedAt = new DateTime(2025, 9, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        pending.Id = userManagement.AddPending(pending);

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);
        var updater = new Mock<IChannelPostUpdater>();
        updater
            .Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ModerationHandler(
            botMock.Object,
            announcements,
            userManagement,
            posts,
            updater.Object,
            adminChatId: 1);

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery($"mod_{action}_{pending.Id}", chatId: 1, messageId: 101),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Null(announcements.Get(99));
        Assert.NotNull(userManagement.GetPending(pending.Id));
        Assert.Contains(callbackAnswers, text => text == expectedAnswer);
        Assert.DoesNotContain(sentMessages, text => text.Contains("Ваш анонс"));
        Assert.Equal(userBecomesAllowed, userManagement.IsAllowed(888));
        updater.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<ITelegramBotClient> CreateBotMock(List<string> sentMessages, List<string> callbackAnswers)
    {
        var botMock = new Mock<ITelegramBotClient>();

        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<Message> request, CancellationToken _) =>
            {
                var text = request.GetType().GetProperty("Text")?.GetValue(request) as string ?? string.Empty;
                sentMessages.Add(text);
                return new Message { Text = text };
            });

        botMock
            .Setup(b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<bool> request, CancellationToken _) =>
            {
                var text = request.GetType().GetProperty("Text")?.GetValue(request) as string ?? string.Empty;
                callbackAnswers.Add(text);
                return true;
            });

        return botMock;
    }

    private static CallbackQuery CreateCallbackQuery(string data, long chatId, int messageId)
    {
        var payload = new
        {
            id = "callback-1",
            from = new
            {
                id = 1000,
                is_bot = false,
                first_name = "Admin",
                username = "admin"
            },
            data,
            message = new
            {
                message_id = messageId,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                chat = new { id = chatId, type = "private" }
            }
        };

        return JsonSerializer.Deserialize<CallbackQuery>(
            JsonSerializer.Serialize(payload),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
