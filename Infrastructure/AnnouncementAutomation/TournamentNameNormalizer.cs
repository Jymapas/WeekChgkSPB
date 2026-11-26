using System.Globalization;
using System.Text.RegularExpressions;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class TournamentNameNormalizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex B52Regex = new(
        @"(?:Серия\s+)?B-52\s*:\s*сезон\s*(?<season>\d{1,2})\s*,?\s*эпизод\s*(?<episode>\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SyncSuffixRegex = new(
        @"\s*\(\s*синхрон\s*\)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex YearRegex = new(
        @"(?:\s*[—–-]\s*)?\b20\d{2}\s*$",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex NumberSignRegex = new(
        @"\s*№\s*(?<number>\d+)",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TrailingNumberRegex = new(
        @"\s*[—–-]\s*(?<number>\d+(?:[,.]\d+)?)\s*$",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex RomanSuffixRegex = new(
        @"(?<prefix>\b(?:том|Masters|Challenger)\s*)\b(?<roman>[IVXLCDM]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex RussianPartSuffixRegex = new(
        @"(?:\s*:\s*часть\s+(?<part>первая|вторая|третья|четвёртая|четвертая|пятая|шестая|седьмая|восьмая|девятая|десятая)|\s*:\s*(?<stage>первый|второй|третий|четв[её]рт(?:ый|ой)|пятый|шестой|седьмой|восьмой|девятый|десятый)\s+этап)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public string Normalize(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var value = rawName.Trim().Trim('"', '\'', '«', '»');
        value = Regex.Replace(
            value,
            @"^\s*(?:Международный|Строго\s+синхронный)\s+турнир\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

        value = value.Trim('"', '«', '»').TrimEnd('.');
        value = Regex.Replace(value, @"\s*\(\s*Финал ЛВ\s*\)", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        value = Regex.Replace(value, @"^Sims To Be\s*:\s*Vol\.\s*(\d+)\s*$", "Sims To Be-$1",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        value = Regex.Replace(value,
            @"^(?<stage>Первый|Второй|Третий|Четв[её]ртый) этап отбора на (?:Чемпионат России|ЧР) среди студентов(?: по интеллектуальным играм)?$",
            match => "Отбор на СтудЧР-" + (match.Groups["stage"].Value.ToLowerInvariant() switch
            {
                "первый" => "1", "второй" => "2", "третий" => "3", _ => "4"
            }), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

        var b52 = B52Regex.Match(value);
        if (b52.Success)
        {
            var season = int.Parse(b52.Groups["season"].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(b52.Groups["episode"].Value, CultureInfo.InvariantCulture);
            return $"B-52: s{season:00}e{episode:00}";
        }

        value = SyncSuffixRegex.Replace(value, " ");
        value = Regex.Replace(value,
            @"^ОВСЧ\s*\.?\s*(?:Сезон\s+20\d{2}\s*/\s*20\d{2}\s*\.\s*)?(?:[—–-]\s*|№\s*)?(?<stage>\d+)\s*(?:этап)?\s*$",
            "ОВСЧ-${stage}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        value = Regex.Replace(value, @"\s*\(20\d{2}\)\s*$", string.Empty, RegexOptions.CultureInvariant, RegexTimeout);
        value = Regex.Replace(value, @"^Плюс\s+(\d+)$", "Плюс-$1", RegexOptions.IgnoreCase, RegexTimeout);
        value = Regex.Replace(value, @"^(Бесконечные Земли):\s*том\s+([IVXLCDM]+)\s*$",
            match => $"{match.Groups[1].Value}-{TryParseRoman(match.Groups[2].Value)}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        value = YearRegex.Replace(value, string.Empty);
        value = NumberSignRegex.Replace(value, match => $"-{match.Groups["number"].Value}");
        value = RomanSuffixRegex.Replace(value, ReplaceRomanSuffix);
        value = RussianPartSuffixRegex.Replace(value, ReplaceRussianPartSuffix);
        value = TrailingNumberRegex.Replace(value, match => $"-{match.Groups["number"].Value}");
        value = WhitespaceRegex.Replace(value, " ");
        return value.Trim().Trim('—', '–', '-', ':', ' ');
    }

    private static string ReplaceRomanSuffix(Match match)
    {
        var number = TryParseRoman(match.Groups["roman"].Value);
        if (number <= 0)
        {
            return match.Value;
        }

        return $"{match.Groups["prefix"].Value.Trim()}-{number}";
    }

    private static string ReplaceRussianPartSuffix(Match match)
    {
        var part = match.Groups["part"].Success
            ? match.Groups["part"].Value.ToLowerInvariant()
            : match.Groups["stage"].Value.ToLowerInvariant();
        var number = part switch
        {
            "первая" or "первый" => 1,
            "вторая" or "второй" => 2,
            "третья" or "третий" => 3,
            "четвёртая" or "четвертая" or "четвёртый" or "четвертый" or "четвёртой" or "четвертой" => 4,
            "пятая" or "пятый" => 5,
            "шестая" or "шестой" => 6,
            "седьмая" or "седьмой" => 7,
            "восьмая" or "восьмой" => 8,
            "девятая" or "девятый" => 9,
            "десятая" or "десятый" => 10,
            _ => 0
        };
        return number == 0 ? match.Value : $"-{number}";
    }

    private static int TryParseRoman(string value)
    {
        var total = 0;
        var previous = 0;
        foreach (var character in value.ToUpperInvariant().Reverse())
        {
            var current = character switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0
            };
            if (current == 0)
            {
                return 0;
            }

            total += current < previous ? -current : current;
            previous = current;
        }

        return total;
    }
}
