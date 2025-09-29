using System;
using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class FooterDeleteCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.FooterDel);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Используй: /footer_del <id>", cancellationToken: context.CancellationToken);
            return;
        }

        context.Footers.Delete(id);
        await context.Bot.SendMessage(msg.Chat.Id, "Удалено", cancellationToken: context.CancellationToken);
    }
}
