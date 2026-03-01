using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal abstract class EditAnnouncementCommandHandlerBase : IBotCommandHandler
{
    private readonly string _command;
    private readonly AddStep _waitingStep;
    private readonly string _usage;
    private readonly IChannelPostUpdater _channelPostUpdater;

    protected EditAnnouncementCommandHandlerBase(
        string command,
        AddStep waitingStep,
        string usage,
        IChannelPostUpdater channelPostUpdater)
    {
        _command = command;
        _waitingStep = waitingStep;
        _usage = usage;
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, _command);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        var text = msg.Text;
        if (text is null)
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Bot.SendMessage(msg.Chat.Id, $"Используй: {_usage}", cancellationToken: context.CancellationToken);
            return;
        }

        var link = context.Helper.NormalizePostLink(parts[1]);
        var existing = context.Announcements.GetByLink(link);
        if (existing is null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Анонс с такой ссылкой не найден", cancellationToken: context.CancellationToken);
            return;
        }

        var userId = msg.From?.Id;
        var isAdmin = context.IsAdminChat;
        var canEdit = isAdmin || (userId.HasValue && existing.UserId == userId);

        if (!canEdit)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Вы можете редактировать только свои анонсы", cancellationToken: context.CancellationToken);
            return;
        }

        var hasInlineValue = parts.Length > 2;
        var inlineValue = hasInlineValue ? string.Join(' ', parts.Skip(2)) : null;

        if (hasInlineValue)
        {
            var (success, message) = Apply(existing, inlineValue, context.Helper);
            if (!success)
            {
                await context.Bot.SendMessage(msg.Chat.Id, message, cancellationToken: context.CancellationToken);

                if (msg.From is not null)
                {
                    var st = context.StateStore.AddOrUpdate(msg.From.Id);
                    st.Step = _waitingStep;
                    st.Existing = existing;
                    await context.Bot.SendMessage(msg.Chat.Id, BuildPrompt(existing, context.Helper), cancellationToken: context.CancellationToken);
                }

                return;
            }

            context.Announcements.Update(existing);
            await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
            await context.Bot.SendMessage(msg.Chat.Id, message, cancellationToken: context.CancellationToken);

            if (msg.From is not null)
            {
                context.StateStore.Remove(msg.From.Id);
            }

            return;
        }

        if (msg.From is null)
        {
            return;
        }

        var state = context.StateStore.AddOrUpdate(msg.From.Id);
        state.Step = _waitingStep;
        state.Existing = existing;

        await context.Bot.SendMessage(msg.Chat.Id, BuildPrompt(existing, context.Helper), cancellationToken: context.CancellationToken);
    }

    protected abstract string BuildPrompt(Announcement existing, BotCommandHelper helper);

    protected abstract (bool Success, string Message) Apply(Announcement existing, string? inlineValue, BotCommandHelper helper);
}
