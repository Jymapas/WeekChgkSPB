using System;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class DeleteAnnouncementCommandHandler : IBotCommandHandler
{
    private readonly IChannelPostUpdater _channelPostUpdater;

    public DeleteAnnouncementCommandHandler(IChannelPostUpdater channelPostUpdater)
    {
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(BotCommandContext context)
    {
        return context.Helper.IsCommand(context.Message.Text, BotCommands.Delete);
    }

    public async Task HandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Используй: /delete <ссылка|id>", cancellationToken: context.CancellationToken);
            return;
        }

        var link = context.Helper.NormalizePostLink(parts[1]);
        var existing = context.Announcements.GetByLink(link);
        if (existing is null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Анонс с такой ссылкой не найден", cancellationToken: context.CancellationToken);
            return;
        }

        var deleted = context.Announcements.Delete(existing.Id);
        if (msg.From is not null)
        {
            context.StateStore.Remove(msg.From.Id);
        }

        if (deleted)
        {
            await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        }

        await context.Bot.SendMessage(
            msg.Chat.Id,
            $"Анонс ({existing.TournamentName}) удален: {link}",
            cancellationToken: context.CancellationToken);
    }
}
