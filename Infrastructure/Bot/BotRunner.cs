using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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

    private const string AddLinesPrompt =
        "Отправь 5 или 6 строк: id поста, название турнира, место, дата и время по Петербургу " +
        "(можно в формате 2025-08-10T19:30 или двумя строками — например, 22 сентября и 19:30), " +
        "стоимость (целое число).";

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
            if (parts.Length >= 3 && TryParseDateTime(parts[1], out var f) && TryParseDateTime(parts[2], out var t))
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

            if (parts.Length >= 3 && TryParseDateTime(parts[1], out var f) && TryParseDateTime(parts[2], out var t))
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

        if (msg.Text.StartsWith(BotCommands.AddLines, StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.Existing = null;
            st.Step = AddStep.WaitingId;
            ResetDraft(st);

            await bot.SendMessage(msg.Chat.Id, "Отправь id поста", cancellationToken: ct);
            return;
        }

        if (msg.Text.StartsWith(BotCommands.Add, StringComparison.OrdinalIgnoreCase))
        {
            var st = _states.AddOrUpdate(msg.From!.Id, _ => new AddAnnouncementState(), (_, s) => s);
            st.Existing = null;
            st.Step = AddStep.WaitingLines;
            ResetDraft(st);

            await bot.SendMessage(msg.Chat.Id, AddLinesPrompt, cancellationToken: ct);
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
                {
                    var local = TimeZoneInfo.ConvertTimeFromUtc(existing.DateTimeUtc, PostFormatter.Moscow);
                    return $"Редактирование анонса {existing.Id}.\nТекущая дата и время (Москва): {local:yyyy-MM-dd HH:mm}\nОтправь новую дату и время по Москве";
                },
                (existing, newValue) =>
                {
                    if (!TryParseDateTime(newValue, out var parsedUtc))
                    {
                        return (false, "Неверный формат. Пример: 2025-08-10T19:30 (Москва)");
                    }

                    existing.DateTimeUtc = parsedUtc;
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
                    "Дата и время по Москве. Можно отправить ISO (пример: 2025-08-10T19:30) " +
                    "или двумя строками: дата (например, 22 сентября) и новой строкой время (например, 19:30)",
                    cancellationToken: ct);
                break;

            case AddStep.WaitingDateTime:
                if (!TryParseDateTime(msg.Text, out var utcValue))
                {
                    await bot.SendMessage(msg.Chat.Id,
                        "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30",
                        cancellationToken: ct);
                    return;
                }

                st.Draft.DateTimeUtc = utcValue;
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

            case AddStep.WaitingLines:
                if (await TryProcessAddLines(bot, msg, msg.Text ?? string.Empty, ct))
                {
                    st.Step = AddStep.Done;
                    _states.TryRemove(msg.From!.Id, out _);
                    st.Existing = null;
                    ResetDraft(st);
                }
                else
                {
                    await bot.SendMessage(msg.Chat.Id, AddLinesPrompt, cancellationToken: ct);
                }
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
                        if (!TryParseDateTime(msg.Text, out var parsedUtc))
                        {
                            return (false,
                                "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30");
                        }

                        existing.DateTimeUtc = parsedUtc;
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

    private async Task<bool> TryProcessAddLines(ITelegramBotClient bot, Message msg, string content, CancellationToken ct)
    {
        if (!TryBuildAnnouncementFromLines(content, out var announcement, out var error))
        {
            await bot.SendMessage(msg.Chat.Id, error, cancellationToken: ct);
            return false;
        }

        if (!_posts.Exists(announcement.Id))
        {
            await bot.SendMessage(msg.Chat.Id, "Такого поста нет в базе", cancellationToken: ct);
            return false;
        }

        if (_ann.Exists(announcement.Id))
        {
            await bot.SendMessage(msg.Chat.Id, "Анонс для этого id уже есть", cancellationToken: ct);
            return false;
        }

        _ann.Insert(announcement);
        await bot.SendMessage(msg.Chat.Id, "Сохранено", cancellationToken: ct);
        _states.TryRemove(msg.From!.Id, out _);
        return true;
    }

    private static bool TryBuildAnnouncementFromLines(string content, out Announcement announcement, out string error)
    {
        announcement = default!;

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        var lines = rawLines.Select(static line => line.Trim()).ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count < 5)
        {
            error = "Нужно передать 5 или 6 строк: id, название, место, дата и время (одна строка ISO или две строки), стоимость.";
            return false;
        }

        if (lines.Count > 6)
        {
            error = "Ожидаю 5 или 6 строк без дополнительного текста.";
            return false;
        }

        if (!long.TryParse(lines[0], out var id))
        {
            error = "Первая строка — числовой id.";
            return false;
        }

        var name = lines[1];
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Вторая строка должна содержать название турнира.";
            return false;
        }

        var place = lines[2];

        string dateTimeInput;
        string costLine;

        if (lines.Count == 6)
        {
            dateTimeInput = lines[3] + "\n" + lines[4];
            costLine = lines[5];
        }
        else
        {
            dateTimeInput = lines[3];
            costLine = lines[4];
        }

        if (!TryParseDateTime(dateTimeInput, out var dt))
        {
            error = "Дата и время не распознаны. Пример: 2025-08-10T19:30 или 22 сентября\\n19:30.";
            return false;
        }

        if (!int.TryParse(costLine, out var cost))
        {
            error = "Строка со стоимостью должна содержать целое число.";
            return false;
        }

        announcement = new Announcement
        {
            Id = id,
            TournamentName = name,
            Place = place,
            DateTimeUtc = dt,
            Cost = cost
        };

        error = string.Empty;
        return true;
    }

    private static void ResetDraft(AddAnnouncementState st)
    {
        st.Draft.Id = 0;
        st.Draft.TournamentName = string.Empty;
        st.Draft.Place = string.Empty;
        st.Draft.DateTimeUtc = DateTime.MinValue;
        st.Draft.Cost = 0;
    }

    private static readonly string[] MoscowDateTimeFormats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd"
    };

    private static readonly CultureInfo RuCulture = new("ru-RU");
    private static readonly Regex YearRegex = new("\\d{4}");

    private static bool TryParseDateTime(string? input, out DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            utc = default;
            return false;
        }

        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 2 && TryParseHumanDateTime(parts[0], parts[1], out utc))
        {
            return true;
        }

        if (parts.Length == 1)
        {
            var single = parts[0];

            if (TryParseSingleLineHumanDateTime(single, out utc))
            {
                return true;
            }

            if (DateTimeOffset.TryParse(single, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dtoSingle))
            {
                utc = dtoSingle.UtcDateTime;
                return true;
            }

            if (DateTime.TryParseExact(single, MoscowDateTimeFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var localSingle))
            {
                var unspecifiedLocal = DateTime.SpecifyKind(localSingle, DateTimeKind.Unspecified);
                utc = TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, PostFormatter.Moscow);
                return true;
            }

            if (DateTime.TryParse(single, RuCulture, DateTimeStyles.AllowWhiteSpaces, out var ruLocal))
            {
                var unspecifiedLocal = DateTime.SpecifyKind(ruLocal, DateTimeKind.Unspecified);
                utc = TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, PostFormatter.Moscow);
                return true;
            }
        }

        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }

        if (DateTime.TryParseExact(normalized, MoscowDateTimeFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, PostFormatter.Moscow);
            return true;
        }

        if (DateTime.TryParse(normalized, RuCulture, DateTimeStyles.AllowWhiteSpaces, out var ruLocalFallback))
        {
            var unspecified = DateTime.SpecifyKind(ruLocalFallback, DateTimeKind.Unspecified);
            utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, PostFormatter.Moscow);
            return true;
        }

        utc = default;
        return false;
    }

    private static bool TryParseHumanDateTime(string datePart, string timePart, out DateTime utc)
    {
        if (!TryBuildLocalFromHuman(datePart, timePart, out var local))
        {
            utc = default;
            return false;
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, PostFormatter.Moscow);
        return true;
    }

    private static bool TryParseSingleLineHumanDateTime(string single, out DateTime utc)
    {
        utc = default;

        var pieces = single.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2)
        {
            return false;
        }

        var datePart = string.Join(' ', pieces[..^1]);
        var timePart = pieces[^1];

        if (!TryBuildLocalFromHuman(datePart, timePart, out var local))
        {
            return false;
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, PostFormatter.Moscow);
        return true;
    }

    private static bool TryBuildLocalFromHuman(string datePart, string timePart, out DateTime local)
    {
        local = default;

        if (!TimeSpan.TryParse(timePart, CultureInfo.InvariantCulture, out var timeOfDay))
        {
            return false;
        }

        if (!DateTime.TryParse(datePart, RuCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
        {
            return false;
        }

        local = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day,
            timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds);

        if (!ContainsYear(datePart))
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PostFormatter.Moscow);
            if (local < nowLocal)
            {
                local = local.AddYears(1);
            }
        }

        return true;
    }

    private static bool ContainsYear(string datePart)
    {
        return YearRegex.IsMatch(datePart);
    }

    private static string EscapeForCode(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
