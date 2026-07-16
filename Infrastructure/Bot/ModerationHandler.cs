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
    private readonly FootersRepository _footers;
    private readonly long _adminChatId;
    private readonly BotConversationState _stateStore;

    public ModerationHandler(
        ITelegramBotClient bot,
        AnnouncementsRepository announcements,
        UserManagementRepository userManagement,
        PostsRepository posts,
        IChannelPostUpdater channelPostUpdater,
        FootersRepository footers,
        long adminChatId,
        BotConversationState stateStore)
    {
        _bot = bot;
        _announcements = announcements;
        _userManagement = userManagement;
        _posts = posts;
        _channelPostUpdater = channelPostUpdater;
        _footers = footers;
        _adminChatId = adminChatId;
        _stateStore = stateStore;
    }

    public async Task<bool> HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data is null)
            return false;

        if (callbackQuery.Data.StartsWith("admedit_"))
            return await HandleAdminEditCallback(callbackQuery, ct);

        if (callbackQuery.Data.StartsWith("footeredit_"))
            return await HandleFooterEditCallback(callbackQuery, ct);

        if (callbackQuery.Data.StartsWith("modeditback_"))
            return await HandleModEditBackCallback(callbackQuery, ct);

        if (callbackQuery.Data.StartsWith("modedit_"))
            return await HandleModEditMenuCallback(callbackQuery, ct);

        if (callbackQuery.Data.StartsWith("pmedit_"))
            return await HandlePendingFieldEditCallback(callbackQuery, ct);

        if (!callbackQuery.Data.StartsWith("mod_"))
            return false;

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

    private async Task<bool> HandleAdminEditCallback(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split('_');
        // Format: admedit_{field}_{announcementId}
        if (parts.Length < 3 || !long.TryParse(parts[2], out var announcementId))
            return false;

        var announcement = _announcements.Get(announcementId);
        if (announcement is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.AdminEditNotFound, cancellationToken: ct);
            return true;
        }

        var adminUserId = callbackQuery.From.Id;
        var state = _stateStore.AddOrUpdate(adminUserId);
        state.Existing = announcement;

        string prompt;
        switch (parts[1])
        {
            case "name":
                state.Step = AddStep.EditWaitingName;
                prompt = Messages.Edit.NamePrompt(announcement.Id, announcement.TournamentName);
                break;
            case "datetime":
                state.Step = AddStep.EditWaitingDateTime;
                var moscowDt = TimeZoneInfo.ConvertTimeFromUtc(announcement.DateTimeUtc, PostFormatter.Moscow);
                prompt = Messages.Edit.DateTimePrompt(announcement.Id, moscowDt.ToString("dd.MM.yyyy HH:mm"));
                break;
            case "place":
                state.Step = AddStep.EditWaitingPlace;
                prompt = Messages.Edit.PlacePrompt(announcement.Id, announcement.Place);
                break;
            case "cost":
                state.Step = AddStep.EditWaitingCost;
                prompt = Messages.Edit.CostPrompt(announcement.Id, announcement.Cost, announcement.CostLabel);
                break;
            default:
                return false;
        }

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await _bot.SendMessage(_adminChatId, prompt, cancellationToken: ct);
        return true;
    }

    private async Task<bool> HandleFooterEditCallback(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var data = callbackQuery.Data!;
        // Format: footeredit_{action}_{footerId}
        var withoutPrefix = data["footeredit_".Length..];
        var underscoreIdx = withoutPrefix.IndexOf('_');
        if (underscoreIdx < 0) return false;

        var action = withoutPrefix[..underscoreIdx];
        if (!long.TryParse(withoutPrefix[(underscoreIdx + 1)..], out var footerId))
            return false;

        if (action == "select")
        {
            var footer = _footers.Get(footerId);
            if (footer is null)
            {
                await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Footer.NotFound, cancellationToken: ct);
                return true;
            }

            DateTime? displayDate = footer.Value.ExpiresAt.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(footer.Value.ExpiresAt.Value, PostFormatter.Moscow)
                : null;

            var text = Messages.Footer.Detail(footer.Value.Id, footer.Value.Text, displayDate);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(Messages.Footer.ButtonEditText, $"footeredit_text_{footerId}"),
                    InlineKeyboardButton.WithCallbackData(Messages.Footer.ButtonEditExpiry, $"footeredit_expiry_{footerId}")
                }
            });

            await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            await _bot.SendMessage(_adminChatId, text, replyMarkup: keyboard, cancellationToken: ct);
            return true;
        }

        if (action is "text" or "expiry")
        {
            var footer = _footers.Get(footerId);
            if (footer is null)
            {
                await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Footer.NotFound, cancellationToken: ct);
                return true;
            }

            var adminUserId = callbackQuery.From.Id;
            var state = _stateStore.AddOrUpdate(adminUserId);
            state.FooterEditId = footerId;

            string prompt;
            if (action == "text")
            {
                state.Step = AddStep.FooterEditWaitingText;
                prompt = Messages.Footer.EditTextPrompt;
            }
            else
            {
                state.Step = AddStep.FooterEditWaitingExpiry;
                prompt = Messages.Footer.EditExpiryPrompt;
            }

            await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            await _bot.SendMessage(_adminChatId, prompt, cancellationToken: ct);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleModEditMenuCallback(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split('_');
        if (parts.Length < 2 || !long.TryParse(parts[1], out var pendingId))
            return false;

        var pending = _userManagement.GetPending(pendingId);
        if (pending is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.RequestNotFound, cancellationToken: ct);
            return true;
        }

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageReplyMarkup(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                BuildPendingEditFieldsKeyboard(pendingId),
                cancellationToken: ct);
        }

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        return true;
    }

    private async Task<bool> HandleModEditBackCallback(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split('_');
        if (parts.Length < 2 || !long.TryParse(parts[1], out var pendingId))
            return false;

        var pending = _userManagement.GetPending(pendingId);
        if (pending is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.RequestNotFound, cancellationToken: ct);
            return true;
        }

        if (callbackQuery.Message is not null)
        {
            await _bot.EditMessageReplyMarkup(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                BuildModerationKeyboard(pendingId),
                cancellationToken: ct);
        }

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        return true;
    }

    private async Task<bool> HandlePendingFieldEditCallback(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split('_');
        // Format: pmedit_{field}_{pendingId}
        if (parts.Length < 3 || !long.TryParse(parts[2], out var pendingId))
            return false;

        var pending = _userManagement.GetPending(pendingId);
        if (pending is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Moderation.RequestNotFound, cancellationToken: ct);
            return true;
        }

        var adminUserId = callbackQuery.From.Id;
        var state = _stateStore.AddOrUpdate(adminUserId);
        state.PendingEditId = pendingId;
        if (callbackQuery.Message is not null)
        {
            state.PendingEditChatId = callbackQuery.Message.Chat.Id;
            state.PendingEditMessageId = callbackQuery.Message.MessageId;
        }

        string prompt;
        switch (parts[1])
        {
            case "name":
                state.Step = AddStep.PendingEditWaitingName;
                prompt = Messages.Edit.NamePrompt(pending.Id, pending.TournamentName);
                break;
            case "datetime":
                state.Step = AddStep.PendingEditWaitingDateTime;
                var moscowDt = TimeZoneInfo.ConvertTimeFromUtc(pending.DateTimeUtc, PostFormatter.Moscow);
                prompt = Messages.Edit.DateTimePrompt(pending.Id, moscowDt.ToString("dd.MM.yyyy HH:mm"));
                break;
            case "place":
                state.Step = AddStep.PendingEditWaitingPlace;
                prompt = Messages.Edit.PlacePrompt(pending.Id, pending.Place);
                break;
            case "cost":
                state.Step = AddStep.PendingEditWaitingCost;
                prompt = Messages.Edit.CostPrompt(pending.Id, pending.Cost, pending.CostLabel);
                break;
            default:
                return false;
        }

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await _bot.SendMessage(_adminChatId, prompt, cancellationToken: ct);
        return true;
    }

    internal async Task RefreshModerationMessage(PendingAnnouncement pending, long chatId, int messageId, CancellationToken ct)
    {
        var userInfo = pending.UserName ?? $"#{pending.UserId}";
        var text = $"{Messages.Moderation.NewRequest}\n\n{FormatPendingAnnouncement(pending, userInfo)}";

        await _bot.EditMessageText(
            chatId,
            messageId,
            text,
            replyMarkup: BuildModerationKeyboard(pending.Id),
            cancellationToken: ct);
    }

    private static InlineKeyboardMarkup BuildModerationKeyboard(long pendingId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonApprove, $"mod_approve_{pendingId}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonAllow, $"mod_allow_{pendingId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonReject, $"mod_reject_{pendingId}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonBan, $"mod_ban_{pendingId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEdit, $"modedit_{pendingId}")
            }
        });
    }

    private static InlineKeyboardMarkup BuildPendingEditFieldsKeyboard(long pendingId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditName, $"pmedit_name_{pendingId}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditTime, $"pmedit_datetime_{pendingId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditPlace, $"pmedit_place_{pendingId}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditCost, $"pmedit_cost_{pendingId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonBack, $"modeditback_{pendingId}")
            }
        });
    }

    private async Task HandleApprove(PendingAnnouncement pending, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var announcement = new Announcement
        {
            TournamentName = pending.TournamentName,
            Place = pending.Place,
            DateTimeUtc = pending.DateTimeUtc,
            Cost = pending.Cost,
            CostLabel = pending.CostLabel,
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

        var userInfo = pending.UserName ?? $"#{pending.UserId}";

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
            CostLabel = pending.CostLabel,
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

        var userInfo = pending.UserName ?? $"#{pending.UserId}";

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

        var userInfo = pending.UserName ?? $"#{pending.UserId}";

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

        var userInfo = pending.UserName ?? $"#{pending.UserId}";

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

        await _bot.SendMessage(_adminChatId, text, replyMarkup: BuildModerationKeyboard(pending.Id), cancellationToken: ct);
    }

    public async Task SendAllowedUserNotification(Announcement announcement, string userName, CancellationToken ct)
    {
        var text = $"{Messages.Moderation.AllowedUserNewAnnouncement}\n\n{FormatAnnouncementForAdmin(announcement, userName)}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditName, $"admedit_name_{announcement.Id}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditTime, $"admedit_datetime_{announcement.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditPlace, $"admedit_place_{announcement.Id}"),
                InlineKeyboardButton.WithCallbackData(Messages.Moderation.ButtonEditCost, $"admedit_cost_{announcement.Id}")
            }
        });

        await _bot.SendMessage(_adminChatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private static string FormatPendingAnnouncement(PendingAnnouncement pending, string userInfo)
    {
        var moscowDt = TimeZoneInfo.ConvertTimeFromUtc(pending.DateTimeUtc, PostFormatter.Moscow);
        return $"Пользователь: {userInfo}\n" +
               $"Название: {pending.TournamentName}\n" +
               $"Место: {pending.Place}\n" +
               $"Дата и время: {moscowDt:yyyy-MM-dd HH:mm} МСК\n" +
               $"Стоимость: {PostFormatter.FormatCost(pending.Cost, pending.CostLabel)}\n" +
               $"Ссылка: {pending.Link ?? "нет"}";
    }

    private static string FormatAnnouncementForAdmin(Announcement announcement, string userInfo)
    {
        return $"Пользователь: {userInfo}\n" +
               $"Название: {announcement.TournamentName}\n" +
               $"Место: {announcement.Place}\n" +
               $"Дата и время: {announcement.DateTimeUtc:yyyy-MM-dd HH:mm} UTC\n" +
               $"Стоимость: {PostFormatter.FormatCost(announcement.Cost, announcement.CostLabel)}";
    }
}
