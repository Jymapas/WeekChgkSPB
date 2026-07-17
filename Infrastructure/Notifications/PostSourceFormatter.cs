using System.Net;
using System.Text.RegularExpressions;

namespace WeekChgkSPB.Infrastructure.Notifications;

internal static class PostSourceFormatter
{
    private const int TelegramSafeLength = 4_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex BreakRegex = new(
        @"<(?:br\s*/?|/p|/div|/li|hr\s*/?)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TagsRegex = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex HorizontalWhitespaceRegex = new(
        @"[ \t\u00A0]+",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex BlankLinesRegex = new(
        @"\n{3,}",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string Format(Post post)
    {
        var title = CleanInline(post.Title);
        var body = CleanBody(post.Description);
        var link = (post.Link?.Trim() ?? string.Empty);
        if (link.Length > 500)
        {
            link = link[..500];
        }

        var content =
            $"ID: <code>{post.Id}</code>\n" +
            $"Заголовок: <code>{WebUtility.HtmlEncode(title)}</code>\n\n" +
            WebUtility.HtmlEncode(body);
        var linkLine = string.IsNullOrWhiteSpace(link)
            ? string.Empty
            : $"\n\nСсылка: {WebUtility.HtmlEncode(link)}";
        var result = content + linkLine;
        if (result.Length <= TelegramSafeLength)
        {
            return result;
        }

        const string suffix = "\n\n[Текст сокращён — полный текст доступен по ссылке]";
        var trailer = suffix + linkLine;
        var availableContentLength = Math.Max(0, TelegramSafeLength - trailer.Length);
        return string.Concat(
            content.AsSpan(0, Math.Min(content.Length, availableContentLength)),
            trailer);
    }

    private static string CleanInline(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty);
        decoded = TagsRegex.Replace(decoded, " ");
        return HorizontalWhitespaceRegex.Replace(decoded, " ").Trim();
    }

    private static string CleanBody(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        decoded = BreakRegex.Replace(decoded, "\n");
        decoded = TagsRegex.Replace(decoded, " ");
        decoded = HorizontalWhitespaceRegex.Replace(decoded, " ");
        return BlankLinesRegex.Replace(decoded, "\n\n").Trim();
    }
}
