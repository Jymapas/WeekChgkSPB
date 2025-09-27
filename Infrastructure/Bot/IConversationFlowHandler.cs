using System.Threading.Tasks;

namespace WeekChgkSPB.Infrastructure.Bot;

internal interface IConversationFlowHandler
{
    bool CanHandle(AddStep step);
    Task<bool> HandleAsync(BotCommandContext context, AddAnnouncementState state);
}
