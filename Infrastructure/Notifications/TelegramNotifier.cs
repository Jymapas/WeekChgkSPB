using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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

    public async Task NotifyNewPostAsync(Post post, CancellationToken ct = default)
    {
        await SendCodeBlock(post.Id.ToString(), ct);

        await SendCodeBlock(post.Title ?? "", ct);

        var body = post.Description ?? "";
        foreach (var chunk in SplitBy(body, 4000))
            await SendCodeBlock(chunk, ct);
    }

    public Task NotifyAutomationCandidateAsync(Post post, Announcement announcement, CancellationToken ct = default) =>
        SendAutomationMessageAsync("SHADOW — кандидат не сохранён", post, announcement, ct);

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
            linkPreviewOptions: _noPreview,
            cancellationToken: ct);
    }

    private async Task SendCodeBlock(string text, CancellationToken ct)
    {
        var escaped = Escape(text);
        await _bot.SendMessage(
            _chatId,
            $"<code>{escaped}</code>",
            ParseMode.Html,
            linkPreviewOptions: _noPreview,
            cancellationToken: ct);
        await Task.Delay(500, ct);
    }

    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static IEnumerable<string> SplitBy(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            yield return string.Empty;
            yield break;
        }

        for (var i = 0; i < s.Length; i += max)
            yield return s.Substring(i, Math.Min(max, s.Length - i));
    }
}
