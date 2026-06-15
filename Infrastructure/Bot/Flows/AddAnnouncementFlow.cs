using System;
using System.Collections.Generic;
using System.Text;
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
            await context.Bot.SendMessage(msg.Chat.Id, Messages.LinkRequired, cancellationToken: context.CancellationToken);
            return true;
        }

        if (context.Announcements.GetByLink(link) is not null)
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.AnnouncementAlreadyExists, cancellationToken: context.CancellationToken);
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
        await context.Bot.SendMessage(msg.Chat.Id, Messages.Add.PromptName, cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingName(BotCommandContext context, AddAnnouncementState state)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.Text))
        {
            await context.Bot.SendMessage(msg.Chat.Id, Messages.NameRequired, cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.TournamentName = msg.Text.Trim();
        state.Step = AddStep.WaitingPlace;
        await context.Bot.SendMessage(msg.Chat.Id, Messages.Add.PromptPlace, cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingPlace(BotCommandContext context, AddAnnouncementState state)
    {
        state.Draft.Place = context.Message.Text?.Trim() ?? string.Empty;
        state.Step = AddStep.WaitingDateTime;
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Add.PromptDateTime, cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingDateTime(BotCommandContext context, AddAnnouncementState state)
    {
        if (!context.Helper.TryParseDateTime(context.Message.Text, out var utcValue))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Add.InvalidDateTime, cancellationToken: context.CancellationToken);
            return true;
        }

        state.Draft.DateTimeUtc = utcValue;
        state.Step = AddStep.WaitingCost;
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Add.PromptCost, cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleWaitingCost(BotCommandContext context, AddAnnouncementState state)
    {
        var input = context.Message.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Add.PromptCost, cancellationToken: context.CancellationToken);
            return true;
        }

        var (cost, costLabel) = BotCommandHelper.ParseCost(input);
        state.Draft.Cost = cost;
        state.Draft.CostLabel = costLabel;
        
        var userId = context.Message.From?.Id;
        var isAdmin = context.IsAdminChat;
        var needsModeration = !isAdmin && userId.HasValue && 
                              (context.UserManagement is null || !context.UserManagement.IsAllowed(userId.Value));
        
        if (needsModeration && userId.HasValue)
        {
            if (context.UserManagement is null || context.Moderation is null)
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.ModerationUnavailable, cancellationToken: context.CancellationToken);
                return true;
            }

            if (context.UserManagement.IsBanned(userId.Value))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.UserBanned, cancellationToken: context.CancellationToken);
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
                CostLabel = state.Draft.CostLabel,
                UserId = userId.Value,
                Link = state.DraftLink,
                CreatedAt = DateTime.UtcNow
            };

            var pendingId = context.UserManagement.AddPending(pending);
            pending.Id = pendingId;

            var userName = context.Message.From?.Username is not null
                ? $"@{context.Message.From.Username}"
                : $"{context.Message.From?.FirstName} {context.Message.From?.LastName}".Trim();

            await context.Moderation.SendModerationRequest(pending, userId.Value, userName, context.CancellationToken);
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.AnnouncementSentForModeration, cancellationToken: context.CancellationToken);

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
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Add.ExternalLinkRequired, cancellationToken: context.CancellationToken);
                return true;
            }

            state.Draft.UserId = isAdmin ? null : userId;
            context.Announcements.InsertExternal(state.Draft, state.DraftLink);
        }
        await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);

        state.Step = AddStep.Done;
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Saved, cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        return true;
    }

    private async Task<bool> HandleWaitingLines(BotCommandContext context, AddAnnouncementState state)
    {
        var content = context.Message.Text ?? string.Empty;
        var blocks = context.Helper.SplitIntoBlocks(content);
        return blocks.Count > 1
            ? await HandleMultipleBlocks(context, state, blocks)
            : await HandleSingleBlock(context, state);
    }

    private async Task<bool> HandleSingleBlock(BotCommandContext context, AddAnnouncementState state)
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
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.AnnouncementAlreadyExists, cancellationToken: context.CancellationToken);
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
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.ModerationUnavailable, cancellationToken: context.CancellationToken);
                return true;
            }

            if (context.UserManagement.IsBanned(userId.Value))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.UserBanned, cancellationToken: context.CancellationToken);
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
                CostLabel = announcement.CostLabel,
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
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.AnnouncementSentForModeration, cancellationToken: context.CancellationToken);

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
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Saved, cancellationToken: context.CancellationToken);
        context.StateStore.Remove(context.Message.From!.Id);
        state.Existing = null;
        context.Helper.ResetDraft(state);
        return true;
    }

    private async Task<bool> HandleMultipleBlocks(BotCommandContext context, AddAnnouncementState state, IReadOnlyList<string> blocks)
    {
        var userId = context.Message.From?.Id;
        var isAdmin = context.IsAdminChat;
        var needsModeration = !isAdmin && userId.HasValue &&
                              (context.UserManagement is null || !context.UserManagement.IsAllowed(userId.Value));

        if (needsModeration && userId.HasValue)
        {
            if (context.UserManagement is null || context.Moderation is null)
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.ModerationUnavailable, cancellationToken: context.CancellationToken);
                ClearState(context, state);
                return true;
            }

            if (context.UserManagement.IsBanned(userId.Value))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.UserBanned, cancellationToken: context.CancellationToken);
                ClearState(context, state);
                return true;
            }
        }

        var savedCount = 0;
        var moderatedCount = 0;
        var errors = new List<string>();

        var userName = userId.HasValue
            ? (context.Message.From?.Username is not null
                ? $"@{context.Message.From.Username}"
                : $"{context.Message.From?.FirstName} {context.Message.From?.LastName}".Trim())
            : string.Empty;

        for (var i = 0; i < blocks.Count; i++)
        {
            var blockLabel = $"Блок {i + 1}";

            if (!context.Helper.TryBuildAnnouncementFromLines(blocks[i], out var announcement, out var link, out var error))
            {
                errors.Add($"{blockLabel}: {error}");
                continue;
            }

            if (context.Announcements.GetByLink(link) is not null)
            {
                errors.Add($"{blockLabel}: анонс с такой ссылкой уже есть");
                continue;
            }

            if (needsModeration && userId.HasValue)
            {
                var pending = new PendingAnnouncement
                {
                    TournamentName = announcement.TournamentName,
                    Place = announcement.Place,
                    DateTimeUtc = announcement.DateTimeUtc,
                    Cost = announcement.Cost,
                    CostLabel = announcement.CostLabel,
                    UserId = userId.Value,
                    Link = link,
                    CreatedAt = DateTime.UtcNow
                };

                var pendingId = context.UserManagement!.AddPending(pending);
                pending.Id = pendingId;
                await context.Moderation!.SendModerationRequest(pending, userId.Value, userName, context.CancellationToken);
                moderatedCount++;
            }
            else
            {
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
                savedCount++;
            }
        }

        if (savedCount > 0)
        {
            await _channelPostUpdater.UpdateLastPostAsync(context.CancellationToken);
        }

        await context.Bot.SendMessage(context.Message.Chat.Id, BuildSummary(blocks.Count, savedCount, moderatedCount, errors), cancellationToken: context.CancellationToken);
        ClearState(context, state);
        return true;
    }

    private static string BuildSummary(int total, int saved, int moderated, List<string> errors)
    {
        var sb = new StringBuilder();

        if (saved > 0)
            sb.AppendLine(Messages.Add.MultiSavedCount(saved, total));
        if (moderated > 0)
            sb.AppendLine(Messages.Add.MultiModeratedCount(moderated, total));
        if (errors.Count > 0)
        {
            sb.AppendLine(Messages.Add.MultiErrorsHeader(errors.Count));
            foreach (var e in errors)
                sb.AppendLine(Messages.Add.MultiErrorLine(e));
        }

        return sb.ToString().TrimEnd();
    }

    private void ClearState(BotCommandContext context, AddAnnouncementState state)
    {
        var userId = context.Message.From?.Id;
        if (userId.HasValue)
            context.StateStore.Remove(userId.Value);
        state.Existing = null;
        context.Helper.ResetDraft(state);
    }
}
