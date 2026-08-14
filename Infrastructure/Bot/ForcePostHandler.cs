using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class ForcePostHandler
{
    private const string ConfirmData = "forcepost_confirm";
    private const string CancelData = "forcepost_cancel";

    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly ChannelPostsRepository _history;
    private readonly ITelegramBotClient _bot;
    private readonly string? _channelId;

    public ForcePostHandler(
        AnnouncementsRepository announcements,
        FootersRepository footers,
        ChannelPostsRepository history,
        ITelegramBotClient bot,
        string? channelId)
    {
        _announcements = announcements;
        _footers = footers;
        _history = history;
        _bot = bot;
        _channelId = channelId;
    }

    public async Task<bool> HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var data = callbackQuery.Data;
        if (data is null || callbackQuery.Message is null)
        {
            return false;
        }

        if (data == CancelData)
        {
            await CancelAsync(callbackQuery, ct);
            return true;
        }

        if (data == ConfirmData)
        {
            await ConfirmAsync(callbackQuery, ct);
            return true;
        }

        return false;
    }

    private async Task CancelAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var message = callbackQuery.Message!;
        await _bot.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            (message.Text ?? string.Empty) + Messages.ForcePost.Cancelled,
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
    }

    private async Task ConfirmAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var message = callbackQuery.Message!;

        if (string.IsNullOrEmpty(_channelId))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.ForcePost.NoChannel, cancellationToken: ct);
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowLocal.Date, DateTimeKind.Unspecified), PostFormatter.Moscow);
        var rows = _announcements.GetWithLinksInRange(fromUtc);
        if (rows.Count == 0)
        {
            await _bot.EditMessageText(
                message.Chat.Id,
                message.MessageId,
                (message.Text ?? string.Empty) + Messages.ForcePost.Cancelled,
                cancellationToken: ct);
            await _bot.AnswerCallbackQuery(callbackQuery.Id, Messages.ForcePost.NoAnnouncements, cancellationToken: ct);
            return;
        }

        var footerLines = _footers.GetAllTextsDesc();
        var text = PostFormatter.BuildScheduleMessage(rows, footerLines);

        var sentMessage = await _bot.SendMessage(
            _channelId,
            text,
            ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);

        _history.MarkPosted(DateTime.UtcNow, DateTime.UtcNow, sentMessage.MessageId);

        await _bot.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            text + Messages.ForcePost.Published,
            ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);

        await _bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
    }
}
