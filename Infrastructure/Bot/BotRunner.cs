using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
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
        if (msg.Text.StartsWith(BotCommands.EditName, StringComparison.OrdinalIgnoreCase))
        {
            await HandleEditCommand(
                bot,
                msg,
                AddStep.EditWaitingName,
                "/edit_name <id> [новое название]",
                existing =>
                    $"Редактирование анонса {existing.Id}.\nТекущее название: {existing.TournamentName}\nОтправь новое название",
                (existing, newValue) =>
                {
                    if (string.IsNullOrWhiteSpace(newValue))
                    {
                        return (false, "Название не может быть пустым");
                    }

                    existing.TournamentName = newValue;
                    return (true, "Название обновлено");
                },
                ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.EditPlace, StringComparison.OrdinalIgnoreCase))
        {
            await HandleEditCommand(
                bot,
                msg,
                AddStep.EditWaitingPlace,
                "/edit_place <id> [новое место]",
                existing =>
                    $"Редактирование анонса {existing.Id}.\nТекущее место: {existing.Place}\nОтправь новое место",
                (existing, newValue) =>
                {
                    existing.Place = newValue ?? string.Empty;
                    return (true, "Место обновлено");
                },
                ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.EditDateTime, StringComparison.OrdinalIgnoreCase))
        {
            await HandleEditCommand(
                bot,
                msg,
                AddStep.EditWaitingDateTime,
                "/edit_datetime <id> [новая дата и время]",
                existing =>
                    $"Редактирование анонса {existing.Id}.\nТекущая дата и время: {existing.DateTimeUtc:O}\nОтправь новую дату и время в формате ISO 8601 UTC",
                (existing, newValue) =>
                {
                    if (!DateTime.TryParse(newValue, null,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        return (false, "Неверный формат. Пример: 2025-08-10T19:30:00Z");
                    }

                    existing.DateTimeUtc = parsed.ToUniversalTime();
                    return (true, "Дата и время обновлены");
                },
                ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.EditCost, StringComparison.OrdinalIgnoreCase))
        {
            await HandleEditCommand(
                bot,
                msg,
                AddStep.EditWaitingCost,
                "/edit_cost <id> [новая стоимость]",
                existing =>
                    $"Редактирование анонса {existing.Id}.\nТекущая стоимость: {existing.Cost}\nОтправь новую стоимость (целое число)",
                (existing, newValue) =>
                {
                    if (!int.TryParse(newValue, out var cost))
                    {
                        return (false, "Нужно целое число");
                    }

                    existing.Cost = cost;
                    return (true, "Стоимость обновлена");
                },
                ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.Edit, StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Используй команды: /edit_name, /edit_place, /edit_datetime, /edit_cost";
            await bot.SendMessage(msg.Chat.Id, usage, cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.FooterAdd, StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
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
            await HandleStateFlow(bot, msg, state, ct);
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

    private async Task HandleStateFlow(ITelegramBotClient bot, Message msg, AddAnnouncementState st, CancellationToken ct)
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
                if (string.IsNullOrWhiteSpace(msg.Text))
                {
                    await bot.SendMessage(msg.Chat.Id, "Название не может быть пустым", cancellationToken: ct);
                    return;
                }

                st.Draft.TournamentName = msg.Text!.Trim();
                st.Step = AddStep.WaitingPlace;
                await bot.SendMessage(msg.Chat.Id, "Место проведения", cancellationToken: ct);
                break;

            case AddStep.WaitingPlace:
                st.Draft.Place = msg.Text?.Trim() ?? string.Empty;
                st.Step = AddStep.WaitingDateTime;
                await bot.SendMessage(msg.Chat.Id,
                    "Дата и время (ISO 8601 UTC; пример: 2025-08-10T19:30:00Z)", cancellationToken: ct);
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
                st.Existing = null;
                break;

            case AddStep.EditWaitingName:
                await ApplyEditFromState(
                    bot,
                    msg,
                    st,
                    existing =>
                    {
                        if (string.IsNullOrWhiteSpace(msg.Text))
                        {
                            return (false, "Название не может быть пустым");
                        }

                        existing.TournamentName = msg.Text!.Trim();
                        return (true, "Название обновлено");
                    },
                    ct);
                break;

            case AddStep.EditWaitingPlace:
                await ApplyEditFromState(
                    bot,
                    msg,
                    st,
                    existing =>
                    {
                        existing.Place = msg.Text?.Trim() ?? string.Empty;
                        return (true, "Место обновлено");
                    },
                    ct);
                break;

            case AddStep.EditWaitingDateTime:
                await ApplyEditFromState(
                    bot,
                    msg,
                    st,
                    existing =>
                    {
                        if (!DateTime.TryParse(msg.Text, null,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                out var parsed))
                        {
                            return (false, "Неверный формат. Пример: 2025-08-10T19:30:00Z");
                        }

                        existing.DateTimeUtc = parsed.ToUniversalTime();
                        return (true, "Дата и время обновлены");
                    },
                    ct);
                break;

            case AddStep.EditWaitingCost:
                await ApplyEditFromState(
                    bot,
                    msg,
                    st,
                    existing =>
                    {
                        if (!int.TryParse(msg.Text, out var parsedCost))
                        {
                            return (false, "Нужно целое число");
                        }

                        existing.Cost = parsedCost;
                        return (true, "Стоимость обновлена");
                    },
                    ct);
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

    private async Task HandleEditCommand(
        ITelegramBotClient bot,
        Message msg,
        AddStep waitingStep,
        string usage,
        Func<Announcement, string> promptFactory,
        Func<Announcement, string?, (bool Success, string Message)> apply,
        CancellationToken ct)
    {
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
        {
            await bot.SendMessage(msg.Chat.Id, $"Используй: {usage}", cancellationToken: ct);
            return;
        }

        var existing = _ann.Get(id);
        if (existing is null)
        {
            await bot.SendMessage(msg.Chat.Id, "Анонс с таким id не найден", cancellationToken: ct);
            return;
        }

        var hasInlineValue = parts.Length > 2;
        var inlineValue = hasInlineValue ? string.Join(' ', parts.Skip(2)) : null;

        if (hasInlineValue)
        {
            var (success, message) = apply(existing, inlineValue);
            if (!success)
            {
                await bot.SendMessage(msg.Chat.Id, message, cancellationToken: ct);

                var stError = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
                stError.Step = waitingStep;
                stError.Existing = existing;
                await bot.SendMessage(msg.Chat.Id, promptFactory(existing), cancellationToken: ct);
                return;
            }

            _ann.Update(existing);
            await bot.SendMessage(msg.Chat.Id, message, cancellationToken: ct);
            _states.TryRemove(msg.From!.Id, out _);
            return;
        }

        var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
        st.Step = waitingStep;
        st.Existing = existing;

        await bot.SendMessage(msg.Chat.Id, promptFactory(existing), cancellationToken: ct);
    }

    private async Task ApplyEditFromState(
        ITelegramBotClient bot,
        Message msg,
        AddAnnouncementState st,
        Func<Announcement, (bool Success, string Message)> mutator,
        CancellationToken ct)
    {
        if (st.Existing is null)
        {
            st.Step = AddStep.None;
            await bot.SendMessage(msg.Chat.Id, "Нет активного анонса для редактирования", cancellationToken: ct);
            _states.TryRemove(msg.From!.Id, out _);
            return;
        }

        var (success, response) = mutator(st.Existing);
        if (!success)
        {
            await bot.SendMessage(msg.Chat.Id, response, cancellationToken: ct);
            return;
        }

        _ann.Update(st.Existing);
        await bot.SendMessage(msg.Chat.Id, response, cancellationToken: ct);

        st.Step = AddStep.Done;
        _states.TryRemove(msg.From!.Id, out _);
        st.Existing = null;
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
