using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WeekChgkSPB.Infrastructure.Notifications;

public class TelegramNotifier : INotifier
{
    private const int TgLimit = 4096;
    private readonly TelegramBotClient _bot;
    private readonly long _chatId;
    private readonly LinkPreviewOptions _linkPreviewOptions = new() { IsDisabled = true };

    public TelegramNotifier(string token, long chatId)
    {
        _bot = new TelegramBotClient(token);
        _chatId = chatId;
    }

    public async Task NotifyNewPostAsync(Post post, CancellationToken ct = default)
    {
        var text = $"<b>Новый пост</b>\n{Escape(post.Title)}\n{post.Link}";
        text = text.Length > TgLimit ? text[..TgLimit] : text;

        await _bot.SendMessage(
            _chatId,
            text,
            ParseMode.Html,
            linkPreviewOptions: _linkPreviewOptions,
            cancellationToken: ct);
    }

    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}