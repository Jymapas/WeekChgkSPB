using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementPreParser(TimeZoneInfo moscowTimeZone)
{
    private const int MaxSourceLength = 12_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly string[] MonthNames =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];
    private static readonly string[] SectionEndMarkers =
    [
        "депозит", "приз", "редактор", "для участия", "для того, чтобы", "регистрац",
        "ведущ", "максимум", "заявк", "контакт", "зарегистрирован"
    ];
    private static readonly string[] DiscountMarkers =
    [
        "студент", "пар", "соло", "трио", "скид", "вторая игра", "вторую игру", "депозит"
    ];
    private static readonly Regex TagsRegex = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex BreakRegex = new(
        @"<(?:br\s*/?|/p|/div|/li|hr\s*/?)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WhitespaceRegex = new(
        @"[ \t\u00A0]+",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex BlankLinesRegex = new(
        @"\n{2,}",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex DateRegex = new(
        @"(?<!\d)(?<day>\d{1,2})(?:-го)?\s+(?<month>января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)(?:\s+(?<year>20\d{2}))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TimeRegex = new(
        @"(?<!\d)(?<hour>[01]?\d|2[0-3])[:.-](?<minute>[0-5]\d)(?!\d)",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MoneyRegex = new(
        @"(?<!\d)(?<amount>\d[\d ]{2,5})\s*(?:руб(?:\.|лей|ля)?|₽)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex FullTeamRegex = new(
        @"(?:до\s*)?6(?:\s*[-–—]?\s*(?:ти|х))?\s*(?:чел(?:овек)?|игрок)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SmallTeamRegex = new(
        @"(?:до|из)?\s*[1-3]\s*(?:-?х)?\s*(?:чел(?:овек)?|игрок)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex QuotedVenueRegex = new(
        "(?:\\bв|на\\s+площадке\\s*:?|по\\s+адресу\\s*:)\\s*(?:кафе|клубе|клуб|ресторане|ресторан|баре|бар)?\\s*[\"«](?<place>[^\"»\\r\\n]{1,80})[\"»]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex UnquotedVenueRegex = new(
        @"(?:\bв|на\s+площадке\s*:?)\s+(?:кафе|клубе|ресторане|баре)\s+(?<place>[A-Za-zА-Яа-яЁё0-9'’`.-]{2,80})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public AnnouncementPreParseResult Parse(Post post, DateTime nowUtc)
    {
        var sourceLength = (post.Title?.Length ?? 0) + (post.Description?.Length ?? 0);
        if (sourceLength > MaxSourceLength)
        {
            return AnnouncementPreParseResult.Failed("source_too_large", sourceLength);
        }

        try
        {
            var title = CleanInline(post.Title);
            var body = CleanBody(post.Description);
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var eventLine = FindEventLine(lines);
            if (string.IsNullOrWhiteSpace(eventLine))
            {
                return AnnouncementPreParseResult.Failed("event_not_found", sourceLength);
            }

            var compactEventText = string.Equals(title, eventLine, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title}\n{eventLine}";

            var price = ParseCost(lines);
            if (!price.Success)
            {
                return AnnouncementPreParseResult.Failed(
                    price.FailureCode!,
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence);
            }

            if (!TryParseLocalDateTime(title, eventLine, nowUtc, out var localDateTime))
            {
                return AnnouncementPreParseResult.Failed(
                    "datetime_not_found",
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence);
            }

            var place = TryParsePlace(compactEventText);
            if (string.IsNullOrWhiteSpace(place))
            {
                return AnnouncementPreParseResult.Failed(
                    "place_not_found",
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence);
            }

            return new AnnouncementPreParseResult(
                true,
                null,
                compactEventText,
                price.Cost,
                price.Evidence,
                localDateTime,
                place,
                sourceLength);
        }
        catch (RegexMatchTimeoutException)
        {
            return AnnouncementPreParseResult.Failed("regex_timeout", sourceLength);
        }
    }

    internal static string NormalizePlace(string value)
    {
        var normalized = value.Trim().Trim('"', '\'', '«', '»')
            .Replace('`', '\'')
            .Replace('’', '\'');
        return normalized.ToLowerInvariant() switch
        {
            "rossi's" => "Rossi's",
            "барбоссов" => "БарБоссов",
            "барбосов" => "БарБоссов",
            "blackwood" => "BlackWood",
            _ => normalized
        };
    }

    private static string CleanInline(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty);
        decoded = TagsRegex.Replace(decoded, " ");
        return WhitespaceRegex.Replace(decoded, " ").Trim();
    }

    private static string CleanBody(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        decoded = BreakRegex.Replace(decoded, "\n");
        decoded = TagsRegex.Replace(decoded, " ");
        decoded = WhitespaceRegex.Replace(decoded, " ");
        return BlankLinesRegex.Replace(decoded, "\n").Trim();
    }

    private static string? FindEventLine(IReadOnlyList<string> lines)
    {
        var complete = lines
            .Where(line => DateRegex.IsMatch(line) && TimeRegex.IsMatch(line))
            .OrderByDescending(ScoreEventLine)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(complete))
        {
            return complete;
        }

        var eventDescription = lines
            .Where(line => DateRegex.IsMatch(line))
            .OrderByDescending(ScoreEventLine)
            .FirstOrDefault();
        var gameTime = lines.FirstOrDefault(line =>
            TimeRegex.IsMatch(line) &&
            (line.Contains("начало турнира", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("первый вопрос", StringComparison.OrdinalIgnoreCase)));
        return eventDescription is not null && gameTime is not null
            ? $"{eventDescription} {gameTime}"
            : null;
    }

    private static int ScoreEventLine(string line)
    {
        var score = 0;
        if (line.Contains('"') || line.Contains('«')) score += 4;
        if (line.Contains("синхрон", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (line.Contains("кафе", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("клуб", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("ресторан", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("площад", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (line.StartsWith("Начало регистрации", StringComparison.OrdinalIgnoreCase)) score -= 5;
        return score;
    }

    private static (bool Success, string? FailureCode, int? Cost, string? Evidence) ParseCost(IReadOnlyList<string> lines)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("стоимост", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("взнос", StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return (false, "cost_not_found", null, null);
        }

        var candidates = new List<(int Cost, string Evidence)>();
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > start && SectionEndMarkers.Any(marker => line.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            var money = MoneyRegex.Match(line);
            if (!money.Success || !IsFullTeamPrice(line, money.Index, i == start))
            {
                continue;
            }

            var amountText = money.Groups["amount"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0)
            {
                candidates.Add((amount, line));
            }
        }

        var distinct = candidates.DistinctBy(candidate => candidate.Cost).ToList();
        return distinct.Count switch
        {
            0 => (false, "cost_not_found", null, null),
            1 => (true, null, distinct[0].Cost, distinct[0].Evidence),
            _ => (false, "cost_ambiguous", null, string.Join(" | ", distinct.Select(candidate => candidate.Evidence)))
        };
    }

    private static bool IsFullTeamPrice(string line, int moneyIndex, bool isHeadingLine)
    {
        var lower = line.ToLowerInvariant();
        var hasTeam = lower.Contains("команд", StringComparison.Ordinal);
        if (!hasTeam)
        {
            return isHeadingLine && line.StartsWith("взнос", StringComparison.OrdinalIgnoreCase) &&
                   !DiscountMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
        }

        var prefix = lower[..Math.Min(moneyIndex, lower.Length)];
        if (DiscountMarkers.Any(marker => prefix.Contains(marker, StringComparison.Ordinal)))
        {
            return false;
        }

        if (FullTeamRegex.IsMatch(lower))
        {
            return true;
        }

        return !SmallTeamRegex.IsMatch(lower);
    }

    private bool TryParseLocalDateTime(string title, string eventLine, DateTime nowUtc, out DateTime localDateTime)
    {
        var combined = $"{title}\n{eventLine}";
        var dateMatch = DateRegex.Match(combined);
        var timeMatch = TimeRegex.Match(title);
        if (!timeMatch.Success)
        {
            var matches = TimeRegex.Matches(eventLine);
            timeMatch = matches.Count > 0 ? matches[matches.Count - 1] : Match.Empty;
        }

        if (!dateMatch.Success || !timeMatch.Success)
        {
            localDateTime = default;
            return false;
        }

        var day = int.Parse(dateMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
        var monthName = dateMatch.Groups["month"].Value.ToLowerInvariant();
        var month = Array.IndexOf(MonthNames, monthName) + 1;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), moscowTimeZone);
        var year = dateMatch.Groups["year"].Success
            ? int.Parse(dateMatch.Groups["year"].Value, CultureInfo.InvariantCulture)
            : nowLocal.Year;
        var hour = int.Parse(timeMatch.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(timeMatch.Groups["minute"].Value, CultureInfo.InvariantCulture);

        try
        {
            localDateTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            if (!dateMatch.Groups["year"].Success && localDateTime.Date < nowLocal.Date.AddMonths(-6))
            {
                localDateTime = localDateTime.AddYears(1);
            }

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            localDateTime = default;
            return false;
        }
    }

    private static string? TryParsePlace(string compactEventText)
    {
        var quoted = QuotedVenueRegex.Matches(compactEventText);
        if (quoted.Count > 0)
        {
            return NormalizePlace(quoted[quoted.Count - 1].Groups["place"].Value);
        }

        var unquoted = UnquotedVenueRegex.Matches(compactEventText);
        return unquoted.Count > 0
            ? NormalizePlace(unquoted[unquoted.Count - 1].Groups["place"].Value)
            : null;
    }
}
