using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditPlaceCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditPlaceCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditPlace, AddStep.EditWaitingPlace, Messages.Edit.PlaceUsage, channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return Messages.Edit.PlacePrompt(existing.Id, existing.Place);
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        existing.Place = inlineValue?.Trim() ?? string.Empty;
        return (true, Messages.Edit.PlaceUpdated);
    }
}
