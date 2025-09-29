using System;
using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class FooterAddCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.FooterAdd);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        if (context.Message.From is null)
        {
            return;
        }

        var state = context.StateStore.AddOrUpdate(context.Message.From.Id);
        state.Existing = null;
        state.Step = AddStep.FooterWaitingText;

        await context.Bot.SendMessage(context.Message.Chat.Id, "Отправь одну строку HTML для футера", cancellationToken: context.CancellationToken);
    }
}
