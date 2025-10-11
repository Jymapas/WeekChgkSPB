using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace WeekChgkSPB.Infrastructure.Notifications;

internal sealed class ChannelPostUpdater : IChannelPostUpdater
{
    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly ChannelPostsRepository _history;
    private readonly ITelegramBotClient _bot;
    private readonly string _channelId;

    public ChannelPostUpdater(
        AnnouncementsRepository announcements,
        FootersRepository footers,
        ChannelPostsRepository history,
        ITelegramBotClient bot,
        string channelId)
    {
        _announcements = announcements;
        _footers = footers;
        _history = history;
        _bot = bot;
        _channelId = channelId;
    }

    public async Task UpdateLastPostAsync(CancellationToken ct)
    {
        var entry = _history.GetLatest();
        if (entry is null || entry.MessageId is null)
        {
            return;
        }

        var fromUtc = ResolveFromScheduledUtc(entry.ScheduledAtUtc);
        var rows = _announcements.GetWithLinksInRange(fromUtc);
        if (rows.Count == 0)
        {
            return;
        }

        var footerLines = _footers.GetAllTextsDesc();
        var updatedAtLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow);
        var text = PostFormatter.BuildScheduleMessage(rows, footerLines.Count > 0 ? footerLines : null, updatedAtLocal);

        try
        {
            await _bot.EditMessageText(
                chatId: _channelId,
                messageId: entry.MessageId.Value,
                text: text,
                parseMode: ParseMode.Html,
                linkPreviewOptions: new Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Channel post update failed: {ex.Message}");
        }
    }

    private static DateTime ResolveFromScheduledUtc(DateTime scheduledUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(scheduledUtc, PostFormatter.Moscow);
        var startLocal = local.Date;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), PostFormatter.Moscow);
    }
}
