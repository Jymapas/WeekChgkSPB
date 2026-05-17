using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditNameCommandHandler : EditAnnouncementCommandHandlerBase
{
    public EditNameCommandHandler(IChannelPostUpdater channelPostUpdater)
        : base(BotCommands.EditName, AddStep.EditWaitingName, Messages.Edit.NameUsage, channelPostUpdater)
    {
    }

    protected override string BuildPrompt(Announcement existing, BotCommandHelper helper)
    {
        return Messages.Edit.NamePrompt(existing.Id, existing.TournamentName);
    }

    protected override (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper)
    {
        if (string.IsNullOrWhiteSpace(inlineValue))
        {
            return (false, Messages.NameRequired);
        }

        existing.TournamentName = inlineValue;
        return (true, Messages.Edit.NameUpdated);
    }
}
