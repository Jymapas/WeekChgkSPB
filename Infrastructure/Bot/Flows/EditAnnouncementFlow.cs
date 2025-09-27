using System;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class EditAnnouncementFlow : IConversationFlowHandler
{
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
                    return (false, "Название не может быть пустым");
                }

                existing.TournamentName = context.Message.Text.Trim();
                return (true, "Название обновлено");
            }),
            AddStep.EditWaitingPlace => await HandleEdit(context, state, existing =>
            {
                existing.Place = context.Message.Text?.Trim() ?? string.Empty;
                return (true, "Место обновлено");
            }),
            AddStep.EditWaitingDateTime => await HandleEdit(context, state, existing =>
            {
                if (!context.Helper.TryParseDateTime(context.Message.Text, out var parsedUtc))
                {
                    return (false,
                        "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30");
                }

                existing.DateTimeUtc = parsedUtc;
                return (true, "Дата и время обновлены");
            }),
            AddStep.EditWaitingCost => await HandleEdit(context, state, existing =>
            {
                if (!int.TryParse(context.Message.Text, out var parsedCost))
                {
                    return (false, "Нужно целое число");
                }

                existing.Cost = parsedCost;
                return (true, "Стоимость обновлена");
            }),
            _ => false
        };
    }

    private static async Task<bool> HandleEdit(
        BotCommandContext context,
        AddAnnouncementState state,
        Func<Announcement, (bool Success, string Message)> mutator)
    {
        if (state.Existing is null)
        {
            state.Step = AddStep.None;
            await context.Bot.SendMessage(context.Message.Chat.Id, "Нет активного анонса для редактирования", cancellationToken: context.CancellationToken);
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
        await context.Bot.SendMessage(context.Message.Chat.Id, response, cancellationToken: context.CancellationToken);

        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        return true;
    }
}
