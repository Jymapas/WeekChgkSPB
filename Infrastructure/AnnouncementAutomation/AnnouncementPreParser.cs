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
        "скид", "вторая игра", "вторую игру"
    ];
    private static readonly string[] ExcludedTariffMarkers =
    [
        "студент", "пар", "соло", "трио", "депозит"
    ];
    private static readonly string[] VenueChangeMarkers =
    [
        "перенос площадки",
        "замена площадки",
        "смена площадки"
    ];
    private static readonly string[] IgnoredPostMarkers =
    [
        "продолжается регистрация"
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
    private static readonly Regex TeamCapacityRegex = new(
        @"(?:до|из)?\s*(?<capacity>[1-6])(?:\s*[-–—]?\s*(?:ти|х))?\s*(?:чел(?:овек(?:а|у|ом)?|овек)?|игрок(?:а|ов)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex QuotedVenueRegex = new(
        "(?:\\bв|(?=ресто-клуб)|на\\s+площадке\\s*:?|по\\s+адресу\\s*:)\\s*(?:ресто-клубе|ресто-клуб|кафе|клубе|клуб|ресторане|ресторан|баре|бар)?\\s*[\"«](?<place>[^\"»\\r\\n]{1,80})[\"»]",
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
            var isVenueChange = VenueChangeMarkers.Any(marker =>
                title.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                body.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (IgnoredPostMarkers.Any(marker =>
                    title.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                    body.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return AnnouncementPreParseResult.Failed(
                    "post_update_ignored",
                    sourceLength,
                    title);
            }

            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var eventLine = FindEventLine(lines) ??
                            (DateRegex.IsMatch(title) && TimeRegex.IsMatch(title) ? title : null);
            if (string.IsNullOrWhiteSpace(eventLine))
            {
                return AnnouncementPreParseResult.Failed("event_not_found", sourceLength);
            }

            var compactEventText = string.Equals(title, eventLine, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title}\n{eventLine}";
            if (TryParsePlace(compactEventText) is null)
            {
                // Добавляем только фрагмент с площадкой, без соседних цен и контактов.
                var venue = QuotedVenueRegex.Match(body);
                if (venue.Success) compactEventText += "\n" + venue.Value;
            }

            var price = ParseCost(lines);
            var hasLocalDateTime = TryParseLocalDateTime(title, eventLine, nowUtc, out var localDateTime);
            var place = TryParsePlace(compactEventText);
            var gameTimes = lines.Select(FindGameTime).Where(match => match.Success)
                .Select(TimeOfDay).Distinct().ToList();
            var titleTime = TimeRegex.Match(title);
            var timeConflict = gameTimes.Count > 1 ||
                (titleTime.Success && gameTimes.Count == 1 &&
                 TimeOfDay(titleTime) != gameTimes[0]);

            if (!price.Success)
            {
                return AnnouncementPreParseResult.Failed(
                    price.FailureCode!,
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence,
                    hasLocalDateTime && !timeConflict ? localDateTime : null,
                    place,
                    isVenueChange);
            }

            if (timeConflict)
            {
                return AnnouncementPreParseResult.Failed(
                    "datetime_conflict", sourceLength, compactEventText, price.Cost,
                    price.Evidence, place: place, isVenueChange: isVenueChange);
            }

            if (!hasLocalDateTime)
            {
                return AnnouncementPreParseResult.Failed(
                    "datetime_not_found",
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence,
                    place: place,
                    isVenueChange: isVenueChange);
            }

            if (string.IsNullOrWhiteSpace(place))
            {
                return AnnouncementPreParseResult.Failed(
                    "place_not_found",
                    sourceLength,
                    compactEventText,
                    price.Cost,
                    price.Evidence,
                    localDateTime,
                    isVenueChange: isVenueChange);
            }

            return new AnnouncementPreParseResult(
                true,
                null,
                compactEventText,
                price.Cost,
                price.Evidence,
                localDateTime,
                place,
                sourceLength,
                isVenueChange);
        }
        catch (RegexMatchTimeoutException)
        {
            return AnnouncementPreParseResult.Failed("regex_timeout", sourceLength);
        }
    }

    public static bool IsVenueChange(Post post)
    {
        if ((post.Title?.Length ?? 0) + (post.Description?.Length ?? 0) > MaxSourceLength)
        {
            return false;
        }

        try
        {
            var title = CleanInline(post.Title);
            var body = CleanBody(post.Description);
            return VenueChangeMarkers.Any(marker =>
                title.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                body.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
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
        var gameTime = lines.FirstOrDefault(line => FindGameTime(line).Success);
        return eventDescription is not null && gameTime is not null
            ? $"{eventDescription} {gameTime}"
            : null;
    }

    internal static Match FindGameTime(string line)
    {
        var marker = Regex.Match(line, @"начало турнира|первый вопрос|первый тур",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        if (!marker.Success)
        {
            var schedule = Regex.IsMatch(line,
                @"^\s*\d{1,2}[:.-]\d{2}\s*[-–—]\s*(?:\d{1,2}[:.-]\d{2}\s*[-–—]?\s*)?КВРМ\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            return schedule ? TimeRegex.Match(line) : Match.Empty;
        }
        // В тексте время следует за маркером, в расписании интервал стоит перед ним.
        var after = TimeRegex.Match(line, marker.Index + marker.Length);
        return after.Success ? after : TimeRegex.Match(line);
    }

    private static TimeSpan TimeOfDay(Match match) => new(
        int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture), 0);

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

        var candidates = new List<(int Cost, string Evidence, int? Capacity)>();
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > start && SectionEndMarkers.Any(marker => line.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            // Обычное участие и зачёт отбора — конкурирующие основные тарифы.
            if (Regex.IsMatch(line, @"(?:зач[её]т|выводящий).*(?<!\d)\d{3,5}(?!\d)|\d{3,5}.*(?:зач[её]т|обычное участие)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout) &&
                Regex.Matches(line, @"(?<!\d)\d{3,5}(?!\d)", RegexOptions.CultureInvariant, RegexTimeout).Count > 1)
            {
                return (false, "cost_ambiguous", null, line);
            }

            var money = MoneyRegex.Match(line);
            if (!money.Success ||
                !TryGetTeamCapacity(line, money.Index, i == start, out var capacity))
            {
                continue;
            }

            var amountText = money.Groups["amount"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0)
            {
                candidates.Add((amount, line, capacity));
            }
        }

        var maximumCapacity = candidates
            .Where(candidate => candidate.Capacity.HasValue)
            .Select(candidate => candidate.Capacity!.Value)
            .DefaultIfEmpty()
            .Max();
        var eligible = maximumCapacity > 0
            ? candidates.Where(candidate =>
                !candidate.Capacity.HasValue || candidate.Capacity == maximumCapacity)
            : candidates;
        var distinct = eligible.DistinctBy(candidate => candidate.Cost).ToList();
        return distinct.Count switch
        {
            0 => (false, "cost_not_found", null, null),
            1 => (true, null, distinct[0].Cost, distinct[0].Evidence),
            _ => (false, "cost_ambiguous", null, string.Join(" | ", distinct.Select(candidate => candidate.Evidence)))
        };
    }

    private static bool TryGetTeamCapacity(
        string line,
        int moneyIndex,
        bool isHeadingLine,
        out int? capacity)
    {
        var lower = line.ToLowerInvariant();
        var hasTeam = lower.Contains("команд", StringComparison.Ordinal);
        var prefix = lower[..Math.Min(moneyIndex, lower.Length)];
        if (DiscountMarkers.Any(marker => prefix.Contains(marker, StringComparison.Ordinal)))
        {
            capacity = null;
            return false;
        }

        var capacityMatch = TeamCapacityRegex.Match(lower);
        var tariffDescriptorEnd = capacityMatch.Success
            ? capacityMatch.Index + capacityMatch.Length
            : moneyIndex;
        if (ExcludedTariffMarkers.Any(marker =>
                lower[..Math.Min(tariffDescriptorEnd, lower.Length)].Contains(marker, StringComparison.Ordinal)))
        {
            capacity = null;
            return false;
        }

        if (capacityMatch.Success &&
            int.TryParse(capacityMatch.Groups["capacity"].Value, out var parsedCapacity))
        {
            capacity = parsedCapacity;
            return parsedCapacity >= 4 && (hasTeam || isHeadingLine || moneyIndex < capacityMatch.Index);
        }

        capacity = null;
        return hasTeam ||
               (isHeadingLine &&
                line.StartsWith("взнос", StringComparison.OrdinalIgnoreCase) &&
                !DiscountMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal)));
    }

    private bool TryParseLocalDateTime(string title, string eventLine, DateTime nowUtc, out DateTime localDateTime)
    {
        var combined = $"{title}\n{eventLine}";
        var dateMatch = DateRegex.Match(combined);
        var timeMatch = TimeRegex.Match(title);
        if (!timeMatch.Success)
        {
            timeMatch = FindGameTime(eventLine);
            if (!timeMatch.Success) timeMatch = TimeRegex.Match(eventLine);
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
