using System.Globalization;
using System.Text;

namespace WeekChgkSPB.Infrastructure.Notifications;

public static class PostFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");
    public static readonly TimeZoneInfo Moscow =
#if WINDOWS
        TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
#else
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
#endif

    public static string BuildScheduleMessage(IEnumerable<AnnouncementRow> rows, IEnumerable<string>? footerLines = null)
    {
        var byDate = rows
            .Select(r => (r, local: TimeZoneInfo.ConvertTimeFromUtc(r.DateTimeUtc, Moscow)))
            .GroupBy(x => x.local.Date)
            .OrderBy(g => g.Key);

        var sb = new StringBuilder();

        sb.AppendLine("Продолжаем вести список синхронов в Санкт-Петербурге.\n");

        foreach (var g in byDate)
        {
            var dayName = Abbrev2(Ru.DateTimeFormat.GetDayName(g.Key.DayOfWeek));
            sb.Append("<b>")
                .Append(g.Key.ToString("dd MMMM", Ru))
                .Append(" (")
                .Append(dayName)
                .Append(")</b>\n");

            foreach (var x in g)
            {
                var time = x.local.ToString("HH:mm", Ru);
                sb.Append($"""<a href="{x.r.Link}">{x.r.TournamentName} - {x.r.Place} ({time}) {x.r.Cost} р.</a>""")
                    .Append('\n');
            }
        }

        if (footerLines is not null)
        {
            sb.AppendLine();

            foreach (var line in footerLines)
            {
                sb.AppendLine(line.TrimEnd());
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildScheduleHtml(IEnumerable<AnnouncementRow> rows, IEnumerable<string>? footerLines = null)
    {
        return BuildScheduleMessage(rows, footerLines);
    }

    public static string WrapAsCodeForTelegram(string html, int tgLimit = 4096)
    {
        var escaped = html.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        var wrapped = "<code>" + escaped + "</code>";

        if (wrapped.Length <= tgLimit) return wrapped;

        var budget = tgLimit - "</code>".Length - 1;
        if (budget < 0) budget = 0;
        return "<code>" + escaped[..Math.Min(budget, escaped.Length)] + "…</code>";
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