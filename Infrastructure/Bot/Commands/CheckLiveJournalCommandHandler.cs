using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal sealed class CheckLiveJournalCommandHandler(RssPoller poller) : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context) =>
        context.Helper.IsCommand(context.Message.Text, BotCommands.CheckLJ);

    public async Task HandleAsync(BotCommandContext context)
    {
        var message = context.IsAdminChat
            ? await poller.CheckAsync(context.CancellationToken)
            : Messages.Bot.AccessDenied;
        await context.Bot.SendMessage(context.Message.Chat.Id, message, cancellationToken: context.CancellationToken);
    }
}
