using System.Net;
using Microsoft.Data.Sqlite;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementReviewHandler(
    ITelegramBotClient bot,
    long adminChatId,
    AnnouncementReviewDraftRepository drafts,
    AnnouncementParseAttemptsRepository attempts,
    PostsRepository posts,
    AnnouncementsRepository announcements,
    IChannelPostUpdater channelPostUpdater,
    BotConversationState stateStore,
    INotifier notifier)
{
    public async Task EnsureNotificationAsync(
        Post post,
        AnnouncementReviewDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.Status != AnnouncementReviewStatuses.Pending)
        {
            return;
        }

        if (!draft.SourceMessageId.HasValue)
        {
            var sourceMessageId = await notifier.NotifyNewPostAsync(post, cancellationToken);
            drafts.SetSourceMessageId(draft.PostId, sourceMessageId);
        }

        draft = drafts.Get(draft.PostId) ?? draft;
        if (!draft.ReviewMessageId.HasValue)
        {
            var message = await bot.SendMessage(
                adminChatId,
                FormatReview(draft),
                ParseMode.Html,
                replyMarkup: BuildKeyboard(draft),
                cancellationToken: cancellationToken);
            drafts.SetReviewMessageId(draft.PostId, message.MessageId);
        }

        attempts.MarkNotified(draft.PostId);
    }

    public async Task<bool> HandleCallbackQuery(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (data is null)
        {
            return false;
        }

        if (TryParsePostId(data, "autoreview_add_", out var addPostId))
        {
            await AddAsync(addPostId, callbackQuery, cancellationToken);
            return true;
        }

        if (TryParsePostId(data, "autoreview_skip_", out var skipPostId))
        {
            await SkipAsync(skipPostId, callbackQuery, cancellationToken);
            return true;
        }

        if (data.StartsWith("autoreviewedit_", StringComparison.Ordinal))
        {
            await BeginEditAsync(data, callbackQuery, cancellationToken);
            return true;
        }

        return false;
    }

    public async Task RefreshAsync(
        AnnouncementReviewDraft draft,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        await bot.EditMessageText(
            chatId,
            messageId,
            FormatReview(draft),
            ParseMode.Html,
            replyMarkup: BuildKeyboard(draft),
            cancellationToken: cancellationToken);
    }

    private async Task AddAsync(
        long postId,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var draft = drafts.Get(postId);
        if (draft is null || draft.Status != AnnouncementReviewStatuses.Pending)
        {
            await AnswerAlreadyProcessed(callbackQuery, cancellationToken);
            return;
        }

        if (!draft.IsComplete)
        {
            await bot.AnswerCallbackQuery(
                callbackQuery.Id,
                "Сначала заполните все поля.",
                cancellationToken: cancellationToken);
            return;
        }

        var post = posts.Get(postId);
        if (post is null)
        {
            await bot.AnswerCallbackQuery(
                callbackQuery.Id,
                "Исходный RSS-пост не найден.",
                cancellationToken: cancellationToken);
            return;
        }

        var alreadyExists = announcements.Exists(postId) ||
                            (!string.IsNullOrWhiteSpace(post.Link) &&
                             announcements.GetByLink(post.Link) is not null);
        if (!alreadyExists)
        {
            try
            {
                announcements.Insert(new Announcement
                {
                    Id = postId,
                    TournamentName = draft.TournamentName!,
                    Place = draft.Place!,
                    DateTimeUtc = draft.DateTimeUtc!.Value,
                    Cost = draft.Cost!.Value
                });
            }
            catch (SqliteException) when (announcements.Exists(postId))
            {
                alreadyExists = true;
            }
        }

        drafts.SetStatus(postId, AnnouncementReviewStatuses.Added);
        attempts.SetOutcome(postId, "review_added");
        attempts.MarkSaved(postId);
        if (!alreadyExists)
        {
            await channelPostUpdater.UpdateLastPostAsync(cancellationToken);
            attempts.MarkChannelUpdated(postId);
        }

        if (callbackQuery.Message is not null)
        {
            await bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"<b>Анонс добавлен</b>\n\n{FormatFields(draft)}",
                ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        await bot.AnswerCallbackQuery(
            callbackQuery.Id,
            alreadyExists ? "Анонс уже был добавлен." : "Анонс добавлен.",
            cancellationToken: cancellationToken);
    }

    private async Task SkipAsync(
        long postId,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var draft = drafts.Get(postId);
        if (draft is null || draft.Status != AnnouncementReviewStatuses.Pending)
        {
            await AnswerAlreadyProcessed(callbackQuery, cancellationToken);
            return;
        }

        drafts.SetStatus(postId, AnnouncementReviewStatuses.Skipped);
        attempts.SetOutcome(postId, "review_skipped", draft.FailureCode);
        if (callbackQuery.Message is not null)
        {
            await bot.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"<b>Кандидат пропущен</b>\n\n{FormatFields(draft)}",
                ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        await bot.AnswerCallbackQuery(
            callbackQuery.Id,
            "Кандидат пропущен.",
            cancellationToken: cancellationToken);
    }

    private async Task BeginEditAsync(
        string data,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var rest = data["autoreviewedit_".Length..];
        var separator = rest.LastIndexOf('_');
        if (separator <= 0 ||
            !long.TryParse(rest[(separator + 1)..], out var postId))
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        var draft = drafts.Get(postId);
        if (draft is null || draft.Status != AnnouncementReviewStatuses.Pending)
        {
            await AnswerAlreadyProcessed(callbackQuery, cancellationToken);
            return;
        }

        var state = stateStore.AddOrUpdate(callbackQuery.From.Id);
        state.AutomationReviewPostId = postId;
        state.AutomationReviewChatId = callbackQuery.Message?.Chat.Id;
        state.AutomationReviewMessageId = callbackQuery.Message?.MessageId;

        var field = rest[..separator];
        string prompt;
        switch (field)
        {
            case "name":
                state.Step = AddStep.AutomationReviewWaitingName;
                prompt = $"Введите название турнира. Сейчас: {draft.TournamentName ?? "не распознано"}";
                break;
            case "place":
                state.Step = AddStep.AutomationReviewWaitingPlace;
                prompt = $"Введите площадку. Сейчас: {draft.Place ?? "не распознано"}";
                break;
            case "datetime":
                state.Step = AddStep.AutomationReviewWaitingDateTime;
                var local = draft.DateTimeUtc.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(draft.DateTimeUtc.Value, PostFormatter.Moscow)
                        .ToString("dd.MM.yyyy HH:mm")
                    : "не распознано";
                prompt = $"Введите дату и время в формате ДД.ММ.ГГГГ ЧЧ:ММ. Сейчас: {local}";
                break;
            case "cost":
                state.Step = AddStep.AutomationReviewWaitingCost;
                prompt = $"Введите стоимость команды числом. Сейчас: {draft.Cost?.ToString() ?? "не распознано"}";
                break;
            default:
                await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                return;
        }

        await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        await bot.SendMessage(adminChatId, prompt, cancellationToken: cancellationToken);
    }

    private async Task AnswerAlreadyProcessed(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        await bot.AnswerCallbackQuery(
            callbackQuery.Id,
            "Черновик уже обработан.",
            cancellationToken: cancellationToken);
    }

    private static InlineKeyboardMarkup BuildKeyboard(AnnouncementReviewDraft draft)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Название", $"autoreviewedit_name_{draft.PostId}"),
                InlineKeyboardButton.WithCallbackData("Дата", $"autoreviewedit_datetime_{draft.PostId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Площадка", $"autoreviewedit_place_{draft.PostId}"),
                InlineKeyboardButton.WithCallbackData("Стоимость", $"autoreviewedit_cost_{draft.PostId}")
            }
        };
        rows.Add(draft.IsComplete
            ?
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Добавить", $"autoreview_add_{draft.PostId}"),
                InlineKeyboardButton.WithCallbackData("Пропустить", $"autoreview_skip_{draft.PostId}")
            }
            :
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Пропустить", $"autoreview_skip_{draft.PostId}")
            });
        return new InlineKeyboardMarkup(rows);
    }

    private static string FormatReview(AnnouncementReviewDraft draft)
    {
        var reason = string.IsNullOrWhiteSpace(draft.FailureCode)
            ? "все автоматические проверки пройдены"
            : WebUtility.HtmlEncode(draft.FailureCode);
        return $"<b>Кандидат требует подтверждения</b>\n" +
               $"Причина: <code>{reason}</code>\n\n" +
               FormatFields(draft);
    }

    private static string FormatFields(AnnouncementReviewDraft draft)
    {
        var date = draft.DateTimeUtc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(draft.DateTimeUtc.Value, PostFormatter.Moscow)
                .ToString("dd.MM.yyyy HH:mm")
            : null;
        return $"ID: <code>{draft.PostId}</code>\n" +
               $"Название: {Value(draft.TournamentName)}\n" +
               $"Площадка: {Value(draft.Place)}\n" +
               $"Дата: {Value(date)}\n" +
               $"Стоимость команды: {CostValue(draft.Cost)}";
    }

    private static string Value(string? value) =>
        WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "не распознано" : value);

    private static string CostValue(int? cost) =>
        cost.HasValue ? $"{cost.Value} ₽" : "не распознано";

    private static bool TryParsePostId(string data, string prefix, out long postId)
    {
        postId = 0;
        return data.StartsWith(prefix, StringComparison.Ordinal) &&
               long.TryParse(data[prefix.Length..], out postId);
    }
}
