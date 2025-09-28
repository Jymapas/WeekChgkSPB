using System;
using System.Text.Json;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

internal static class FlowTestContextFactory
{
    public static BotCommandContext CreateContext(
        ITelegramBotClient bot,
        string text,
        long chatId,
        long userId,
        AnnouncementsRepository announcements,
        PostsRepository posts,
        FootersRepository footers,
        BotConversationState stateStore,
        BotCommandHelper helper)
    {
        var messagePayload = new
        {
            message_id = 1,
            text,
            chat = new { id = chatId, type = "private" },
            from = new { id = userId, is_bot = false, first_name = "user" }
        };

        var message = JsonSerializer.Deserialize<Message>(
            JsonSerializer.Serialize(messagePayload),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to create message for test context");

        return new BotCommandContext(
            bot,
            message,
            CancellationToken.None,
            announcements,
            posts,
            footers,
            stateStore,
            helper);
    }
}
