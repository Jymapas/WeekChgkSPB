using System.Collections.Concurrent;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WeekChgkSPB.Infrastructure.Bot;

public class BotRunner
{
    private readonly long _allowedChatId;
    private readonly AnnouncementsRepository _ann;
    private readonly ITelegramBotClient _bot;
    private readonly PostsRepository _posts;

    private readonly ConcurrentDictionary<long, AddAnnouncementState> _states = new();

    public BotRunner(ITelegramBotClient bot, long allowedChatId, PostsRepository posts, AnnouncementsRepository ann)
    {
        _bot = bot;
        _allowedChatId = allowedChatId;
        _posts = posts;
        _ann = ann;
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
        var msg = update.Message;
        if (msg is null) return;
        if (msg.Chat.Id != _allowedChatId) return;
        if (msg.Text is null) return;

        if (msg.Text.StartsWith("/add", StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.Step = AddStep.WaitingId;
            st.Draft.TournamentName = "";
            st.Draft.Place = "";
            st.Draft.DateTimeUtc = DateTime.MinValue;
            st.Draft.Cost = 0;

            await bot.SendMessage(msg.Chat.Id, "Отправь id поста", cancellationToken: ct);
            return;
        }

        if (_states.TryGetValue(msg.From!.Id, out var state) && state.Step != AddStep.None)
            await HandleAddFlow(bot, msg, state, ct);
    }

    private async Task HandleAddFlow(ITelegramBotClient bot, Message msg, AddAnnouncementState st, CancellationToken ct)
    {
        switch (st.Step)
        {
            case AddStep.WaitingId:
                if (!long.TryParse(msg.Text, out var id))
                {
                    await bot.SendMessage(msg.Chat.Id, "Нужен числовой id", cancellationToken: ct);
                    return;
                }

                if (!_posts.Exists(id))
                {
                    await bot.SendMessage(msg.Chat.Id, "Такого поста нет в базе", cancellationToken: ct);
                    return;
                }

                if (_ann.Exists(id))
                {
                    await bot.SendMessage(msg.Chat.Id, "Анонс для этого id уже есть", cancellationToken: ct);
                    st.Step = AddStep.None;
                    return;
                }

                st.Draft.Id = id;
                st.Step = AddStep.WaitingName;
                await bot.SendMessage(msg.Chat.Id, "Название турнира", cancellationToken: ct);
                break;

            case AddStep.WaitingName:
                st.Draft.TournamentName = msg.Text!;
                st.Step = AddStep.WaitingPlace;
                await bot.SendMessage(msg.Chat.Id, "Место проведения", cancellationToken: ct);
                break;

            case AddStep.WaitingPlace:
                st.Draft.Place = msg.Text!;
                st.Step = AddStep.WaitingDateTime;
                await bot.SendMessage(msg.Chat.Id, "Дата и время (ISO 8601 UTC; пример: 2025-08-10T19:30:00Z)",
                    cancellationToken: ct);
                break;

            case AddStep.WaitingDateTime:
                if (!DateTime.TryParse(msg.Text, null,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                {
                    await bot.SendMessage(msg.Chat.Id, "Неверный формат. Пример: 2025-08-10T19:30:00Z",
                        cancellationToken: ct);
                    return;
                }

                st.Draft.DateTimeUtc = dt.ToUniversalTime();
                st.Step = AddStep.WaitingCost;
                await bot.SendMessage(msg.Chat.Id, "Стоимость (целое число)", cancellationToken: ct);
                break;

            case AddStep.WaitingCost:
                if (!int.TryParse(msg.Text, out var cost))
                {
                    await bot.SendMessage(msg.Chat.Id, "Нужно целое число", cancellationToken: ct);
                    return;
                }

                st.Draft.Cost = cost;

                _ann.Insert(st.Draft);
                st.Step = AddStep.Done;

                await bot.SendMessage(msg.Chat.Id, "Сохранено", cancellationToken: ct);
                _states.TryRemove(msg.From!.Id, out _);
                break;
        }
    }
}