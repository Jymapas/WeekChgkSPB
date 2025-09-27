using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditPlaceCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditPlaceCommandHandler()
        : base(BotCommands.EditPlace, AddStep.EditWaitingPlace, "/edit_place <id> [новое место]")
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return $"Редактирование анонса {existing.Id}.\nТекущее место: {existing.Place}\nОтправь новое место";
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        existing.Place = inlineValue?.Trim() ?? string.Empty;
        return (true, "Место обновлено");
    }
}
