using Telegram.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal sealed class DeleteAnnouncementCommandHandler(IChannelPostUpdater channelPostUpdater) : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context) => context.Helper.IsCommand(context.Message.Text, BotCommands.Delete);

    public async Task HandleAsync(BotCommandContext context)
    {
        var msg = context.Message;
        var parts = msg.Text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.Delete.Usage, cancellationToken: context.CancellationToken);
            return;
        }

        var link = context.Helper.NormalizePostLink(parts[1]);
        var existing = context.Announcements.GetByLink(link);
        if (existing is null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementNotFound, cancellationToken: context.CancellationToken);
            return;
        }

        var result = context.Announcements.DeleteAuthorized(existing.Id, context.Actor);
        if (msg.From is not null) context.StateStore.Remove(msg.From.Id);
        if (result.Status == AnnouncementMutationStatus.Denied)
        {
            context.AccessDenied = true;
            await context.Bot.SendMessage(msg.Chat.Id, Messages.Delete.CannotDeleteOthers, cancellationToken: context.CancellationToken);
            return;
        }
        if (result.Status == AnnouncementMutationStatus.NotFound)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementNotFound, cancellationToken: context.CancellationToken);
            return;
        }

        if (result.Status == AnnouncementMutationStatus.Changed)
            await channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        await context.Bot.SendMessage(msg.Chat.Id,
            Messages.Delete.Deleted(result.Announcement!.TournamentName, link), cancellationToken: context.CancellationToken);
    }
}
