namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class AddCommandHandler : StartAddFlowCommandHandlerBase
{
    public AddCommandHandler()
        : base(BotCommands.Add, AddStep.WaitingLines, context => context.Helper.AddLinesPrompt)
    {
    }
}
