using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class BotCommandHelper
{
    private readonly TimeZoneInfo _moscow;
    private const string LiveJournalBaseUrl = "https://chgk-spb.livejournal.com";

    private static readonly string[] MoscowDateTimeFormats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd"
    };

    private static readonly CultureInfo RuCulture = new("ru-RU");
    private static readonly Regex YearRegex = new("\\d{4}");

    public BotCommandHelper(TimeZoneInfo moscow)
    {
        _moscow = moscow;
    }

    public string AddLinesPrompt => Messages.Add.LinesPrompt;

    public (DateTime FromUtc, DateTime? ToUtc) ResolveDateRangeOrDefault(string commandText)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && TryParseDateTime(parts[1], out var fromUtc))
        {
            return (fromUtc, null);
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _moscow);
        var startLocal = nowLocal.Date;
        var fallbackFromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, _moscow);
        return (fallbackFromUtc, null);
    }

    public bool IsCommand(string? text, string command)
    {
        if (text is null)
        {
            return false;
        }

        if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == command.Length)
        {
            return true;
        }

        var next = text[command.Length];
        return char.IsWhiteSpace(next) || next == '@';
    }

    public bool TryParseDateTime(string? input, out DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            utc = default;
            return false;
        }

        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 2 && TryParseHumanDateTime(parts[0], parts[1], out utc))
        {
            return true;
        }

        if (parts.Length == 1)
        {
            var single = parts[0];

            if (TryParseSingleLineHumanDateTime(single, out utc))
            {
                return true;
            }

            if (DateTimeOffset.TryParse(single, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dtoSingle))
            {
                utc = dtoSingle.UtcDateTime;
                return true;
            }

            if (TryParseLocal(single, MoscowDateTimeFormats, CultureInfo.InvariantCulture, out utc))
            {
                return true;
            }

            if (TryParseLocal(single, RuCulture, DateTimeStyles.AllowWhiteSpaces, out utc))
            {
                return true;
            }
        }

        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }

        if (TryParseLocal(normalized, MoscowDateTimeFormats, CultureInfo.InvariantCulture, out utc))
        {
            return true;
        }

        if (TryParseLocal(normalized, RuCulture, DateTimeStyles.AllowWhiteSpaces, out utc))
        {
            return true;
        }

        utc = default;
        return false;
    }

    public bool TryBuildAnnouncementFromLines(string content, out Announcement announcement, out string link, out string error)
    {
        announcement = default!;
        link = string.Empty;

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        var lines = rawLines.Select(static line => line.Trim()).ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count < 5)
        {
            error = Messages.Add.TooFewLines;
            return false;
        }

        if (lines.Count > 6)
        {
            error = Messages.Add.TooManyLines;
            return false;
        }

        link = NormalizePostLink(lines[0]);
        if (string.IsNullOrWhiteSpace(link))
        {
            error = Messages.Add.FirstLineMustBeLink;
            return false;
        }

        var name = lines[1];
        if (string.IsNullOrWhiteSpace(name))
        {
            error = Messages.Add.SecondLineMustBeName;
            return false;
        }

        var place = lines[2];

        string dateTimeInput;
        string costLine;

        if (lines.Count == 6)
        {
            dateTimeInput = lines[3] + "\n" + lines[4];
            costLine = lines[5];
        }
        else
        {
            dateTimeInput = lines[3];
            costLine = lines[4];
        }

        if (!TryParseDateTime(dateTimeInput, out var dt))
        {
            error = Messages.Add.DateTimeNotRecognized;
            return false;
        }

        if (string.IsNullOrWhiteSpace(costLine))
        {
            error = Messages.Add.InvalidCost;
            return false;
        }

        var (cost, costLabel) = ParseCost(costLine);

        announcement = new Announcement
        {
            TournamentName = name,
            Place = place,
            DateTimeUtc = dt,
            Cost = cost,
            CostLabel = costLabel
        };

        error = string.Empty;
        return true;
    }

    public IReadOnlyList<string> SplitIntoBlocks(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var blocks = new List<string>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join('\n', current));
                    current.Clear();
                }
            }
            else
            {
                current.Add(line);
            }
        }

        if (current.Count > 0)
        {
            blocks.Add(string.Join('\n', current));
        }

        return blocks;
    }

    public static (int Cost, string? CostLabel) ParseCost(string input)
    {
        var trimmed = input.Trim();
        if (int.TryParse(trimmed, out var cost))
            return (cost, null);
        return (0, trimmed);
    }

    public void ResetDraft(AddAnnouncementState state)
    {
        state.Draft.Id = 0;
        state.Draft.TournamentName = string.Empty;
        state.Draft.Place = string.Empty;
        state.Draft.DateTimeUtc = DateTime.MinValue;
        state.Draft.Cost = 0;
        state.Draft.CostLabel = null;
        state.DraftLink = string.Empty;
    }

    public string NormalizePostLink(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();
        if (long.TryParse(trimmed, out var id) && id > 0)
        {
            return $"{LiveJournalBaseUrl}/{id}.html";
        }

        return trimmed;
    }

    public string EscapeForCode(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private bool TryParseHumanDateTime(string datePart, string timePart, out DateTime utc)
    {
        if (!TryBuildLocalFromHuman(datePart, timePart, out var local))
        {
            utc = default;
            return false;
        }

        return TryConvertLocalToUtc(local, out utc);
    }

    private bool TryParseSingleLineHumanDateTime(string single, out DateTime utc)
    {
        utc = default;

        var pieces = single.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2)
        {
            return false;
        }

        var datePart = string.Join(' ', pieces[..^1]);
        var timePart = pieces[^1];

        if (!TryBuildLocalFromHuman(datePart, timePart, out var local))
        {
            return false;
        }

        return TryConvertLocalToUtc(local, out utc);
    }

    private bool TryBuildLocalFromHuman(string datePart, string timePart, out DateTime local)
    {
        local = default;

        if (!TimeSpan.TryParse(timePart, CultureInfo.InvariantCulture, out var timeOfDay))
        {
            return false;
        }

        if (!DateTime.TryParse(datePart, RuCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
        {
            return false;
        }

        local = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day,
            timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds);

        if (!ContainsYear(datePart))
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _moscow);
            if (local < nowLocal)
            {
                local = local.AddYears(1);
            }
        }

        return true;
    }

    private static bool ContainsYear(string datePart)
    {
        return YearRegex.IsMatch(datePart);
    }

    private bool TryParseLocal(
        string input,
        string[] formats,
        IFormatProvider provider,
        out DateTime utc)
    {
        if (DateTime.TryParseExact(input, formats, provider, DateTimeStyles.None, out var parsed))
        {
            return TryConvertLocalToUtc(parsed, out utc);
        }

        utc = default;
        return false;
    }

    private bool TryParseLocal(
        string input,
        IFormatProvider provider,
        DateTimeStyles styles,
        out DateTime utc)
    {
        if (DateTime.TryParse(input, provider, styles, out var parsed))
        {
            return TryConvertLocalToUtc(parsed, out utc);
        }

        utc = default;
        return false;
    }

    private bool TryConvertLocalToUtc(DateTime local, out DateTime utc)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, _moscow);
        return true;
    }
}
