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
        @"(?:\s*[—–-]\s*)?\b20\d{2}\b",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex NumberSignRegex = new(
        @"\s*№\s*(?<number>\d+)",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TrailingNumberRegex = new(
        @"\s*[—–-]\s*(?<number>\d+)\s*$",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex RomanSuffixRegex = new(
        @"(?<prefix>\b(?:том|Masters|Challenger)\s*)\b(?<roman>[IVXLCDM]+)\b",
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
            @"^\s*Международный\s+турнир\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

        var b52 = B52Regex.Match(value);
        if (b52.Success)
        {
            var season = int.Parse(b52.Groups["season"].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(b52.Groups["episode"].Value, CultureInfo.InvariantCulture);
            return $"B-52: s{season:00}e{episode:00}";
        }

        value = SyncSuffixRegex.Replace(value, " ");
        value = YearRegex.Replace(value, string.Empty);
        value = NumberSignRegex.Replace(value, match => $"-{match.Groups["number"].Value}");
        value = RomanSuffixRegex.Replace(value, ReplaceRomanSuffix);
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
