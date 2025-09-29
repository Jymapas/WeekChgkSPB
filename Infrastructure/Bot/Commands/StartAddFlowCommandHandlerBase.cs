using System;
using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal abstract class StartAddFlowCommandHandlerBase : IBotCommandHandler
{
    private readonly string _command;
    private readonly AddStep _targetStep;
    private readonly Func<BotCommandContext, string> _promptFactory;

    protected StartAddFlowCommandHandlerBase(
        string command,
        AddStep targetStep,
        Func<BotCommandContext, string> promptFactory)
    {
        _command = command;
        _targetStep = targetStep;
        _promptFactory = promptFactory;
    }

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, _command);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var from = context.Message.From;
        if (from is null)
        {
            return;
        }

        var state = context.StateStore.AddOrUpdate(from.Id);
        state.Existing = null;
        state.Step = _targetStep;
        context.Helper.ResetDraft(state);

        var prompt = _promptFactory(context);
        await context.Bot.SendMessage(context.Message.Chat.Id, prompt, cancellationToken: context.CancellationToken);
    }
}
