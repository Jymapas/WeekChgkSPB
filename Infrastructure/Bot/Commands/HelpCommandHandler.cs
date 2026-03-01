using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class HelpCommandHandler : IBotCommandHandler
{
    private const string HelpText =
        "Доступные команды:\n" +
        "/help - показать эту справку\n" +
        "/add - добавить анонс одним сообщением\n" +
        "/add_lines - добавить анонс по шагам\n" +
        "/edit - показать команды редактирования\n" +
        "/edit_name - изменить название\n" +
        "/edit_place - изменить место\n" +
        "/edit_datetime - изменить дату и время\n" +
        "/edit_cost - изменить стоимость\n" +
        "/delete - удалить свой анонс";

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.Help);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        await context.Bot.SendMessage(
            context.Message.Chat.Id,
            HelpText,
            cancellationToken: context.CancellationToken);
    }
}
