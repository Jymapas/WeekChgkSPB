using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class ModerationHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly AnnouncementsRepository _announcements;
    private readonly UserManagementRepository _userManagement;
    private readonly PostsRepository _posts;
    private readonly IChannelPostUpdater _channelPostUpdater;
    private readonly long _adminChatId;

    public ModerationHandler(
        ITelegramBotClient bot,
        AnnouncementsRepository announcements,
        UserManagementRepository userManagement,
        PostsRepository posts,
        IChannelPostUpdater channelPostUpdater,
        long adminChatId)
    {
        _bot = bot;
        _announcements = announcements;
        _userManagement = userManagement;
        _posts = posts;
        _channelPostUpdater = channelPostUpdater;
        _adminChatId = adminChatId;
    }

    public async Task<bool> HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data is null || !callbackQuery.Data.StartsWith("mod_"))
        {
            return false;
        }

        var parts = callbackQuery.Data.Split('_');
        if (parts.Length < 3)
        {
            return false;
        }

        if (!long.TryParse(parts[2], out var pendingId))
        {
            return false;
        }

        var pending = _userManagement.GetPending(pendingId);
        if (pending is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.RequestNotFound, cancellationToken: ct);
            if (callbackQuery.Message is not null)
            {
                await _bot.EditMessageText(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    Messages.Moderation.AlreadyProcessed,
                    cancellationToken: ct);
            }
            return true;
        }

        var action = parts[1];
        switch (action)
        {
            case "approve":
                await HandleApprove(pending, callbackQuery, ct);
                break;
            case "allow":
                await HandleAllow(pending, callbackQuery, ct);
                break;
            case "reject":
                await HandleReject(pending, callbackQuery, ct);
                break;
            case "ban":
                await HandleBan(pending, callbackQuery, ct);
                break;
            default:
                return false;
        }

        return true;
    }

    private async Task HandleApprove(PendingAnnouncement pending, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var announcement = new Announcement
        {
            TournamentName = pending.TournamentName,
            Place = pending.Place,
            DateTimeUtc = pending.DateTimeUtc,
            Cost = pending.Cost,
            UserId = pending.UserId
        };

        if (pending.Link is not null && _posts.TryGetIdByLink(pending.Link, out var id))
        {
            announcement.Id = id;
            _announcements.Insert(announcement);
        }
        else if (pending.Link is not null)
        {
            _announcements.InsertExternal(announcement, pending.Link);
        }
        else
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.LinkMissing, cancellationToken: ct);
            return;
        }

        _userManagement.DeletePending(pending.Id);
        await _channelPostUpdater.UpdateLastPostAsync(ct);

        var userInfo = callbackQuery.From.Username is not null
            ? $"@{callbackQuery.From.Username}"
            : $"{callbackQuery.From.FirstName} {callbackQuery.From.LastName}".Trim();

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"{Messages.Moderation.AdminApproved}\n\n{FormatPendingAnnouncement(pending, userInfo)}",
                cancellationToken: ct);
        }

        await _bot.SendMessage(
            pending.UserId,
            Messages.Moderation.UserApproved(pending.TournamentName),
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.Approved, cancellationToken: ct);
    }

    private async Task HandleAllow(PendingAnnouncement pending, CallbackQuery callbackQuery, CancellationToken ct)
    {
        _userManagement.AllowUser(pending.UserId);

        var announcement = new Announcement
        {
            TournamentName = pending.TournamentName,
            Place = pending.Place,
            DateTimeUtc = pending.DateTimeUtc,
            Cost = pending.Cost,
            UserId = pending.UserId
        };

        if (pending.Link is not null && _posts.TryGetIdByLink(pending.Link, out var id))
        {
            announcement.Id = id;
            _announcements.Insert(announcement);
        }
        else if (pending.Link is not null)
        {
            _announcements.InsertExternal(announcement, pending.Link);
        }
        else
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.LinkMissing, cancellationToken: ct);
            return;
        }

        _userManagement.DeletePending(pending.Id);
        await _channelPostUpdater.UpdateLastPostAsync(ct);

        var userInfo = callbackQuery.From.Username is not null
            ? $"@{callbackQuery.From.Username}"
            : $"{callbackQuery.From.FirstName} {callbackQuery.From.LastName}".Trim();

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"{Messages.Moderation.AdminAllowed}\n\n{FormatPendingAnnouncement(pending, userInfo)}",
                cancellationToken: ct);
        }

        await _bot.SendMessage(
            pending.UserId,
            Messages.Moderation.UserAllowed(pending.TournamentName),
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.Allowed, cancellationToken: ct);
    }

    private async Task HandleReject(PendingAnnouncement pending, CallbackQuery callbackQuery, CancellationToken ct)
    {
        _userManagement.DeletePending(pending.Id);

        var userInfo = callbackQuery.From.Username is not null
            ? $"@{callbackQuery.From.Username}"
            : $"{callbackQuery.From.FirstName} {callbackQuery.From.LastName}".Trim();

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"{Messages.Moderation.AdminRejected}\n\n{FormatPendingAnnouncement(pending, userInfo)}",
                cancellationToken: ct);
        }

        await _bot.SendMessage(
            pending.UserId,
            Messages.Moderation.UserRejected(pending.TournamentName),
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.Rejected, cancellationToken: ct);
    }

    private async Task HandleBan(PendingAnnouncement pending, CallbackQuery callbackQuery, CancellationToken ct)
    {
        _userManagement.BanUser(pending.UserId);
        _userManagement.DeletePending(pending.Id);

        var userInfo = callbackQuery.From.Username is not null
            ? $"@{callbackQuery.From.Username}"
            : $"{callbackQuery.From.FirstName} {callbackQuery.From.LastName}".Trim();

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"{Messages.Moderation.AdminBanned}\n\n{FormatPendingAnnouncement(pending, userInfo)}",
                cancellationToken: ct);
        }

        await _bot.SendMessage(
            pending.UserId,
            Messages.Moderation.UserBannedNotification,
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.Banned, cancellationToken: ct);
    }

    public async Task SendModerationRequest(PendingAnnouncement pending, long userId, string userName, CancellationToken ct)
    {
        var text = $"{Messages.Moderation.NewRequest}\n\n{FormatPendingAnnouncement(pending, userName)}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonApprove, $"mod_approve_{pending.Id}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonAllow, $"mod_allow_{pending.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonReject, $"mod_reject_{pending.Id}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonBan, $"mod_ban_{pending.Id}")
            }
        });

        await _bot.SendMessage(_adminChatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private static string FormatPendingAnnouncement(PendingAnnouncement pending, string userInfo)
    {
        return $"Пользователь: {userInfo}\n" +
               $"Название: {pending.TournamentName}\n" +
               $"Место: {pending.Place}\n" +
               $"Дата и время: {pending.DateTimeUtc:yyyy-MM-dd HH:mm} UTC\n" +
               $"Стоимость: {pending.Cost}\n" +
               $"Ссылка: {pending.Link ?? "нет"}";
    }
}
