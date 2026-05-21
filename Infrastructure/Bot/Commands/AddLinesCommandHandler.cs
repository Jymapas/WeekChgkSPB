namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class AddLinesCommandHandler : StartAddFlowCommandHandlerBase
{
    public AddLinesCommandHandler()
        : base(BotCommands.AddLines, AddStep.WaitingId, _ => Messages.Add.PromptId)
    {
    }
}
