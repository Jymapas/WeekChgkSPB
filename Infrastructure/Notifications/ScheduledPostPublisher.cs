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
    private readonly PostsRepository _posts;
    private readonly ITelegramBotClient _bot;
    private readonly string _channelId;
    private readonly ChannelPostScheduleOptions _options;
    private readonly TimeZoneInfo _scheduleZone;
    private readonly int _announcementRetentionDays;

    public ScheduledPostPublisher(
        AnnouncementsRepository announcements,
        FootersRepository footers,
        ChannelPostsRepository history,
        PostsRepository posts,
        ITelegramBotClient bot,
        string channelId,
        ChannelPostScheduleOptions options,
        TimeZoneInfo scheduleZone,
        int announcementRetentionDays)
    {
        _announcements = announcements;
        _footers = footers;
        _history = history;
        _posts = posts;
        _bot = bot;
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        _options = options;
        _scheduleZone = scheduleZone;
        if (announcementRetentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(announcementRetentionDays), "Retention must be positive.");
        }
        _announcementRetentionDays = announcementRetentionDays;
    }

    public async Task TryPublishAsync(DateTime utcNow, CancellationToken ct)
    {
        var dueSlots = GetDueSlotsUtc(utcNow);
        if (dueSlots.Count == 0)
        {
            return;
        }

        var (fromUtc, toUtc) = ResolveRangeUtc(utcNow);
        var rows = toUtc is null
            ? _announcements.GetWithLinksInRange(fromUtc)
            : _announcements.GetWithLinksInRange(fromUtc, toUtc.Value);
        if (rows.Count == 0)
        {
            return;
        }

        var footerLines = _footers.GetAllTextsDesc();
        var text = PostFormatter.BuildScheduleMessage(rows, footerLines.Count > 0 ? footerLines : null);

        var postedAny = false;
        foreach (var slotUtc in dueSlots)
        {
            if (_history.HasPosted(slotUtc))
            {
                continue;
            }

            var sentMessage = await _bot.SendMessage(
                _channelId,
                text,
                ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            _history.MarkPosted(slotUtc, DateTime.UtcNow, sentMessage.MessageId);
            postedAny = true;
        }

        if (postedAny)
        {
            CleanupStaleData(utcNow);
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

    private void CleanupStaleData(DateTime utcNow)
    {
        var thresholdUtc = utcNow.AddDays(-_announcementRetentionDays);
        _announcements.DeleteOlderThan(thresholdUtc);
    }

    private (DateTime FromUtc, DateTime? ToUtc) ResolveRangeUtc(DateTime utcNow)
    {
        var nowMoscow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, PostFormatter.Moscow);
        var startLocal = nowMoscow.Date;
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), PostFormatter.Moscow);
        return (fromUtc, null);
    }
}
