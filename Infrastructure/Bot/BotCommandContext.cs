using Telegram.Bot;
using Telegram.Bot.Types;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot;

internal sealed record BotCommandContext(
    ITelegramBotClient Bot,
    Message Message,
    CancellationToken CancellationToken,
    AnnouncementsRepository Announcements,
    PostsRepository Posts,
    FootersRepository Footers,
    BotConversationState StateStore,
    BotCommandHelper Helper,
    bool IsAdminChat = false,
    UserManagementRepository? UserManagement = null,
    ModerationHandler? Moderation = null);
