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
    private readonly IReadOnlyList<IConversationFlowHandler> _flows;

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
        _flows = flows.ToList();
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

    private async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
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
        var isCommand = message.Text.StartsWith('/');

        if (isCommand)
        {
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(context))
                {
                    continue;
                }

                await handler.HandleAsync(context);
                return;
            }
        }

        if (message.From is null)
        {
            return;
        }

        if (!_stateStore.TryGet(message.From.Id, out var state) || state is null || state.Step == AddStep.None)
        {
            return;
        }

        foreach (var flow in _flows)
        {
            if (!flow.CanHandle(state.Step))
            {
                continue;
            }

            if (await flow.HandleAsync(context, state))
            {
                return;
            }
        }
    }

}
