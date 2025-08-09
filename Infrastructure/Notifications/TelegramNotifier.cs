using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WeekChgkSPB.Infrastructure.Notifications;

public class TelegramNotifier : INotifier
{
    private readonly TelegramBotClient _bot;
    private readonly long _chatId;
    private readonly LinkPreviewOptions _linkPreviewOptions = new LinkPreviewOptions { IsDisabled = true };

    public TelegramNotifier(string token, long chatId)
    {
        _bot = new TelegramBotClient(token);
        _chatId = chatId;
    }

    public async Task NotifyNewPostAsync(Post post, CancellationToken ct = default)
    {
        var text = $"<b>Новый пост</b>\n{Escape(post.Title)}\n{post.Link}";
        await _bot.SendMessage(
            chatId: _chatId,
            text: text,
            parseMode: ParseMode.Html,
            linkPreviewOptions: _linkPreviewOptions,
            cancellationToken: ct);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}