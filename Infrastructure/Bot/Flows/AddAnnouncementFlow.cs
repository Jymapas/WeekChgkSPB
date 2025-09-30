using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class AddAnnouncementFlow : IConversationFlowHandler
{
    private readonly IChannelPostUpdater _channelPostUpdater;

    public AddAnnouncementFlow(IChannelPostUpdater channelPostUpdater)
    {
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(AddStep step)
    {
        return step is AddStep.WaitingId
            or AddStep.WaitingName
            or AddStep.WaitingPlace
            or AddStep.WaitingDateTime
            or AddStep.WaitingCost
            or AddStep.WaitingLines;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        return state.Step switch
        {
            AddStep.WaitingId => await HandleWaitingId(context, state),
            AddStep.WaitingName => await HandleWaitingName(context, state),
            AddStep.WaitingPlace => await HandleWaitingPlace(context, state),
            AddStep.WaitingDateTime => await HandleWaitingDateTime(context, state),
            AddStep.WaitingCost => await HandleWaitingCost(context, state),
            AddStep.WaitingLines => await HandleWaitingLines(context, state),
            _ => false
        };
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

        state.Draft.TournamentName = msg.Text.Trim();
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
        if (!context.Helper.TryParseDateTime(context.Message.Text, out var utcValue))
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
        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);

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
        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        await context.Bot.SendMessage(context.Message.Chat.Id, "Сохранено", cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        context.Helper.ResetDraft(state);
        return true;
    }
}
