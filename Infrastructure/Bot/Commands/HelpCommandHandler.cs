using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class HelpCommandHandler : IBotCommandHandler
{

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.Help);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        await context.Bot.SendMessage(
            context.Message.Chat.Id,
            Messages.Help.Text,
            cancellationToken: context.CancellationToken);
    }
}
