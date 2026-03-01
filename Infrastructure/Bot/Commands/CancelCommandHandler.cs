using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class CancelCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.Cancel);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var from = context.Message.From;
        if (from is not null)
        {
            context.StateStore.Remove(from.Id);
        }

        await context.Bot.SendMessage(
            context.Message.Chat.Id,
            "Текущее действие отменено",
            cancellationToken: context.CancellationToken);
    }
}
