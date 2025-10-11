using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Configuration;

internal sealed record AppSettings(
    string DbPath,
    string BotToken,
    long ChatId,
    string? ChannelId,
    ChannelPostScheduleOptions? ScheduleOptions)
{
    public bool HasChannel => !string.IsNullOrEmpty(ChannelId);
    public bool HasScheduler => HasChannel && ScheduleOptions is not null;
}
