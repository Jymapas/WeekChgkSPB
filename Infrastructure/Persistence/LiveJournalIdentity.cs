using System.Globalization;

namespace WeekChgkSPB;

/// <summary>Проверка идентификаторов и ссылок источников ЖЖ.</summary>
internal static class LiveJournalIdentity
{
    internal const string CommunityHost = "chgk-spb.livejournal.com";

    internal static bool TryNormalizeLogin(string? value, out string login)
    {
        login = (value ?? string.Empty).Trim().ToLowerInvariant();
        return login.Length is > 0 and <= 64 && login.All(c =>
            c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }

    internal static bool TryNormalizeLink(string? value, out string link, out long postId)
    {
        link = string.Empty;
        postId = 0;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !uri.Host.Equals(CommunityHost, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.StartsWith('/') || !path.EndsWith(".html", StringComparison.Ordinal)) return false;
        var digits = path.AsSpan(1, path.Length - 6);
        if (digits.IsEmpty || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out postId) || postId <= 0)
            return false;
        link = $"https://{CommunityHost}/{postId}.html";
        return true;
    }
}
