using System;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class EditAnnouncementFlow : IConversationFlowHandler
{
    private readonly IChannelPostUpdater _channelPostUpdater;

    public EditAnnouncementFlow(IChannelPostUpdater channelPostUpdater)
    {
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(AddStep step)
    {
        return step is AddStep.EditWaitingName
            or AddStep.EditWaitingPlace
            or AddStep.EditWaitingDateTime
            or AddStep.EditWaitingCost;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        return state.Step switch
        {
            AddStep.EditWaitingName => await HandleEdit(context, state, existing =>
            {
                if (string.IsNullOrWhiteSpace(context.Message.Text))
                {
                    return (false, Messages.NameRequired);
                }

                existing.TournamentName = context.Message.Text.Trim();
                return (true, Messages.Edit.NameUpdated);
            }),
            AddStep.EditWaitingPlace => await HandleEdit(context, state, existing =>
            {
                existing.Place = context.Message.Text?.Trim() ?? string.Empty;
                return (true, Messages.Edit.PlaceUpdated);
            }),
            AddStep.EditWaitingDateTime => await HandleEdit(context, state, existing =>
            {
                if (!context.Helper.TryParseDateTime(context.Message.Text, out var parsedUtc))
                {
                    return (false, Messages.Edit.InvalidDateTime);
                }

                existing.DateTimeUtc = parsedUtc;
                return (true, Messages.Edit.DateTimeUpdated);
            }),
            AddStep.EditWaitingCost => await HandleEdit(context, state, existing =>
            {
                var input = context.Message.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input))
                {
                    return (false, Messages.Add.PromptCost);
                }

                var (parsedCost, costLabel) = BotCommandHelper.ParseCost(input);
                existing.Cost = parsedCost;
                existing.CostLabel = costLabel;
                return (true, Messages.Edit.CostUpdated);
            }),
            _ => false
        };
    }

    private async Task<bool> HandleEdit(
        BotCommandContext context,
        AddAnnouncementState state,
        Func<Announcement, (bool Success, string Message)> mutator)
    {
        if (state.Existing is null)
        {
            state.Step = AddStep.None;
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Edit.NoActiveAnnouncement, cancellationToken: context.CancellationToken);
            context.StateStore.Remove(context.Message.From!.Id);
            return true;
        }

        var (success, response) = mutator(state.Existing);
        if (!success)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, response, cancellationToken: context.CancellationToken);
            return true;
        }

        context.Announcements.Update(state.Existing);
        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        await context.Bot.SendMessage(context.Message.Chat.Id, response, cancellationToken: context.CancellationToken);

        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        return true;
    }
}
