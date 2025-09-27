using System;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class ConversationFlowProcessor
{
    private readonly BotCommandHelper _helper;

    public ConversationFlowProcessor(BotCommandHelper helper)
    {
        _helper = helper;
    }

    public async Task<bool> TryHandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        if (msg.From is null)
        {
            return false;
        }

        if (!context.StateStore.TryGet(msg.From.Id, out var state) || state is null || state.Step == AddStep.None)
        {
            return false;
        }

        switch (state.Step)
        {
            case AddStep.WaitingId:
                return await HandleWaitingId(context, state);
            case AddStep.WaitingName:
                return await HandleWaitingName(context, state);
            case AddStep.WaitingPlace:
                return await HandleWaitingPlace(context, state);
            case AddStep.WaitingDateTime:
                return await HandleWaitingDateTime(context, state);
            case AddStep.WaitingCost:
                return await HandleWaitingCost(context, state);
            case AddStep.WaitingLines:
                return await HandleWaitingLines(context, state);
            case AddStep.EditWaitingName:
                return await ApplyEditFromState(context, state, existing =>
                {
                    if (string.IsNullOrWhiteSpace(msg.Text))
                    {
                        return (false, "Название не может быть пустым");
                    }

                    existing.TournamentName = msg.Text!.Trim();
                    return (true, "Название обновлено");
                });
            case AddStep.EditWaitingPlace:
                return await ApplyEditFromState(context, state, existing =>
                {
                    existing.Place = msg.Text?.Trim() ?? string.Empty;
                    return (true, "Место обновлено");
                });
            case AddStep.EditWaitingDateTime:
                return await ApplyEditFromState(context, state, existing =>
                {
                    if (!_helper.TryParseDateTime(msg.Text, out var parsedUtc))
                    {
                        return (false,
                            "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30");
                    }

                    existing.DateTimeUtc = parsedUtc;
                    return (true, "Дата и время обновлены");
                });
            case AddStep.EditWaitingCost:
                return await ApplyEditFromState(context, state, existing =>
                {
                    if (!int.TryParse(msg.Text, out var parsedCost))
                    {
                        return (false, "Нужно целое число");
                    }

                    existing.Cost = parsedCost;
                    return (true, "Стоимость обновлена");
                });
            case AddStep.FooterWaitingText:
                return await HandleFooterText(context, state);
            default:
                return false;
        }
    }

    private async Task<bool> HandleWaitingId(BotCommandContext context, AddAnnouncementState state)
    {
        var msg = context.Message;
        if (!long.TryParse(msg.Text, out var id))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Нужен числовой id", cancellationToken: context.CancellationToken);
            return true;
        }

        if (!context.Posts.Exists(id))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Такого поста нет в базе", cancellationToken: context.CancellationToken);
            return true;
        }

        if (context.Announcements.Exists(id))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Анонс для этого id уже есть", cancellationToken: context.CancellationToken);
            state.Step = AddStep.None;
            return true;
        }

        state.Draft.Id = id;
        state.Step = AddStep.WaitingName;
        await context.Bot.SendMessage(msg.Chat.Id, "Название турнира", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingName(BotCommandContext context, AddAnnouncementState state)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.Text))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Название не может быть пустым", cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.TournamentName = msg.Text!.Trim();
        state.Step = AddStep.WaitingPlace;
        await context.Bot.SendMessage(msg.Chat.Id, "Место проведения", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingPlace(BotCommandContext context, AddAnnouncementState state)
    {
        state.Draft.Place = context.Message.Text?.Trim() ?? string.Empty;
        state.Step = AddStep.WaitingDateTime;
        await context.Bot.SendMessage(context.Message.Chat.Id,
            "Дата и время по Москве. Можно отправить ISO (пример: 2025-08-10T19:30) " +
            "или двумя строками: дата (например, 22 сентября) и новой строкой время (например, 19:30)",
            cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingDateTime(BotCommandContext context, AddAnnouncementState state)
    {
        if (!_helper.TryParseDateTime(context.Message.Text, out var utcValue))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30",
                cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.DateTimeUtc = utcValue;
        state.Step = AddStep.WaitingCost;
        await context.Bot.SendMessage(context.Message.Chat.Id, "Стоимость (целое число)", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingCost(BotCommandContext context, AddAnnouncementState state)
    {
        if (!int.TryParse(context.Message.Text, out var cost))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Нужно целое число", cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.Cost = cost;
        context.Announcements.Insert(state.Draft);

        state.Step = AddStep.Done;
        await context.Bot.SendMessage(context.Message.Chat.Id, "Сохранено", cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        return true;
    }

    private async Task<bool> HandleWaitingLines(BotCommandContext context, AddAnnouncementState state)
    {
        var content = context.Message.Text ?? string.Empty;
        if (!context.Helper.TryBuildAnnouncementFromLines(content, out var announcement, out var error))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, error, cancellationToken: context.CancellationToken);
            await context.Bot.SendMessage(context.Message.Chat.Id, context.Helper.AddLinesPrompt, cancellationToken: context.CancellationToken);
            return true;
        }

        if (!context.Posts.Exists(announcement.Id))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Такого поста нет в базе", cancellationToken: context.CancellationToken);
            return true;
        }

        if (context.Announcements.Exists(announcement.Id))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Анонс для этого id уже есть", cancellationToken: context.CancellationToken);
            return true;
        }

        context.Announcements.Insert(announcement);
        await context.Bot.SendMessage(context.Message.Chat.Id, "Сохранено", cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        _helper.ResetDraft(state);
        return true;
    }

    private async Task<bool> HandleFooterText(BotCommandContext context, AddAnnouncementState state)
    {
        var html = context.Message.Text?.Trim();
        if (string.IsNullOrEmpty(html))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Нужен непустой текст", cancellationToken: context.CancellationToken);
            return true;
        }

        var footerId = context.Footers.Insert(html);
        await context.Bot.SendMessage(context.Message.Chat.Id, $"Футер добавлен с id={footerId}", cancellationToken: context.CancellationToken);
        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }

    private async Task<bool> ApplyEditFromState(
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
