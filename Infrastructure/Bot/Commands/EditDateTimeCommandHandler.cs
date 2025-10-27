using System;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditDateTimeCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditDateTimeCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditDateTime, AddStep.EditWaitingDateTime, "/edit_datetime <id> [новая дата и время]", channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(existing.DateTimeUtc, PostFormatter.Moscow);
        return $"Редактирование анонса {existing.Id}.\nТекущая дата и время (Москва): {local:yyyy-MM-dd HH:mm}\nОтправь новую дату и время по Москве";
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (!helper.TryParseDateTime(inlineValue, out var parsedUtc))
        {
            return (false, "Неверный формат. Пример: 2025-08-10T19:30 (Москва)");
        }

        existing.DateTimeUtc = parsedUtc;
        return (true, "Дата и время обновлены");
    }
}
