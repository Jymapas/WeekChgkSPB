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
        const string usage = "Используй команды: /edit_name, /edit_place, /edit_datetime, /edit_cost";
        await context.Bot.SendMessage(context.Message.Chat.Id, usage, cancellationToken: context.CancellationToken);
    }
}
