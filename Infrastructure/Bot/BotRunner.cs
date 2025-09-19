using System.Collections.Concurrent;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Bot;

public class BotRunner
{
    private readonly long _allowedChatId;
    private readonly AnnouncementsRepository _ann;
    private readonly ITelegramBotClient _bot;
    private readonly PostsRepository _posts;
    private readonly FootersRepository _footers;

    private readonly ConcurrentDictionary<long, AddAnnouncementState> _states = new();

    public BotRunner(ITelegramBotClient bot, long allowedChatId, PostsRepository posts, AnnouncementsRepository ann, FootersRepository footers)
    {
        _bot = bot;
        _allowedChatId = allowedChatId;
        _posts = posts;
        _ann = ann;
        _footers = footers;
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

        if (msg.Text.StartsWith(BotCommands.MakePostLJ, StringComparison.OrdinalIgnoreCase))
        {
            DateTime fromUtc, toUtc;
            var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && TryParseDate(parts[1], out var f) && TryParseDate(parts[2], out var t))
            {
                fromUtc = f;
                toUtc = t;
            }
            else
            {
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow);
                var startLocal = nowLocal.Date;
                var endLocal = startLocal.AddDays(14).AddHours(23).AddMinutes(59);
                fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, PostFormatter.Moscow);
                toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, PostFormatter.Moscow);
            }

            var rows = _ann.GetWithLinksInRange(fromUtc, toUtc);
            if (rows.Count == 0)
            {
                await bot.SendMessage(msg.Chat.Id, "В выбранном диапазоне анонсов нет", cancellationToken: ct);
                return;
            }

            var footerLines = _footers.GetAllTextsDesc();
            var ljHtml = PostFormatter.BuildScheduleHtml(rows, footerLines);
            var codeMsg = PostFormatter.WrapAsCodeForTelegram(ljHtml);

            await bot.SendMessage(
                msg.Chat.Id,
                codeMsg,
                ParseMode.Html,
                linkPreviewOptions: new Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.MakePost, StringComparison.OrdinalIgnoreCase))
        {
            var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            DateTime fromUtc;
            DateTime toUtc;

            if (parts.Length >= 3 && TryParseDate(parts[1], out var f) && TryParseDate(parts[2], out var t))
            {
                fromUtc = f;
                toUtc = t;
            }
            else
            {
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow);
                var startLocal = nowLocal.Date;
                var endLocal = startLocal.AddDays(14).AddHours(23).AddMinutes(59);
                fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, PostFormatter.Moscow);
                toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, PostFormatter.Moscow);
            }

            var rows = _ann.GetWithLinksInRange(fromUtc, toUtc);
            if (rows.Count == 0)
            {
                await bot.SendMessage(msg.Chat.Id, "В выбранном диапазоне анонсов нет", cancellationToken: ct);
                return;
            }

            var footerLines = _footers.GetAllTextsDesc();

            var text = PostFormatter.BuildScheduleMessage(rows, footerLines);

            await bot.SendMessage(
                msg.Chat.Id,
                text,
                ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.Add, StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.IsEdit = false;
            st.Existing = null;
            st.Step = AddStep.WaitingId;
            st.Draft.Id = 0;
            st.Draft.TournamentName = "";
            st.Draft.Place = "";
            st.Draft.DateTimeUtc = DateTime.MinValue;
            st.Draft.Cost = 0;

            await bot.SendMessage(msg.Chat.Id, "Отправь id поста", cancellationToken: ct);
            return;
        }
        if (msg.Text.StartsWith(BotCommands.Edit, StringComparison.OrdinalIgnoreCase))
        {
            var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
            {
                await bot.SendMessage(msg.Chat.Id, "Используй: /edit <id>", cancellationToken: ct);
                return;
            }

            var existing = _ann.Get(id);
            if (existing is null)
            {
                await bot.SendMessage(msg.Chat.Id, "Анонс с таким id не найден", cancellationToken: ct);
                return;
            }

            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.IsEdit = true;
            st.Existing = existing;
            st.Step = AddStep.WaitingName;
            st.Draft.Id = existing.Id;
            st.Draft.TournamentName = existing.TournamentName;
            st.Draft.Place = existing.Place;
            st.Draft.DateTimeUtc = existing.DateTimeUtc;
            st.Draft.Cost = existing.Cost;

            var prompt =
                $"Редактирование анонса {existing.Id}.\nТекущее название: {existing.TournamentName}\nОтправь новое название";
            await bot.SendMessage(msg.Chat.Id, prompt, cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.FooterAdd, StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.IsEdit = false;
            st.Existing = null;
            st.Step = AddStep.FooterWaitingText;
            await bot.SendMessage(msg.Chat.Id, "Отправь одну строку HTML для футера", cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.FooterList, StringComparison.OrdinalIgnoreCase))
        {
            var all = _footers.ListAllDesc();
            if (all.Count == 0)
            {
                await bot.SendMessage(msg.Chat.Id, "Футер пуст", cancellationToken: ct);
                return;
            }

            var lines = all.Select(x => $"{x.Id}: {x.Text}");
            var text = string.Join("\n", lines);
            await bot.SendMessage(msg.Chat.Id, "<code>" + EscapeForCode(text) + "</code>", ParseMode.Html, cancellationToken: ct);
            return;
        }

        if (_states.TryGetValue(msg.From!.Id, out var state) && state.Step != AddStep.None)
        {
            await HandleAddFlow(bot, msg, state, ct);
        }

        if (msg.Text.StartsWith(BotCommands.FooterDel, StringComparison.OrdinalIgnoreCase))
        {
            var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
            {
                await bot.SendMessage(msg.Chat.Id, "Используй: /footer_del <id>", cancellationToken: ct);
                return;
            }
            _footers.Delete(id);
            await bot.SendMessage(msg.Chat.Id, "Удалено", cancellationToken: ct);
            return;
        }
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
                var placePrompt = st.IsEdit && st.Existing is { } existingPlace &&
                                  !string.IsNullOrWhiteSpace(existingPlace.Place)
                    ? $"Место проведения (сейчас: {existingPlace.Place})"
                    : "Место проведения";
                await bot.SendMessage(msg.Chat.Id, placePrompt, cancellationToken: ct);
                break;

            case AddStep.WaitingPlace:
                st.Draft.Place = msg.Text!;
                st.Step = AddStep.WaitingDateTime;
                var dtPrompt = st.IsEdit && st.Existing is { } existingDt
                    ? $"Дата и время (ISO 8601 UTC; пример: 2025-08-10T19:30:00Z; сейчас: {existingDt.DateTimeUtc.ToUniversalTime():O})"
                    : "Дата и время (ISO 8601 UTC; пример: 2025-08-10T19:30:00Z)";
                await bot.SendMessage(msg.Chat.Id, dtPrompt, cancellationToken: ct);
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
                var costPrompt = st.IsEdit && st.Existing is { } existingCost
                    ? $"Стоимость (целое число; сейчас: {existingCost.Cost})"
                    : "Стоимость (целое число)";
                await bot.SendMessage(msg.Chat.Id, costPrompt, cancellationToken: ct);
                break;

            case AddStep.WaitingCost:
                if (!int.TryParse(msg.Text, out var cost))
                {
                    await bot.SendMessage(msg.Chat.Id, "Нужно целое число", cancellationToken: ct);
                    return;
                }

                st.Draft.Cost = cost;

                if (st.IsEdit)
                {
                    _ann.Update(st.Draft);
                    st.Step = AddStep.Done;
                    await bot.SendMessage(msg.Chat.Id, "Обновлено", cancellationToken: ct);
                }
                else
                {
                    _ann.Insert(st.Draft);
                    st.Step = AddStep.Done;

                    await bot.SendMessage(msg.Chat.Id, "Сохранено", cancellationToken: ct);
                }
                _states.TryRemove(msg.From!.Id, out _);
                st.Existing = null;
                st.IsEdit = false;
                break;

            case AddStep.FooterWaitingText:
                {
                    var html = msg.Text!.Trim();
                    var footerId = _footers.Insert(html);
                    await bot.SendMessage(msg.Chat.Id, $"Футер добавлен с id={footerId}", cancellationToken: ct);
                    st.Step = AddStep.Done;
                    _states.TryRemove(msg.From!.Id, out _);
                    break;
                }
        }
    }

    private static bool TryParseDate(string s, out DateTime utc)
    {
        if (DateTime.TryParse(s, null, DateTimeStyles.AssumeLocal, out var dt))
        {
            utc = dt.ToUniversalTime();
            return true;
        }

        utc = default;
        return false;
    }

    private static string EscapeForCode(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
