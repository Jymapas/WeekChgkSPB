using System.Threading.Tasks;

namespace WeekChgkSPB.Infrastructure.Bot;

internal interface IBotCommandHandler
{
    bool CanHandle(BotCommandContext context);
    Task HandleAsync(BotCommandContext context);
}
