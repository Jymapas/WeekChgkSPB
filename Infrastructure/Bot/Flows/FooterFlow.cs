using System.Threading.Tasks;
using Telegram.Bot;

namespace WeekChgkSPB.Infrastructure.Bot.Flows;

internal class FooterFlow : IConversationFlowHandler
{
    public bool CanHandle(AddStep step)
    {
        return step == AddStep.FooterWaitingText;
    }

    public async Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state)
    {
        if (state.Step != AddStep.FooterWaitingText)
        {
            return false;
        }

        var html = context.Message.Text?.Trim();
        if (string.IsNullOrEmpty(html))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.TextRequired, cancellationToken: context.CancellationToken);
            return true;
        }

        var footerId = context.Footers.Insert(html);
        await context.Bot.SendMessage(context.Message.Chat.Id, Messages.Footer.Added(footerId), cancellationToken: context.CancellationToken);
        state.Step = AddStep.Done;
        context.StateStore.Remove(context.Message.From!.Id);
        return true;
    }
}
