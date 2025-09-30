using System;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WeekChgkSPB.Infrastructure.Notifications;

public class ScheduledPostPublisher
{
    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly ChannelPostsRepository _history;
    private readonly ITelegramBotClient _bot;
    private readonly string _channelId;
    private readonly ChannelPostScheduleOptions _options;
    private readonly TimeZoneInfo _scheduleZone;

    public ScheduledPostPublisher(
        AnnouncementsRepository announcements,
        FootersRepository footers,
        ChannelPostsRepository history,
        ITelegramBotClient bot,
        string channelId,
        ChannelPostScheduleOptions options,
        TimeZoneInfo scheduleZone)
    {
        _announcements = announcements;
        _footers = footers;
        _history = history;
        _bot = bot;
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        _options = options;
        _scheduleZone = scheduleZone;
    }

    public async Task TryPublishAsync(DateTime utcNow, CancellationToken ct)
    {
        var dueSlots = GetDueSlotsUtc(utcNow);
        if (dueSlots.Count == 0)
        {
            return;
        }

        var (fromUtc, toUtc) = ResolveRangeUtc(utcNow);
        var rows = _announcements.GetWithLinksInRange(fromUtc, toUtc);
        if (rows.Count == 0)
        {
            return;
        }

        var footerLines = _footers.GetAllTextsDesc();
        var text = PostFormatter.BuildScheduleMessage(rows, footerLines.Count > 0 ? footerLines : null);

        foreach (var slotUtc in dueSlots)
        {
            if (_history.HasPosted(slotUtc))
            {
                continue;
            }

            await _bot.SendMessage(
                _channelId,
                text,
                ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            _history.MarkPosted(slotUtc, DateTime.UtcNow);
        }
    }

    private List<DateTime> GetDueSlotsUtc(DateTime utcNow)
    {
        var due = new List<DateTime>();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _scheduleZone);
        var weekStart = nowLocal.Date.AddDays(-(int)nowLocal.DayOfWeek);

        foreach (var day in _options.Days)
        {
            var scheduledLocal = weekStart.AddDays((int)day).Add(_options.TimeOfDay);
            if (scheduledLocal > nowLocal)
            {
                continue;
            }

            if (nowLocal - scheduledLocal > _options.TriggerWindow)
            {
                continue;
            }

            var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(scheduledLocal, DateTimeKind.Unspecified), _scheduleZone);
            due.Add(scheduledUtc);
        }

        due.Sort();
        return due;
    }

    private (DateTime FromUtc, DateTime ToUtc) ResolveRangeUtc(DateTime utcNow)
    {
        var nowMoscow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, PostFormatter.Moscow);
        var startLocal = nowMoscow.Date;
        var endLocal = startLocal.AddDays(_options.LookaheadDays).AddHours(23).AddMinutes(59);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), PostFormatter.Moscow);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), PostFormatter.Moscow);
        return (fromUtc, toUtc);
    }
}
