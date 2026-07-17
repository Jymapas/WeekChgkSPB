using Telegram.Bot;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal sealed class AutomationReviewEditFlow(
    AnnouncementReviewDraftRepository drafts,
    AnnouncementReviewHandler reviewHandler) : IConversationFlowHandler
{
    public bool CanHandle(AddStep step) =>
        step is AddStep.AutomationReviewWaitingName
            or AddStep.AutomationReviewWaitingPlace
            or AddStep.AutomationReviewWaitingDateTime
            or AddStep.AutomationReviewWaitingCost;

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        var draft = state.AutomationReviewPostId.HasValue
            ? drafts.Get(state.AutomationReviewPostId.Value)
            : null;
        if (draft is null || draft.Status != AnnouncementReviewStatuses.Pending)
        {
            await context.Bot.SendMessage(
                context.Message.Chat.Id,
                "Черновик уже обработан или не найден.",
                cancellationToken: context.CancellationToken);
            ClearState(context, state);
            return true;
        }

        var input = context.Message.Text?.Trim() ?? string.Empty;
        string? error = state.Step switch
        {
            AddStep.AutomationReviewWaitingName when string.IsNullOrWhiteSpace(input) =>
                "Название не может быть пустым.",
            AddStep.AutomationReviewWaitingPlace when string.IsNullOrWhiteSpace(input) =>
                "Площадка не может быть пустой.",
            AddStep.AutomationReviewWaitingDateTime
                when !context.Helper.TryParseDateTime(input, out _) =>
                Messages.Edit.InvalidDateTime,
            AddStep.AutomationReviewWaitingCost
                when !int.TryParse(input, out var cost) || cost <= 0 =>
                "Стоимость должна быть положительным целым числом.",
            _ => null
        };
        if (error is not null)
        {
            await context.Bot.SendMessage(
                context.Message.Chat.Id,
                error,
                cancellationToken: context.CancellationToken);
            return true;
        }

        switch (state.Step)
        {
            case AddStep.AutomationReviewWaitingName:
                draft.TournamentName = input;
                break;
            case AddStep.AutomationReviewWaitingPlace:
                draft.Place = input;
                break;
            case AddStep.AutomationReviewWaitingDateTime:
                context.Helper.TryParseDateTime(input, out var parsedUtc);
                draft.DateTimeUtc = parsedUtc;
                break;
            case AddStep.AutomationReviewWaitingCost:
                draft.Cost = int.Parse(input);
                break;
        }

        drafts.Upsert(draft);
        if (state.AutomationReviewChatId.HasValue &&
            state.AutomationReviewMessageId.HasValue)
        {
            await reviewHandler.RefreshAsync(
                draft,
                state.AutomationReviewChatId.Value,
                state.AutomationReviewMessageId.Value,
                context.CancellationToken);
        }

        await context.Bot.SendMessage(
            context.Message.Chat.Id,
            "Черновик обновлён.",
            cancellationToken: context.CancellationToken);
        ClearState(context, state);
        return true;
    }

    private static void ClearState(BotCommandContext context, AddAnnouncementState state)
    {
        state.Step = AddStep.Done;
        state.AutomationReviewPostId = null;
        state.AutomationReviewChatId = null;
        state.AutomationReviewMessageId = null;
        if (context.Message.From is not null)
        {
            context.StateStore.Remove(context.Message.From.Id);
        }
    }
}
