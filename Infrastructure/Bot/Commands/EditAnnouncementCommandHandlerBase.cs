using Telegram.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal abstract class EditAnnouncementCommandHandlerBase : IBotCommandHandler
{
    private readonly string _command;
    private readonly AddStep _waitingStep;
    private readonly string _usage;
    private readonly IChannelPostUpdater _channelPostUpdater;

    protected EditAnnouncementCommandHandlerBase(
        string command, AddStep waitingStep, string usage, IChannelPostUpdater channelPostUpdater)
    {
        _command = command;
        _waitingStep = waitingStep;
        _usage = usage;
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(BotCommandContext context) => context.Helper.IsCommand(context.Message.Text, _command);

    public async Task HandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        if (msg.Text is null) return;
        var parts = msg.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.Edit.Usage(_usage), cancellationToken: context.CancellationToken);
            return;
        }

        var existing = context.Announcements.GetByLink(context.Helper.NormalizePostLink(parts[1]));
        if (existing is null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementNotFound, cancellationToken: context.CancellationToken);
            return;
        }
        if (!context.Announcements.CanManage(existing.Id, context.Actor))
        {
            await DenyAsync(context);
            return;
        }
        if (parts.Length == 2)
        {
            await BeginFlowAsync(context, existing);
            return;
        }

        var value = string.Join(' ', parts.Skip(2));
        var result = context.Announcements.EditAuthorized(existing.Id, context.Actor, fresh => Apply(fresh, value, context.Helper));
        if (result.Status == AnnouncementMutationStatus.Denied)
        {
            await DenyAsync(context);
            return;
        }
        if (result.Status == AnnouncementMutationStatus.NotFound)
        {
            if (msg.From is not null) context.StateStore.Remove(msg.From.Id);
            await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementNotFound, cancellationToken: context.CancellationToken);
            return;
        }
        if (result.Status == AnnouncementMutationStatus.Invalid)
        {
            await context.Bot.SendMessage(msg.Chat.Id, result.Message!, cancellationToken: context.CancellationToken);
            // Повторно проверяем доступ: привязку могли отозвать во время отправки ответа.
            var fresh = context.Announcements.Get(existing.Id);
            if (fresh is null)
            {
                if (msg.From is not null) context.StateStore.Remove(msg.From.Id);
                await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementNotFound, cancellationToken: context.CancellationToken);
            }
            else if (context.Announcements.CanManage(fresh.Id, context.Actor))
                await BeginFlowAsync(context, fresh);
            else
                await DenyAsync(context);
            return;
        }

        if (result.Status == AnnouncementMutationStatus.Changed)
            await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        await context.Bot.SendMessage(msg.Chat.Id, result.Message!, cancellationToken: context.CancellationToken);
        if (msg.From is not null) context.StateStore.Remove(msg.From.Id);
    }

    private async Task BeginFlowAsync(BotCommandContext context, Announcement existing)
    {
        if (context.Message.From is null) return;
        var state = context.StateStore.AddOrUpdate(context.Message.From.Id);
        state.Step = _waitingStep;
        state.Existing = existing;
        state.EditChatId = context.Message.Chat.Id;
        await context.Bot.SendMessage(context.Message.Chat.Id, BuildPrompt(existing, context.Helper), cancellationToken: context.CancellationToken);
    }

    private async Task DenyAsync(BotCommandContext context)
    {
        context.AccessDenied = true;
        if (context.Message.From is not null) context.StateStore.Remove(context.Message.From.Id);
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Edit.CannotEditOthers, cancellationToken: context.CancellationToken);
    }

    protected abstract string BuildPrompt(Announcement existing, BotCommandHelper helper);
    protected abstract (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper);
}
