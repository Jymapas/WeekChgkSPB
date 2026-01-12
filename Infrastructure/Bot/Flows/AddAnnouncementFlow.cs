using System;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class AddAnnouncementFlow : IConversationFlowHandler
{
    private readonly IChannelPostUpdater _channelPostUpdater;

    public AddAnnouncementFlow(IChannelPostUpdater channelPostUpdater)
    {
        _channelPostUpdater = channelPostUpdater;
    }

    public bool CanHandle(AddStep step)
    {
        return step is AddStep.WaitingId
            or AddStep.WaitingName
            or AddStep.WaitingPlace
            or AddStep.WaitingDateTime
            or AddStep.WaitingCost
            or AddStep.WaitingLines;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        return state.Step switch
        {
            AddStep.WaitingId => await HandleWaitingId(context, state),
            AddStep.WaitingName => await HandleWaitingName(context, state),
            AddStep.WaitingPlace => await HandleWaitingPlace(context, state),
            AddStep.WaitingDateTime => await HandleWaitingDateTime(context, state),
            AddStep.WaitingCost => await HandleWaitingCost(context, state),
            AddStep.WaitingLines => await HandleWaitingLines(context, state),
            _ => false
        };
    }

    private async Task<bool> HandleWaitingId(BotCommandContext context, AddAnnouncementState state)
    {
        var msg = context.Message;
        var link = context.Helper.NormalizePostLink(msg.Text);
        if (string.IsNullOrWhiteSpace(link))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Нужна ссылка на пост или id в ЖЖ", cancellationToken: context.CancellationToken);
            return true;
        }

        if (context.Announcements.GetByLink(link) is not null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Анонс с такой ссылкой уже есть", cancellationToken: context.CancellationToken);
            state.Step = AddStep.None;
            return true;
        }

        if (context.Posts.TryGetIdByLink(link, out var id))
        {
            state.Draft.Id = id;
        }
        else
        {
            state.Draft.Id = 0;
        }

        state.DraftLink = link;
        state.Step = AddStep.WaitingName;
        await context.Bot.SendMessage(msg.Chat.Id, "Название турнира", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingName(BotCommandContext context, AddAnnouncementState state)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.Text))
        {
            await context.Bot.SendMessage(msg.Chat.Id, "Название не может быть пустым", cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.TournamentName = msg.Text.Trim();
        state.Step = AddStep.WaitingPlace;
        await context.Bot.SendMessage(msg.Chat.Id, "Место проведения", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingPlace(BotCommandContext context, AddAnnouncementState state)
    {
        state.Draft.Place = context.Message.Text?.Trim() ?? string.Empty;
        state.Step = AddStep.WaitingDateTime;
        await context.Bot.SendMessage(context.Message.Chat.Id,
            "Дата и время по Москве. Можно отправить ISO (пример: 2025-08-10T19:30) " +
            "или двумя строками: дата (например, 22 сентября) и новой строкой время (например, 19:30)",
            cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingDateTime(BotCommandContext context, AddAnnouncementState state)
    {
        if (!context.Helper.TryParseDateTime(context.Message.Text, out var utcValue))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30",
                cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.DateTimeUtc = utcValue;
        state.Step = AddStep.WaitingCost;
        await context.Bot.SendMessage(context.Message.Chat.Id, "Стоимость (целое число)", cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingCost(BotCommandContext context, AddAnnouncementState state)
    {
        if (!int.TryParse(context.Message.Text, out var cost))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Нужно целое число", cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.Cost = cost;
        
        var userId = context.Message.From?.Id;
        var isAdmin = context.IsAdminChat;
        var needsModeration = !isAdmin && userId.HasValue && 
                              (context.UserManagement is null || !context.UserManagement.IsAllowed(userId.Value));
        
        if (needsModeration && userId.HasValue)
        {
            if (context.UserManagement is null || context.Moderation is null)
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, "Ошибка: система модерации недоступна", cancellationToken: context.CancellationToken);
                return true;
            }
            
            if (context.UserManagement.IsBanned(userId.Value))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, "Вы заблокированы и не можете добавлять анонсы", cancellationToken: context.CancellationToken);
                state.Step = AddStep.None;
                context.StateStore.Remove(userId.Value);
                return true;
            }
            
            var pending = new PendingAnnouncement
            {
                TournamentName = state.Draft.TournamentName,
                Place = state.Draft.Place,
                DateTimeUtc = state.Draft.DateTimeUtc,
                Cost = state.Draft.Cost,
                UserId = userId.Value,
                Link = state.Draft.Id > 0 ? null : state.DraftLink,
                CreatedAt = DateTime.UtcNow
            };
            
            var pendingId = context.UserManagement.AddPending(pending);
            pending.Id = pendingId;
            
            var userName = context.Message.From?.Username is not null
                ? $"@{context.Message.From.Username}"
                : $"{context.Message.From?.FirstName} {context.Message.From?.LastName}".Trim();
            
            await context.Moderation.SendModerationRequest(pending, userId.Value, userName, context.CancellationToken);
            await context.Bot.SendMessage(context.Message.Chat.Id, "Ваш анонс отправлен на модерацию", cancellationToken: context.CancellationToken);
            
            state.Step = AddStep.None;
            context.StateStore.Remove(userId.Value);
            state.Existing = null;
            return true;
        }
        
        if (state.Draft.Id > 0)
        {
            state.Draft.UserId = isAdmin ? null : userId;
            context.Announcements.Insert(state.Draft);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(state.DraftLink))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, "Нужна ссылка на пост", cancellationToken: context.CancellationToken);
                return true;
            }

            state.Draft.UserId = isAdmin ? null : userId;
            context.Announcements.InsertExternal(state.Draft, state.DraftLink);
        }
        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);

        state.Step = AddStep.Done;
        await context.Bot.SendMessage(context.Message.Chat.Id, "Сохранено", cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        return true;
    }

    private async Task<bool> HandleWaitingLines(BotCommandContext context, AddAnnouncementState state)
    {
        var content = context.Message.Text ?? string.Empty;
        if (!context.Helper.TryBuildAnnouncementFromLines(content, out var announcement, out var link, out var error))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, error, cancellationToken: context.CancellationToken);
            await context.Bot.SendMessage(context.Message.Chat.Id, context.Helper.AddLinesPrompt, cancellationToken: context.CancellationToken);
            return true;
        }

        if (context.Announcements.GetByLink(link) is not null)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "Анонс с такой ссылкой уже есть", cancellationToken: context.CancellationToken);
            return true;
        }

        var userId = context.Message.From?.Id;
        var isAdmin = context.IsAdminChat;
        var needsModeration = !isAdmin && userId.HasValue && 
                              (context.UserManagement is null || !context.UserManagement.IsAllowed(userId.Value));
        
        if (needsModeration && userId.HasValue)
        {
            if (context.UserManagement is null || context.Moderation is null)
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, "Ошибка: система модерации недоступна", cancellationToken: context.CancellationToken);
                return true;
            }
            
            if (context.UserManagement.IsBanned(userId.Value))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, "Вы заблокированы и не можете добавлять анонсы", cancellationToken: context.CancellationToken);
                context.StateStore.Remove(userId.Value);
                context.Helper.ResetDraft(state);
                return true;
            }
            
            var pending = new PendingAnnouncement
            {
                TournamentName = announcement.TournamentName,
                Place = announcement.Place,
                DateTimeUtc = announcement.DateTimeUtc,
                Cost = announcement.Cost,
                UserId = userId.Value,
                Link = link,
                CreatedAt = DateTime.UtcNow
            };
            
            var pendingId = context.UserManagement.AddPending(pending);
            pending.Id = pendingId;
            
            var userName = context.Message.From?.Username is not null
                ? $"@{context.Message.From.Username}"
                : $"{context.Message.From?.FirstName} {context.Message.From?.LastName}".Trim();
            
            await context.Moderation.SendModerationRequest(pending, userId.Value, userName, context.CancellationToken);
            await context.Bot.SendMessage(context.Message.Chat.Id, "Ваш анонс отправлен на модерацию", cancellationToken: context.CancellationToken);
            
            context.StateStore.Remove(userId.Value);
            state.Existing = null;
            context.Helper.ResetDraft(state);
            return true;
        }
        
        if (context.Posts.TryGetIdByLink(link, out var id))
        {
            announcement.Id = id;
            announcement.UserId = isAdmin ? null : userId;
            context.Announcements.Insert(announcement);
        }
        else
        {
            announcement.UserId = isAdmin ? null : userId;
            context.Announcements.InsertExternal(announcement, link);
        }

        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        await context.Bot.SendMessage(context.Message.Chat.Id, "Сохранено", cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        context.Helper.ResetDraft(state);
        return true;
    }
}
