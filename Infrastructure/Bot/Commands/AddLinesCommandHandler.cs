namespace WeekChgkSPB.Infrastructure.Bot.Commands;

internal class AddLinesCommandHandler : StartAddFlowCommandHandlerBase
{
    public AddLinesCommandHandler()
        : base(BotCommands.AddLines, AddStep.WaitingId, context => "Отправь ссылку на пост или id в ЖЖ")
    {
    }
}
