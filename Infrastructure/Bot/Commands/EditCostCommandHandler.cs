using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditCostCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditCostCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditCost, AddStep.EditWaitingCost, "/edit_cost <id> [новая стоимость]", channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return $"Редактирование анонса {existing.Id}.\nТекущая стоимость: {existing.Cost}\nОтправь новую стоимость (целое число)";
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (!int.TryParse(inlineValue, out var cost))
        {
            return (false, "Нужно целое число");
        }

        existing.Cost = cost;
        return (true, "Стоимость обновлена");
    }
}
