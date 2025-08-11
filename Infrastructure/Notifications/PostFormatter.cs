using System.Globalization;
using System.Text;

namespace WeekChgkSPB.Infrastructure.Notifications;

public static class PostFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");

    internal static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById(
#if WINDOWS
        "Russian Standard Time"
#else
        "Europe/Moscow"
#endif
    );

    public static IEnumerable<string> BuildScheduleMessages(IEnumerable<AnnouncementRow> rows)
    {
        var byDate = rows
            .Select(r => (r, local: TimeZoneInfo.ConvertTimeFromUtc(r.DateTimeUtc, Moscow)))
            .GroupBy(x => x.local.Date)
            .OrderBy(g => g.Key);

        const int limit = 4096;
        foreach (var g in byDate)
        {
            var sb = new StringBuilder();

            var dayName = Abbrev2(Ru.DateTimeFormat.GetDayName(g.Key.DayOfWeek));
            sb.Append("<b>")
                .Append(g.Key.ToString("dd MMMM", Ru))
                .Append(" (")
                .Append(dayName)
                .Append(")</b>\n");

            foreach (var x in g)
            {
                var time = x.local.ToString("HH:mm", Ru);
                var line = $"""<a href="{x.r.Link}">{x.r.TournamentName} - {x.r.Place} ({time}) {x.r.Cost} р.</a>""";

                if (sb.Length + line.Length + 1 > limit)
                {
                    yield return sb.ToString().TrimEnd();
                    sb.Clear();
                    sb.Append("<b>")
                        .Append(g.Key.ToString("dd MMMM", Ru))
                        .Append(" (")
                        .Append(dayName)
                        .Append(")</b>\n");
                }

                sb.Append(line).Append('\n');
            }

            yield return sb.ToString().TrimEnd();
        }
    }

    private static string Abbrev2(string full)
    {
        if (string.IsNullOrWhiteSpace(full)) return "";
        full = full.Trim().ToLowerInvariant();
        return full switch
        {
            "понедельник" => "пн",
            "вторник" => "вт",
            "среда" => "ср",
            "четверг" => "чт",
            "пятница" => "пт",
            "суббота" => "сб",
            "воскресенье" => "вс",
            _ => full[..Math.Min(2, full.Length)]
        };
    }
}