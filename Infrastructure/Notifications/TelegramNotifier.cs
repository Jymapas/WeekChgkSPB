using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace WeekChgkSPB.Infrastructure.Notifications;

public class TelegramNotifier : INotifier
{
    private readonly TelegramBotClient _bot;
    private readonly long _chatId;
    private readonly LinkPreviewOptions _noPreview = new() { IsDisabled = true };

    public TelegramNotifier(string token, long chatId)
    {
        _bot = new TelegramBotClient(token);
        _chatId = chatId;
    }

    public async Task<int> NotifyNewPostAsync(Post post, CancellationToken ct = default)
    {
        var message = await _bot.SendMessage(
            _chatId,
            PostSourceFormatter.Format(post),
            ParseMode.Html,
            linkPreviewOptions: _noPreview,
            cancellationToken: ct);
        return message.MessageId;
    }

    public Task NotifyAutomationSavedAsync(Post post, Announcement announcement, CancellationToken ct = default) =>
        SendAutomationMessageAsync("Анонс добавлен автоматически", post, announcement, ct);

    private async Task SendAutomationMessageAsync(
        string heading,
        Post post,
        Announcement announcement,
        CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(announcement.DateTimeUtc, PostFormatter.Moscow);
        var text = $"<b>{Escape(heading)}</b>\n" +
                   $"ID: <code>{post.Id}</code>\n" +
                   $"Название: {Escape(announcement.TournamentName)}\n" +
                   $"Площадка: {Escape(announcement.Place)}\n" +
                   $"Дата: {local:dd.MM.yyyy HH:mm}\n" +
                   $"Стоимость команды: {announcement.Cost} ₽";
        await _bot.SendMessage(
            _chatId,
            text,
            ParseMode.Html,
            replyMarkup: BuildAdminEditKeyboard(announcement.Id),
            linkPreviewOptions: _noPreview,
            cancellationToken: ct);
    }

    internal static InlineKeyboardMarkup BuildAdminEditKeyboard(long announcementId) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Название", $"admedit_name_{announcementId}"),
                InlineKeyboardButton.WithCallbackData("Дата и время", $"admedit_datetime_{announcementId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Площадка", $"admedit_place_{announcementId}"),
                InlineKeyboardButton.WithCallbackData("Стоимость", $"admedit_cost_{announcementId}")
            }
        });

    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

}
