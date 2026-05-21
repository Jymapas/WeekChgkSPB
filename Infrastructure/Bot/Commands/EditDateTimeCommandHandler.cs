using System;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditDateTimeCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditDateTimeCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditDateTime, AddStep.EditWaitingDateTime, Messages.Edit.DateTimeUsage, channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(existing.DateTimeUtc, PostFormatter.Moscow);
        return Messages.Edit.DateTimePrompt(existing.Id, local.ToString("yyyy-MM-dd HH:mm"));
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (!helper.TryParseDateTime(inlineValue, out var parsedUtc))
        {
            return (false, Messages.Edit.InvalidDateTimeShort);
        }

        existing.DateTimeUtc = parsedUtc;
        return (true, Messages.Edit.DateTimeUpdated);
    }
}
