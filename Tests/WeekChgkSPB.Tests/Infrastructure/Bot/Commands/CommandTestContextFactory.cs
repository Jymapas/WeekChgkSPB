using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

internal static class CommandTestContextFactory
{
    public static (BotCommandContext Context, List<string> SentMessages, Mock<ITelegramBotClient> BotMock) Create(
        string text,
        AnnouncementsRepository announcements,
        PostsRepository posts,
        FootersRepository footers,
        BotCommandHelper helper,
        BotConversationState stateStore,
        long chatId = 1,
        long? userId = 1)
    {
        var messages = new List<string>();
        var botMock = new Mock<ITelegramBotClient>();
        botMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<Message> request, CancellationToken _) =>
            {
                var textProp = request.GetType().GetProperty("Text");
                var sentText = textProp?.GetValue(request) as string ?? string.Empty;
                messages.Add(sentText);
                return new Message { Text = sentText };
            });

        var payload = new
        {
            message_id = 1,
            text,
            chat = new { id = chatId, type = "private" },
            from = userId.HasValue ? new { id = userId.Value, is_bot = false, first_name = "user" } : null
        };

        var message = JsonSerializer.Deserialize<Message>(
            JsonSerializer.Serialize(payload),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var context = new BotCommandContext(
            botMock.Object,
            message,
            CancellationToken.None,
            announcements,
            posts,
            footers,
            stateStore,
            helper);

        return (context, messages, botMock);
    }
}
