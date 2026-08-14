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

public class ForcePostHandlerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public ForcePostHandlerTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleCallbackQuery_Confirm_PublishesToChannelAndMarksPosted()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        posts.Insert(new Post { Id = 1, Title = "Title", Link = "link", Description = "desc" });
        announcements.Insert(new Announcement
        {
            Id = 1,
            TournamentName = "Tournament",
            Place = "Place",
            DateTimeUtc = DateTime.UtcNow.AddHours(1),
            Cost = 100
        });

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);

        var handler = new ForcePostHandler(announcements, footers, history, botMock.Object, "@channel");

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery("forcepost_confirm", "Предпросмотр"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Contains(sentMessages, text => text.Contains("Tournament"));
        Assert.Contains(sentMessages, text => text.Contains("Опубликовано в канал"));

        var latest = history.GetLatest();
        Assert.NotNull(latest);
    }

    [Fact]
    public async Task HandleCallbackQuery_Cancel_EditsMessageWithoutPublishing()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);

        var handler = new ForcePostHandler(announcements, footers, history, botMock.Object, "@channel");

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery("forcepost_cancel", "Предпросмотр"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Contains(sentMessages, text => text.Contains("Отменено"));
        Assert.Null(history.GetLatest());
    }

    [Fact]
    public async Task HandleCallbackQuery_UnknownData_ReturnsFalse()
    {
        _fixture.Reset();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var footers = _fixture.CreateFootersRepository();
        var history = _fixture.CreateChannelPostsRepository();

        var sentMessages = new List<string>();
        var callbackAnswers = new List<string>();
        var botMock = CreateBotMock(sentMessages, callbackAnswers);

        var handler = new ForcePostHandler(announcements, footers, history, botMock.Object, "@channel");

        var handled = await handler.HandleCallbackQuery(
            CreateCallbackQuery("mod_approve_1", "Предпросмотр"),
            CancellationToken.None);

        Assert.False(handled);
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

    private static CallbackQuery CreateCallbackQuery(string data, string messageText)
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
                message_id = 99,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                text = messageText,
                chat = new { id = 1, type = "private" }
            }
        };

        return JsonSerializer.Deserialize<CallbackQuery>(
            JsonSerializer.Serialize(payload),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
