using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class BotRunner
{
    private readonly long _allowedChatId;
    private readonly ITelegramBotClient _bot;
    private readonly PostsRepository _posts;
    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly BotCommandHelper _helper;
    private readonly BotConversationState _stateStore;
    private readonly IReadOnlyList<IBotCommandHandler> _handlers;
    private readonly IReadOnlyDictionary<AddStep, IReadOnlyList<IConversationFlowHandler>> _flowsByStep;

    public BotRunner(
        ITelegramBotClient bot,
        long allowedChatId,
        PostsRepository posts,
        AnnouncementsRepository announcements,
        FootersRepository footers,
        BotCommandHelper helper,
        BotConversationState stateStore,
        IEnumerable<IBotCommandHandler> handlers,
        IEnumerable<IConversationFlowHandler> flows)
    {
        _bot = bot;
        _allowedChatId = allowedChatId;
        _posts = posts;
        _announcements = announcements;
        _footers = footers;
        _helper = helper;
        _stateStore = stateStore;
        _handlers = handlers.ToList();
        _flowsByStep = SplitFlows(flows);
    }

    public void Start(CancellationToken ct)
    {
        var opts = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
        _bot.StartReceiving(HandleUpdate, HandleError, opts, ct);
    }

    private Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Bot error: {ex.Message}");
        return Task.CompletedTask;
    }

    internal async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null)
        {
            return;
        }

        if (message.Chat.Id != _allowedChatId)
        {
            return;
        }

        if (message.Text is null)
        {
            return;
        }

        var context = new BotCommandContext(_bot, message, ct, _announcements, _posts, _footers, _stateStore, _helper);

        if (await HandleCommandAsync(context))
        {
            return;
        }

        await HandleFlowAsync(context);
    }

    private async Task<bool> HandleCommandAsync(BotCommandContext context)
    {
        var text = context.Message.Text;
        if (text is null || !text.StartsWith('/'))
        {
            return false;
        }

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(context))
            {
                continue;
            }

            await handler.HandleAsync(context);
            return true;
        }

        return false;
    }

    private async Task HandleFlowAsync(BotCommandContext context)
    {
        var from = context.Message.From;
        if (from is null)
        {
            return;
        }

        if (!_stateStore.TryGet(from.Id, out var state) || state is null || state.Step == AddStep.None)
        {
            return;
        }

        if (!_flowsByStep.TryGetValue(state.Step, out var flowsForStep))
        {
            return;
        }

        foreach (var flow in flowsForStep)
        {
            if (await flow.HandleAsync(context, state))
            {
                return;
            }
        }
    }

    internal static IReadOnlyDictionary<AddStep, IReadOnlyList<IConversationFlowHandler>> SplitFlows(IEnumerable<IConversationFlowHandler> flows)
    {
        if (flows is null)
        {
            throw new ArgumentNullException(nameof(flows));
        }

        var grouped = new Dictionary<AddStep, List<IConversationFlowHandler>>();

        foreach (var flow in flows)
        {
            AddIfHandles(flow, AddStep.WaitingId, grouped);
            AddIfHandles(flow, AddStep.WaitingName, grouped);
            AddIfHandles(flow, AddStep.WaitingPlace, grouped);
            AddIfHandles(flow, AddStep.WaitingDateTime, grouped);
            AddIfHandles(flow, AddStep.WaitingCost, grouped);
            AddIfHandles(flow, AddStep.WaitingLines, grouped);
            AddIfHandles(flow, AddStep.EditWaitingName, grouped);
            AddIfHandles(flow, AddStep.EditWaitingPlace, grouped);
            AddIfHandles(flow, AddStep.EditWaitingDateTime, grouped);
            AddIfHandles(flow, AddStep.EditWaitingCost, grouped);
            AddIfHandles(flow, AddStep.FooterWaitingText, grouped);
        }

        var result = new Dictionary<AddStep, IReadOnlyList<IConversationFlowHandler>>(grouped.Count);
        foreach (var (step, list) in grouped)
        {
            result[step] = list;
        }

        return result;
    }

    private static void AddIfHandles(
        IConversationFlowHandler flow,
        AddStep step,
        IDictionary<AddStep, List<IConversationFlowHandler>> target)
    {
        if (!flow.CanHandle(step))
        {
            return;
        }

        if (!target.TryGetValue(step, out var list))
        {
            list = new List<IConversationFlowHandler>();
            target[step] = list;
        }

        list.Add(flow);
    }
}
