using System;
using System.Globalization;
using System.Threading.Tasks;
using Telegram.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class FooterFlow : IConversationFlowHandler
{
    public bool CanHandle(AddStep step)
    {
        return step is AddStep.FooterWaitingText or AddStep.FooterWaitingExpiry
            or AddStep.FooterEditWaitingText or AddStep.FooterEditWaitingExpiry;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        if (state.Step == AddStep.FooterWaitingText)
            return await HandleTextStepAsync(context, state);

        if (state.Step == AddStep.FooterWaitingExpiry)
            return await HandleExpiryStepAsync(context, state);

        if (state.Step == AddStep.FooterEditWaitingText)
            return await HandleEditTextStepAsync(context, state);

        if (state.Step == AddStep.FooterEditWaitingExpiry)
            return await HandleEditExpiryStepAsync(context, state);

        return false;
    }

    private async Task<bool> HandleTextStepAsync(BotCommandContext context, AddAnnouncementState state)
    {
        var html = context.Message.Text?.Trim();
        if (string.IsNullOrEmpty(html))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.TextRequired, cancellationToken: context.CancellationToken);
            return true;
        }

        state.FooterDraftText = html;
        state.Step = AddStep.FooterWaitingExpiry;
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.ExpiryPrompt, cancellationToken: context.CancellationToken);
        return true;
    }

    private async Task<bool> HandleExpiryStepAsync(BotCommandContext context, AddAnnouncementState state)
    {
        var input = context.Message.Text?.Trim() ?? string.Empty;

        DateTime? expiresAtUtc = null;
        if (!string.Equals(input, "/skip", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParseExact(input, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.ExpiryInvalid, cancellationToken: context.CancellationToken);
                return true;
            }

            var endOfDayMoscow = new DateTime(localDate.Year, localDate.Month, localDate.Day, 23, 59, 59, DateTimeKind.Unspecified);
            expiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(endOfDayMoscow, PostFormatter.Moscow);
        }

        var footerId = context.Footers.Insert(state.FooterDraftText, expiresAtUtc);

        DateTime? displayDate = expiresAtUtc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(expiresAtUtc.Value, PostFormatter.Moscow)
            : null;

        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.Added(footerId, displayDate), cancellationToken: context.CancellationToken);
        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }

    private async Task<bool> HandleEditTextStepAsync(BotCommandContext context, AddAnnouncementState state)
    {
        var html = context.Message.Text?.Trim();
        if (string.IsNullOrEmpty(html))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.TextRequired, cancellationToken: context.CancellationToken);
            return true;
        }

        context.Footers.UpdateText(state.FooterEditId, html);
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.TextUpdated(state.FooterEditId), cancellationToken: context.CancellationToken);
        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }

    private async Task<bool> HandleEditExpiryStepAsync(BotCommandContext context, AddAnnouncementState state)
    {
        var input = context.Message.Text?.Trim() ?? string.Empty;

        DateTime? expiresAtUtc = null;
        if (!string.Equals(input, "/skip", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParseExact(input, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            {
                await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.ExpiryInvalid, cancellationToken: context.CancellationToken);
                return true;
            }

            var endOfDayMoscow = new DateTime(localDate.Year, localDate.Month, localDate.Day, 23, 59, 59, DateTimeKind.Unspecified);
            expiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(endOfDayMoscow, PostFormatter.Moscow);
        }

        context.Footers.UpdateExpiry(state.FooterEditId, expiresAtUtc);

        DateTime? displayDate = expiresAtUtc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(expiresAtUtc.Value, PostFormatter.Moscow)
            : null;

        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.ExpiryUpdated(state.FooterEditId, displayDate), cancellationToken: context.CancellationToken);
        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }
}
