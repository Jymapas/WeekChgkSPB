using System;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot;

public class BotRunner
{
    private readonly long _allowedChatId;
    private readonly ITelegramBotClient _bot;
    private readonly PostsRepository _posts;
    private readonly AnnouncementsRepository _announcements;
    private readonly FootersRepository _footers;
    private readonly BotConversationState _stateStore;
    private readonly BotCommandHelper _helper;
    private readonly IReadOnlyList<IBotCommandHandler> _handlers;
    private readonly ConversationFlowProcessor _flowProcessor;

    public BotRunner(
        ITelegramBotClient bot,
        long allowedChatId,
        PostsRepository posts,
        AnnouncementsRepository announcements,
        FootersRepository footers)
    {
        _bot = bot;
        _allowedChatId = allowedChatId;
        _posts = posts;
        _announcements = announcements;
        _footers = footers;
        _helper = new BotCommandHelper(PostFormatter.Moscow);
        _stateStore = new BotConversationState();
        _handlers = BuildHandlers();
        _flowProcessor = new ConversationFlowProcessor(_helper);
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

        if (await _flowProcessor.TryHandleAsync(context))
        {
            return;
        }
    }

    private IReadOnlyList<IBotCommandHandler> BuildHandlers()
    {
        return new IBotCommandHandler[]
        {
            new MakePostCommandHandler(BotCommands.MakePostLJ, asLiveJournal: true),
            new MakePostCommandHandler(BotCommands.MakePost, asLiveJournal: false),
            new AddLinesCommandHandler(),
            new AddCommandHandler(),
            new EditNameCommandHandler(),
            new EditPlaceCommandHandler(),
            new EditDateTimeCommandHandler(),
            new EditCostCommandHandler(),
            new EditCommandHandler(),
            new DeleteAnnouncementCommandHandler(),
            new FooterAddCommandHandler(),
            new FooterListCommandHandler(),
            new FooterDeleteCommandHandler()
        };
    }
}
