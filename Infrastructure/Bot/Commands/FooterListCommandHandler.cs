using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class FooterListCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.FooterList);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var all = context.Footers.ListAllDesc();
        if (all.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.Empty, cancellationToken: context.CancellationToken);
            return;
        }

        var lines = all.Select(x =>
        {
            if (x.ExpiresAt.HasValue)
            {
                var moscowDate = TimeZoneInfo.ConvertTimeFromUtc(x.ExpiresAt.Value, PostFormatter.Moscow);
                return $"{x.Id}: {x.Text} [до {moscowDate:dd.MM.yyyy}]";
            }
            return $"{x.Id}: {x.Text}";
        });
        var text = string.Join("\n", lines);
        await context.Bot.SendMessage(context.Message.Chat.Id, "<code>" + context.Helper.EscapeForCode(text) + "</code>", ParseMode.Html, cancellationToken: context.CancellationToken);
    }
}
