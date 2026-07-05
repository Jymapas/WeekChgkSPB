using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class FooterEditCommandHandler : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context) =>
        context.Helper.IsCommand(context.Message.Text, BotCommands.FooterEdit);

    public async Task HandleAsync(BotCommandContext context)
    {
        var footers = context.Footers.ListAllDesc();
        if (footers.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                Messages.Footer.NoFooters, cancellationToken: context.CancellationToken);
            return;
        }

        var rows = footers.Select(f =>
        {
            var label = f.Text.Length > 30 ? f.Text[..27] + "…" : f.Text;
            label = $"[{f.Id}] {label}";
            return new[] { InlineKeyboardButton.WithCallbackData(label, $"footeredit_select_{f.Id}") };
        });

        var keyboard = new InlineKeyboardMarkup(rows);
        await context.Bot.SendMessage(context.Message.Chat.Id,
            Messages.Footer.SelectPrompt, replyMarkup: keyboard,
            cancellationToken: context.CancellationToken);
    }
}
