using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class PendingEditFlow : IConversationFlowHandler
{
    private readonly ModerationHandler _moderationHandler;

    public PendingEditFlow(ModerationHandler moderationHandler)
    {
        _moderationHandler = moderationHandler;
    }

    public bool CanHandle(AddStep step)
    {
        return step is AddStep.PendingEditWaitingName
            or AddStep.PendingEditWaitingPlace
            or AddStep.PendingEditWaitingDateTime
            or AddStep.PendingEditWaitingCost;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        return state.Step switch
        {
            AddStep.PendingEditWaitingName => await HandleEdit(context, state, pending =>
            {
                if (string.IsNullOrWhiteSpace(context.Message.Text))
                {
                    return (false, Messages.NameRequired);
                }

                pending.TournamentName = context.Message.Text.Trim();
                return (true, Messages.Edit.NameUpdated);
            }),
            AddStep.PendingEditWaitingPlace => await HandleEdit(context, state, pending =>
            {
                pending.Place = context.Message.Text?.Trim() ?? string.Empty;
                return (true, Messages.Edit.PlaceUpdated);
            }),
            AddStep.PendingEditWaitingDateTime => await HandleEdit(context, state, pending =>
            {
                if (!context.Helper.TryParseDateTime(context.Message.Text, out var parsedUtc))
                {
                    return (false, Messages.Edit.InvalidDateTime);
                }

                pending.DateTimeUtc = parsedUtc;
                return (true, Messages.Edit.DateTimeUpdated);
            }),
            AddStep.PendingEditWaitingCost => await HandleEdit(context, state, pending =>
            {
                var input = context.Message.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input))
                {
                    return (false, Messages.Add.PromptCost);
                }

                var (parsedCost, costLabel) = BotCommandHelper.ParseCost(input);
                pending.Cost = parsedCost;
                pending.CostLabel = costLabel;
                return (true, Messages.Edit.CostUpdated);
            }),
            _ => false
        };
    }

    private async Task<bool> HandleEdit(
        BotCommandContext context,
        AddAnnouncementState state,
        System.Func<PendingAnnouncement, (bool Success, string Message)> mutator)
    {
        var userManagement = context.UserManagement!;
        var pending = state.PendingEditId.HasValue ? userManagement.GetPending(state.PendingEditId.Value) : null;
        if (pending is null)
        {
            state.Step = AddStep.None;
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Moderation.RequestNotFound, cancellationToken: context.CancellationToken);
            context.StateStore.Remove(context.Message.From!.Id);
            return true;
        }

        var (success, response) = mutator(pending);
        if (!success)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, response, cancellationToken: context.CancellationToken);
            return true;
        }

        userManagement.UpdatePending(pending);
        await context.Bot.SendMessage(context.Message.Chat.Id, response, cancellationToken: context.CancellationToken);

        if (state.PendingEditChatId.HasValue && state.PendingEditMessageId.HasValue)
        {
            await _moderationHandler.RefreshModerationMessage(
                pending,
                state.PendingEditChatId.Value,
                state.PendingEditMessageId.Value,
                context.CancellationToken);
        }

        state.Step = AddStep.Done;
        state.PendingEditId = null;
        state.PendingEditChatId = null;
        state.PendingEditMessageId = null;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }
}
