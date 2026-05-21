using System;
using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class EditCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.Edit);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Edit.HelpText, cancellationToken: context.CancellationToken);
    }
}
