using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal abstract class EditAnnouncementCommandHandlerBase : IBotCommandHandler
{
    private readonly string _command;
    private readonly AddStep _waitingStep;
    private readonly string _usage;

    protected EditAnnouncementCommandHandlerBase(string command, AddStep waitingStep, string usage)
    {
        _command = command;
        _waitingStep = waitingStep;
        _usage = usage;
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
        if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
        {
            await context.Bot.SendMessage(msg.Chat.Id, $"Используй: {_usage}", cancellationToken: context.CancellationToken);
            return;
        }

        var existing = context.Announcements.Get(id);
        if (existing is null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Анонс с таким id не найден", cancellationToken: context.CancellationToken);
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
