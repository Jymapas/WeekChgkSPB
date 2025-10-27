using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditNameCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditNameCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditName, AddStep.EditWaitingName, "/edit_name <id> [новое название]", channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return $"Редактирование анонса {existing.Id}.\nТекущее название: {existing.TournamentName}\nОтправь новое название";
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (string.IsNullOrWhiteSpace(inlineValue))
        {
            return (false, "Название не может быть пустым");
        }

        existing.TournamentName = inlineValue;
        return (true, "Название обновлено");
    }
}
