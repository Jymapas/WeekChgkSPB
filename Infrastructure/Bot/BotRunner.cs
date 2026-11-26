using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Infrastructure.Bot.Auditing;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class BotRunner
{
    private readonly BotManualActionAuditor? _manualActionAuditor;
    private readonly long _allowedChatId;
    private readonly ITelegramBotClient _bot;
    private readonly PostsRepository _posts;
    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly UserManagementRepository _userManagement;
    private readonly ModerationHandler _moderationHandler;
    private readonly AnnouncementReviewHandler? _announcementReviewHandler;
    private readonly ForcePostHandler? _forcePostHandler;
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
        UserManagementRepository userManagement,
        ModerationHandler moderationHandler,
        BotCommandHelper helper,
        BotConversationState stateStore,
        IEnumerable<IBotCommandHandler> handlers,
        IEnumerable<IConversationFlowHandler> flows,
        AnnouncementReviewHandler? announcementReviewHandler = null,
        ForcePostHandler? forcePostHandler = null,
        BotManualActionAuditor? manualActionAuditor = null)
    {
        _manualActionAuditor = manualActionAuditor;
        _bot = bot;
        _allowedChatId = allowedChatId;
        _posts = posts;
        _announcements = announcements;
        _footers = footers;
        _userManagement = userManagement;
        _moderationHandler = moderationHandler;
        _announcementReviewHandler = announcementReviewHandler;
        _forcePostHandler = forcePostHandler;
        _helper = helper;
        _stateStore = stateStore;
        _handlers = handlers.ToList();
        _flowsByStep = SplitFlows(flows);
    }

    public void Start(CancellationToken ct)
    {
        var opts = new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] };
        _bot.StartReceiving(HandleUpdate, HandleError, opts, ct);
    }

    private Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Bot error: {ex.Message}");
        return Task.CompletedTask;
    }

    internal async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var action = _manualActionAuditor?.Begin(update);
        Exception? failure = null;
        try
        {
            await HandleUpdateCoreAsync(bot, update, action, ct);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (action is not null)
            {
                try
                {
                    _manualActionAuditor!.Complete(action, failure);
                }
                catch when (failure is not null)
                {
                    // Сохраняем исходное исключение; незавершённая запись остаётся в журнале.
                }
            }
        }
    }

    /// <summary>Передаёт обновление существующим обработчикам команд, ввода и кнопок.</summary>
    private async Task HandleUpdateCoreAsync(ITelegramBotClient bot, Update update, ManualAction? action, CancellationToken ct)
    {
        if (update.CallbackQuery is not null)
        {
            await HandleCallbackQuery(bot, update.CallbackQuery, action, ct);
            return;
        }

        var message = update.Message;
        if (message is null)
        {
            return;
        }

        var from = message.From;
        if (from is not null && _userManagement.IsBanned(from.Id))
        {
            _stateStore.Remove(from.Id);
            await _bot.SendMessage(message.Chat.Id, Messages.Bot.BannedUser, cancellationToken: ct);
            return;
        }

        if (message.Text is null)
        {
            return;
        }

        var isAdminChat = message.Chat.Id == _allowedChatId;
        var context = new BotCommandContext(_bot, message, ct, _announcements, _posts, _footers, _stateStore, _helper, isAdminChat, _userManagement, _moderationHandler);

        if (await HandleCommandAsync(context, action))
        {
            return;
        }

        await HandleFlowAsync(context, action);
    }

    private async Task HandleCallbackQuery(ITelegramBotClient bot, Telegram.Bot.Types.CallbackQuery callbackQuery, ManualAction? action, CancellationToken ct)
    {
        if (callbackQuery.Message is null || callbackQuery.Data is null)
        {
            return;
        }

        if (callbackQuery.Message.Chat.Id != _allowedChatId)
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Bot.AccessDenied, cancellationToken: ct);
            return;
        }

        if (_userManagement.IsBanned(callbackQuery.From.Id))
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, Messages.Bot.AccessDenied, cancellationToken: ct);
            return;
        }

        if (action is not null) action.Handler = nameof(ModerationHandler);
        if (await _moderationHandler.HandleCallbackQuery(callbackQuery, ct))
        {
            if (action is not null) action.Outcome = "handled";
            return;
        }

        if (action is not null) action.Handler = nameof(AnnouncementReviewHandler);
        if (_announcementReviewHandler is not null &&
            await _announcementReviewHandler.HandleCallbackQuery(callbackQuery, ct))
        {
            if (action is not null) action.Outcome = "handled";
            return;
        }

        if (action is not null) action.Handler = nameof(ForcePostHandler);
        if (_forcePostHandler is not null &&
            await _forcePostHandler.HandleCallbackQuery(callbackQuery, ct))
        {
            if (action is not null) action.Outcome = "handled";
            return;
        }

        if (action is not null) action.Handler = null;
        await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
    }

    private async Task<bool> HandleCommandAsync(BotCommandContext context, ManualAction? action)
    {
        var text = context.Message.Text;
        if (text is null || !text.StartsWith('/'))
        {
            return false;
        }

        if (!context.IsAdminChat && BotCommands.AdminOnly.Any(command => _helper.IsCommand(text, command)))
        {
            await DenyAdminActionAsync(context, action);
            return true;
        }

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(context))
            {
                continue;
            }

            if (action is not null) action.Handler = handler.GetType().Name;
            try
            {
                await handler.HandleAsync(context);
            }
            finally
            {
                if (action is not null) action.Denied |= context.AccessDenied;
            }
            if (action is not null)
            {
                action.Outcome = context.AccessDenied ? "denied" : "handled";
            }
            return true;
        }

        return false;
    }

    private async Task HandleFlowAsync(BotCommandContext context, ManualAction? action)
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

        if (!context.IsAdminChat && BotCommands.IsAdminOnlyStep(state.Step))
        {
            await DenyAdminActionAsync(context, action);
            return;
        }

        if (!_flowsByStep.TryGetValue(state.Step, out var flowsForStep))
        {
            return;
        }

        foreach (var flow in flowsForStep)
        {
            if (action is not null) action.Handler = flow.GetType().Name;
            bool handled;
            try
            {
                handled = await flow.HandleAsync(context, state);
            }
            finally
            {
                if (action is not null) action.Denied |= context.AccessDenied;
            }
            if (handled)
            {
                if (action is not null)
                {
                    action.Outcome = context.AccessDenied ? "denied" : "handled";
                }
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
            AddIfHandles(flow, AddStep.FooterWaitingExpiry, grouped);
            AddIfHandles(flow, AddStep.FooterEditWaitingText, grouped);
            AddIfHandles(flow, AddStep.FooterEditWaitingExpiry, grouped);
            AddIfHandles(flow, AddStep.PendingEditWaitingName, grouped);
            AddIfHandles(flow, AddStep.PendingEditWaitingPlace, grouped);
            AddIfHandles(flow, AddStep.PendingEditWaitingDateTime, grouped);
            AddIfHandles(flow, AddStep.PendingEditWaitingCost, grouped);
            AddIfHandles(flow, AddStep.AutomationReviewWaitingName, grouped);
            AddIfHandles(flow, AddStep.AutomationReviewWaitingPlace, grouped);
            AddIfHandles(flow, AddStep.AutomationReviewWaitingDateTime, grouped);
            AddIfHandles(flow, AddStep.AutomationReviewWaitingCost, grouped);
        }

        var result = new Dictionary<AddStep, IReadOnlyList<IConversationFlowHandler>>(grouped.Count);
        foreach (var (step, list) in grouped)
        {
            result[step] = list;
        }

        return result;
    }

    private async Task DenyAdminActionAsync(BotCommandContext context, ManualAction? action)
    {
        context.AccessDenied = true;
        if (action is not null) action.Denied = true;
        if (context.Message.From is { } from) _stateStore.Remove(from.Id);
        await _bot.SendMessage(context.Message.Chat.Id, Messages.Bot.AccessDenied,
            cancellationToken: context.CancellationToken);
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
