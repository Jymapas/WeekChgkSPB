using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditCostCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditCostCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditCost, AddStep.EditWaitingCost, Messages.Edit.CostUsage, channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return Messages.Edit.CostPrompt(existing.Id, existing.Cost);
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (!int.TryParse(inlineValue, out var cost))
        {
            return (false, Messages.InvalidNumber);
        }

        existing.Cost = cost;
        return (true, Messages.Edit.CostUpdated);
    }
}
