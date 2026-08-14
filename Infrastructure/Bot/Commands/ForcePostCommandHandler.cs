using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class ForcePostCommandHandler : IBotCommandHandler
{
    private readonly string? _channelId;

    public ForcePostCommandHandler(string? channelId)
    {
        _channelId = channelId;
    }

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.ForcePost);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        if (!context.IsAdminChat)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Bot.AccessDenied, cancellationToken: context.CancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(_channelId))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.ForcePost.NoChannel, cancellationToken: context.CancellationToken);
            return;
        }

        var (fromUtc, toUtc) = context.Helper.ResolveDateRangeOrDefault(context.Message.Text!);
        var rows = context.Announcements.GetWithLinksInRange(fromUtc, toUtc);
        if (rows.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.ForcePost.NoAnnouncements, cancellationToken: context.CancellationToken);
            return;
        }

        var footerLines = context.Footers.GetAllTextsDesc();
        var text = PostFormatter.BuildScheduleMessage(rows, footerLines);

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Messages.ForcePost.ButtonAccept, "forcepost_confirm"),
                InlineKeyboardButton.WithCallbackData(Messages.ForcePost.ButtonCancel, "forcepost_cancel"),
            },
        });

        await context.Bot.SendMessage(
            context.Message.Chat.Id,
            text,
            ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            replyMarkup: keyboard,
            cancellationToken: context.CancellationToken);
    }
}
