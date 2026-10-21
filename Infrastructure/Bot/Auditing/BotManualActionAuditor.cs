using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Infrastructure.Bot.Auditing;

internal sealed class BotManualActionAuditor(
    ManualActionsRepository repository,
    BotConversationState states,
    AnnouncementsRepository announcements,
    PostsRepository posts,
    FootersRepository footers,
    UserManagementRepository users,
    AnnouncementReviewDraftRepository drafts,
    BotCommandHelper helper,
    long allowedChatId,
    LiveJournalIdentityRepository? identities = null)
{
    internal ManualAction? Begin(Update update)
    {
        var callback = update.CallbackQuery;
        var message = callback?.Message ?? update.Message;
        var user = callback?.From ?? message?.From;
        if (callback is null && message is null) return null;

        AddAnnouncementState? state = null;
        if (user is not null) states.TryGet(user.Id, out state);
        var text = callback?.Data ?? message?.Text ?? message?.Caption;
        var command = message?.Text?.StartsWith('/') == true && callback is null;
        var name = callback is not null ? CallbackName(callback.Data) :
            command ? CommandName(text!) : message?.Text is null ? message?.Type.ToString() ?? "unknown" :
            state?.Step.ToString() ?? "None";
        var denied = (callback is not null && message?.Chat.Id != allowedChatId) ||
            (user is not null && users.IsBanned(user.Id));
        if (command && message?.Chat.Id != allowedChatId &&
            BotCommands.AdminOnly.Contains(name, StringComparer.OrdinalIgnoreCase)) denied = true;

        var action = new ManualAction
        {
            UpdateId = update.Id, UserId = user?.Id, ChatId = message?.Chat.Id,
            MessageId = message?.MessageId, CallbackQueryId = callback?.Id,
            Kind = callback is not null ? "callback" : command ? "command" :
                message?.Text is null ? "unsupported_message" : "input",
            Name = AuditText.Redact(name)!, Input = denied ? null : AuditText.Redact(text),
            State = denied ? null : state, StepBefore = denied ? "None" : state?.Step.ToString() ?? "None",
            Denied = denied
        };
        if (!denied)
        {
            ResolveTarget(action, callback?.Data, command ? text : null);
            if (state?.Step == AddStep.WaitingId && !command)
                action.SourceLink = helper.NormalizePostLink(text);
            ResolveLink(action);
            var editCommand = command && name is BotCommands.EditName or BotCommands.EditPlace or
                BotCommands.EditDateTime or BotCommands.EditCost or BotCommands.Delete;
            var editInput = !command && callback is null && state?.Step is AddStep.EditWaitingName or
                AddStep.EditWaitingPlace or AddStep.EditWaitingDateTime or AddStep.EditWaitingCost;
            var actor = new AnnouncementActor(user?.Id, message?.Chat.Id == allowedChatId,
                message?.Chat.Type == Telegram.Bot.Types.Enums.ChatType.Private);
            if ((editCommand || editInput) &&
                ((editInput && state?.EditChatId != message?.Chat.Id) ||
                 (action.EntityId.HasValue && !announcements.CanManage(action.EntityId.Value, actor))))
                action.Denied = true;
            if (!action.Denied) action.BeforeJson = Snapshot(action, state);
            else RedactDenied(action);
        }
        action.Id = repository.Begin(action);
        return action;
    }

    internal void Complete(ManualAction action, Exception? exception)
    {
        AddAnnouncementState? current = null;
        if (action.UserId.HasValue) states.TryGet(action.UserId.Value, out current);
        action.StepAfter = current?.Step.ToString() ?? "None";
        if (action.Denied) RedactDenied(action);
        if (!action.Denied)
        {
            if (action.StepBefore.StartsWith("Waiting", StringComparison.Ordinal) &&
                action.State?.SubmittedPendingId is long submittedPendingId)
                (action.EntityType, action.EntityId) = ("pending_announcement", submittedPendingId);
            ResolveStateTarget(action, current ?? action.State);
            ResolveLink(action);
            action.AfterJson = Snapshot(action, current ?? action.State);
        }
        if (exception is not null)
            action.Outcome = exception is OperationCanceledException ? "cancelled" : "error";
        else if (action.Denied)
            action.Outcome = "denied";
        else if (action.Outcome == "handled" && action.Kind == "input" &&
                 action.StepBefore != "None" && action.StepBefore == action.StepAfter)
            action.Outcome = "awaiting_input";
        repository.Complete(action, exception?.GetType().FullName);
    }

    /// <summary>Оставляет у запрещённого действия только служебные метаданные.</summary>
    private static void RedactDenied(ManualAction action)
    {
        action.Input = null;
        action.SourceLink = null;
        action.BeforeJson = null;
        action.AfterJson = null;
        action.State = null;
        action.StepBefore = "None";
        action.StepAfter = "None";
    }

    /// <summary>Извлекает имя команды независимо от имени бота и аргументов.</summary>
    private static string CommandName(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Split('@')[0].ToLowerInvariant();

    /// <summary>Отделяет идентификатор сущности от названия callback-действия.</summary>
    private static string CallbackName(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "unknown";
        var separator = data.LastIndexOf('_');
        return separator > 0 && long.TryParse(data[(separator + 1)..], out _)
            ? data[..separator] : data;
    }

    /// <summary>Определяет сущность по кнопке, команде или текущему шагу диалога.</summary>
    private void ResolveTarget(ManualAction action, string? callback, string? command)
    {
        if (callback is not null)
        {
            var separator = callback.LastIndexOf('_');
            if (separator > 0 && long.TryParse(callback[(separator + 1)..], out var id))
            {
                action.EntityType = callback.StartsWith("autoreview", StringComparison.Ordinal) ? "review_draft" :
                    callback.StartsWith("admedit_", StringComparison.Ordinal) ? "announcement" :
                    callback.StartsWith("footeredit_", StringComparison.Ordinal) ? "footer" :
                    callback.StartsWith("mod_", StringComparison.Ordinal) ||
                    callback.StartsWith("modedit", StringComparison.Ordinal) ||
                    callback.StartsWith("pmedit_", StringComparison.Ordinal) ? "pending_announcement" : null;
                if (action.EntityType is not null) action.EntityId = id;
            }
            return;
        }
        if (command is not null)
        {
            var parts = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (action.Name is BotCommands.LjBind or BotCommands.LjUnbind or BotCommands.LjList)
            {
                action.EntityType = "lj_user";
                if (parts.Length > 1 && long.TryParse(parts[1], out var telegramId) && telegramId > 0)
                    action.EntityId = telegramId;
                return;
            }
            if (parts.Length > 1 && action.Name == BotCommands.FooterDel && long.TryParse(parts[1], out var footerId))
            {
                action.EntityType = "footer";
                action.EntityId = footerId;
                return;
            }
            if (parts.Length > 1 && (action.Name.StartsWith("/edit", StringComparison.Ordinal) || action.Name == BotCommands.Delete))
            {
                action.SourceLink = helper.NormalizePostLink(parts[1]);
                var existing = announcements.GetByLink(action.SourceLink);
                if (existing is not null)
                {
                    action.EntityType = "announcement";
                    action.EntityId = existing.Id;
                }
                return;
            }
            if (action.Name != BotCommands.Cancel) return;
        }
        ResolveStateTarget(action, action.State);
    }

    /// <summary>Сохраняет связь с сущностью даже после очистки состояния обработчиком.</summary>
    private static void ResolveStateTarget(ManualAction action, AddAnnouncementState? state)
    {
        if (state is null) return;
        if (action.EntityId is null)
        {
            if (state.Step.ToString().StartsWith("AutomationReview", StringComparison.Ordinal) && state.AutomationReviewPostId.HasValue)
                (action.EntityType, action.EntityId) = ("review_draft", state.AutomationReviewPostId);
            else if (state.Step.ToString().StartsWith("PendingEdit", StringComparison.Ordinal) && state.PendingEditId.HasValue)
                (action.EntityType, action.EntityId) = ("pending_announcement", state.PendingEditId);
            else if ((state.Step.ToString().StartsWith("FooterEdit", StringComparison.Ordinal) ||
                      (action.StepBefore == nameof(AddStep.FooterWaitingExpiry) && state.Step == AddStep.Done)) && state.FooterEditId > 0)
                (action.EntityType, action.EntityId) = ("footer", state.FooterEditId);
            else if (state.Step.ToString().StartsWith("EditWaiting", StringComparison.Ordinal) && state.Existing is not null)
                (action.EntityType, action.EntityId) = ("announcement", state.Existing.Id);
            else if ((state.Step.ToString().StartsWith("Waiting", StringComparison.Ordinal) || state.Step == AddStep.Done) && state.Draft.Id > 0)
                (action.EntityType, action.EntityId) = ("announcement", state.Draft.Id);
        }
        if (string.IsNullOrEmpty(action.SourceLink) && !string.IsNullOrEmpty(state.DraftLink) &&
            (state.Step.ToString().StartsWith("Waiting", StringComparison.Ordinal) || state.Step == AddStep.Done))
            action.SourceLink = state.DraftLink;
    }

    /// <summary>Дополняет ссылку и идентификатор из сохранённых данных.</summary>
    private void ResolveLink(ManualAction action)
    {
        if (action.EntityId.HasValue && action.EntityType is "announcement" or "review_draft")
            action.SourceLink ??= posts.Get(action.EntityId.Value)?.Link;
        if (action.EntityId.HasValue && action.EntityType == "pending_announcement")
            action.SourceLink ??= users.GetPending(action.EntityId.Value)?.Link;
        if (!string.IsNullOrEmpty(action.SourceLink) && action.EntityId is null)
        {
            var saved = announcements.GetByLink(action.SourceLink);
            if (saved is not null) (action.EntityType, action.EntityId) = ("announcement", saved.Id);
            else if (posts.TryGetIdByLink(action.SourceLink, out var id))
                (action.EntityType, action.EntityId) = ("announcement", id);
        }
        action.SourceLink = AuditText.Redact(action.SourceLink);
    }

    /// <summary>Фиксирует значения диалога и сущности до их последующего изменения.</summary>
    private string Snapshot(ManualAction action, AddAnnouncementState? state)
    {
        object? entity = action.EntityType switch
        {
            "announcement" when action.EntityId.HasValue => announcements.Get(action.EntityId.Value),
            "review_draft" when action.EntityId.HasValue => drafts.Get(action.EntityId.Value),
            "pending_announcement" when action.EntityId.HasValue => users.GetPending(action.EntityId.Value),
            "footer" when action.EntityId.HasValue => FooterSnapshot(action.EntityId.Value),
            "lj_user" => identities?.List(action.EntityId),
            _ => null
        };
        var savedByLink = string.IsNullOrEmpty(action.SourceLink) ? null : announcements.GetByLink(action.SourceLink);
        return AuditText.Snapshot(new
        {
            step = state?.Step.ToString() ?? "None",
            draft = state?.Draft,
            draftLink = state?.DraftLink,
            footerText = state?.FooterDraftText,
            entity,
            savedAnnouncement = savedByLink
        });
    }

    /// <summary>Преобразует footer в объект с явно именованными полями JSON.</summary>
    private object? FooterSnapshot(long id)
    {
        var footer = footers.Get(id);
        return footer.HasValue ? new { footer.Value.Id, footer.Value.Text, footer.Value.ExpiresAt } : null;
    }
}
