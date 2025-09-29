namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class AddLinesCommandHandler : StartAddFlowCommandHandlerBase
{
    public AddLinesCommandHandler()
        : base(BotCommands.AddLines, AddStep.WaitingId, context => "Отправь id поста")
    {
    }
}
