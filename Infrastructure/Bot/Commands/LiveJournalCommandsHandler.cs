using System.Globalization;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal sealed class LiveJournalCommandsHandler(LiveJournalIdentityRepository identities) : IBotCommandHandler
{
    public bool CanHandle(BotCommandContext context) =>
        context.Helper.IsCommand(context.Message.Text, BotCommands.LjBind) ||
        context.Helper.IsCommand(context.Message.Text, BotCommands.LjUnbind) ||
        context.Helper.IsCommand(context.Message.Text, BotCommands.LjList);

    public async Task HandleAsync(BotCommandContext context)
    {
        if (!context.IsAdminChat ||
            (context.Message.From is not null && context.UserManagement?.IsBanned(context.Message.From.Id) == true))
        {
            context.AccessDenied = true;
            await SendAsync(context, Messages.Bot.AccessDenied);
            return;
        }

        var parts = context.Message.Text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (context.Helper.IsCommand(parts[0], BotCommands.LjList))
        {
            await ListAsync(context, parts);
            return;
        }

        var bind = context.Helper.IsCommand(parts[0], BotCommands.LjBind);
        var allow = bind && parts.Length == 4 && parts[3] == "--allow";
        if (parts.Length != 3 && !allow)
        {
            await SendAsync(context, bind ? Messages.LiveJournal.BindUsage : Messages.LiveJournal.UnbindUsage);
            return;
        }
        if (!TryGetUserId(parts[1], out var userId))
        {
            await SendAsync(context, Messages.LiveJournal.InvalidUserId);
            return;
        }
        if (!LiveJournalIdentity.TryNormalizeLogin(parts[2], out var login))
        {
            await SendAsync(context, Messages.LiveJournal.InvalidLogin);
            return;
        }

        if (!bind)
        {
            await SendAsync(context, identities.Unbind(userId, login)
                ? Messages.LiveJournal.Unbound : Messages.LiveJournal.BindingNotFound);
            return;
        }

        var result = identities.Bind(userId, login, context.Message.From?.Id, allow);
        await SendAsync(context, result switch
        {
            LjBindingResult.Linked => Messages.LiveJournal.Bound(userId, login, allow),
            LjBindingResult.UnknownAuthor => Messages.LiveJournal.UnknownAuthor,
            LjBindingResult.AmbiguousAuthor => Messages.LiveJournal.AmbiguousAuthor,
            LjBindingResult.BannedUser => Messages.LiveJournal.BannedUser,
            _ => throw new InvalidOperationException("Unknown LiveJournal binding result")
        });
    }

    private async Task ListAsync(BotCommandContext context, string[] parts)
    {
        long? userId = null;
        if (parts.Length > 2)
        {
            await SendAsync(context, Messages.LiveJournal.ListUsage);
            return;
        }
        if (parts.Length == 2)
        {
            if (!TryGetUserId(parts[1], out var parsed))
            {
                await SendAsync(context, Messages.LiveJournal.InvalidUserId);
                return;
            }
            userId = parsed;
        }

        var links = identities.List(userId);
        if (links.Count == 0)
        {
            await SendAsync(context, Messages.LiveJournal.EmptyList);
            return;
        }
        foreach (var group in links.Chunk(20))
        {
            var lines = group.Select(link =>
                $"{link.TelegramUserId} → {link.LjUsername} (ЖЖ ID {link.LjUserId}; " +
                (link.IsBanned ? "заблокирован" : link.IsAllowed ? "в белом списке" : "нет в белом списке") + ")");
            await SendAsync(context, string.Join('\n', lines));
        }
    }

    private static bool TryGetUserId(string text, out long userId) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out userId) && userId > 0;

    private Task SendAsync(BotCommandContext context, string text) =>
        context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: context.CancellationToken);
}
