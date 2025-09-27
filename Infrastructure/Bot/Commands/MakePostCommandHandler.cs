using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class MakePostCommandHandler : IBotCommandHandler
{
    private readonly string _command;
    private readonly bool _asLiveJournal;

    public MakePostCommandHandler(string command, bool asLiveJournal)
    {
        _command = command;
        _asLiveJournal = asLiveJournal;
    }

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, _command);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var (fromUtc, toUtc) = context.Helper.ResolveDateRangeOrDefault(context.Message.Text!);
        var rows = context.Announcements.GetWithLinksInRange(fromUtc, toUtc);
        if (rows.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "В выбранном диапазоне анонсов нет", cancellationToken: context.CancellationToken);
            return;
        }

        var footerLines = context.Footers.GetAllTextsDesc();

        if (_asLiveJournal)
        {
            var ljHtml = PostFormatter.BuildScheduleHtml(rows, footerLines);
            var codeMsg = PostFormatter.WrapAsCodeForTelegram(ljHtml);

            await context.Bot.SendMessage(
                context.Message.Chat.Id,
                codeMsg,
                ParseMode.Html,
                linkPreviewOptions: new Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                cancellationToken: context.CancellationToken);
        }
        else
        {
            var text = PostFormatter.BuildScheduleMessage(rows, footerLines);
            await context.Bot.SendMessage(
                context.Message.Chat.Id,
                text,
                ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: context.CancellationToken);
        }
    }
}
