namespace WeekChgkSPB;

internal static class Messages
{
    // Shared across multiple handlers
    internal const string AnnouncementNotFound = "Анонс с такой ссылкой не найден";
    internal const string AnnouncementAlreadyExists = "Анонс с такой ссылкой уже есть";
    internal const string LinkRequired = "Нужна ссылка на пост или id в ЖЖ";
    internal const string Saved = "Сохранено";
    internal const string InvalidNumber = "Нужно целое число";
    internal const string NameRequired = "Название не может быть пустым";
    internal const string ModerationUnavailable = "Ошибка: система модерации недоступна";
    internal const string UserBanned = "Вы заблокированы и не можете добавлять анонсы";
    internal const string AnnouncementSentForModeration = "Ваш анонс отправлен на модерацию";

    internal static class Bot
    {
        internal const string BannedUser = "Вы заблокированы и не можете пользоваться ботом";
        internal const string AccessDenied = "Доступ запрещен";
    }

    internal static class Help
    {
        internal const string Text =
            "Доступные команды:\n" +
            "/help - показать эту справку\n" +
            "/cancel - отменить текущее действие\n" +
            "/add - добавить анонс одним сообщением\n" +
            "/add_lines - добавить анонс по шагам\n" +
            "/edit - показать команды редактирования\n" +
            "/edit_name - изменить название\n" +
            "/edit_place - изменить место\n" +
            "/edit_datetime - изменить дату и время\n" +
            "/edit_cost - изменить стоимость\n" +
            "/delete - удалить свой анонс";
    }

    internal static class Cancel
    {
        internal const string Cancelled = "Текущее действие отменено";
    }

    internal static class Add
    {
        internal const string PromptId = "Отправь ссылку на пост или id в ЖЖ";
        internal const string PromptName = "Название турнира";
        internal const string PromptPlace = "Место проведения";
        internal const string PromptDateTime =
            "Дата и время по Москве. Можно отправить ISO (пример: 2025-08-10T19:30) " +
            "или двумя строками: дата (например, 22 сентября) и новой строкой время (например, 19:30)";
        internal const string PromptCost = "Стоимость (число или текст, например: 150, бесплатно, донат)";
        internal const string InvalidDateTime = "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30";
        internal const string ExternalLinkRequired = "Нужна ссылка на пост";
        internal const string LinesPrompt =
            "Отправь 5 или 6 строк: ссылка на пост (или id в ЖЖ), название турнира, место, дата и время по Петербургу " +
            "(можно в формате 2025-08-10T19:30 или двумя строками — например, 22 сентября и 19:30), " +
            "стоимость (число или текст: бесплатно, донат).\n" +
            "Несколько анонсов можно отправить одним сообщением — разделяй блоки пустой строкой.";
        internal const string TooFewLines = "Нужно передать 5 или 6 строк: ссылка или id, название, место, дата и время (одна строка ISO или две строки), стоимость.";
        internal const string TooManyLines = "Ожидаю 5 или 6 строк без дополнительного текста.";
        internal const string FirstLineMustBeLink = "Первая строка — ссылка на пост или id в ЖЖ.";
        internal const string SecondLineMustBeName = "Вторая строка должна содержать название турнира.";
        internal const string DateTimeNotRecognized = "Дата и время не распознаны. Пример: 2025-08-10T19:30 или 22 сентября\\n19:30.";
        internal const string InvalidCost = "Стоимость не может быть пустой.";

        internal static string MultiSavedCount(int saved, int total) => $"Сохранено: {saved} из {total}.";
        internal static string MultiModeratedCount(int moderated, int total) => $"Отправлено на модерацию: {moderated} из {total}.";
        internal static string MultiErrorsHeader(int count) => $"Ошибки ({count}):";
        internal static string MultiErrorLine(string error) => $"  — {error}";
    }

    internal static class Edit
    {
        internal const string NameUpdated = "Название обновлено";
        internal const string PlaceUpdated = "Место обновлено";
        internal const string DateTimeUpdated = "Дата и время обновлены";
        internal const string CostUpdated = "Стоимость обновлена";
        internal const string NoActiveAnnouncement = "Нет активного анонса для редактирования";
        internal const string CannotEditOthers = "Вы можете редактировать только свои анонсы";
        internal const string HelpText = "Используй команды: /edit_name, /edit_place, /edit_datetime, /edit_cost";
        internal const string InvalidDateTime = "Неверный формат. Пример ISO: 2025-08-10T19:30 или двумя строками: 22 сентября и 19:30";
        internal const string InvalidDateTimeShort = "Неверный формат. Пример: 2025-08-10T19:30 (Москва)";
        internal const string NameUsage = "/edit_name <ссылка|id> [новое название]";
        internal const string PlaceUsage = "/edit_place <ссылка|id> [новое место]";
        internal const string DateTimeUsage = "/edit_datetime <ссылка|id> [новая дата и время]";
        internal const string CostUsage = "/edit_cost <ссылка|id> [новая стоимость]";

        internal static string Usage(string pattern) => $"Используй: {pattern}";
        internal static string NamePrompt(long id, string currentName) =>
            $"Редактирование анонса {id}.\nТекущее название: {currentName}\nОтправь новое название";
        internal static string PlacePrompt(long id, string currentPlace) =>
            $"Редактирование анонса {id}.\nТекущее место: {currentPlace}\nОтправь новое место";
        internal static string DateTimePrompt(long id, string currentDateTime) =>
            $"Редактирование анонса {id}.\nТекущая дата и время (Москва): {currentDateTime}\nОтправь новую дату и время по Москве";
        internal static string CostPrompt(long id, int currentCost, string? costLabel = null) =>
            $"Редактирование анонса {id}.\nТекущая стоимость: {costLabel ?? $"{currentCost} р."}\nОтправь новую стоимость (число или текст)";
    }

    internal static class Delete
    {
        internal const string Usage = "Используй: /delete <ссылка|id>";
        internal const string CannotDeleteOthers = "Вы можете удалять только свои анонсы";
        internal static string Deleted(string name, string link) => $"Анонс ({name}) удален: {link}";
    }

    internal static class Footer
    {
        internal const string Prompt = "Отправь одну строку HTML для футера";
        internal const string Empty = "Футер пуст";
        internal const string Deleted = "Удалено";
        internal const string TextRequired = "Нужен непустой текст";
        internal const string UsageError = "Используй: /footer_del <id>";
        internal static string Added(long id) => $"Футер добавлен с id={id}";
    }

    internal static class MakePost
    {
        internal const string NoAnnouncements = "В выбранном диапазоне анонсов нет";
    }

    internal static class Moderation
    {
        internal const string RequestNotFound = "Заявка не найдена";
        internal const string AlreadyProcessed = "Заявка уже обработана";
        internal const string LinkMissing = "Ошибка: отсутствует ссылка";
        internal const string Approved = "Пост одобрен";
        internal const string Rejected = "Пост отклонен";
        internal const string Banned = "Пользователь забанен";
        internal const string Allowed = "Пользователь может постить без модерации";
        internal const string ButtonApprove = "✅ Одобрить пост";
        internal const string ButtonAllow = "✅ Постить без модерации";
        internal const string ButtonReject = "❌ Отклонить пост";
        internal const string ButtonBan = "🚫 Забанить";
        internal const string AdminApproved = "✅ Пост одобрен";
        internal const string AdminAllowed = "✅ Пользователь может постить без модерации";
        internal const string AdminRejected = "❌ Пост отклонен";
        internal const string AdminBanned = "🚫 Пользователь забанен";
        internal const string NewRequest = "Новая заявка на добавление анонса";
        internal const string UserBannedNotification = "Вы были заблокированы и больше не можете добавлять анонсы";

        internal static string UserApproved(string name) => $"Ваш анонс \"{name}\" был одобрен и добавлен";
        internal static string UserAllowed(string name) =>
            $"Ваш анонс \"{name}\" был одобрен и добавлен. Теперь вы можете добавлять анонсы без модерации.";
        internal static string UserRejected(string name) => $"Ваш анонс \"{name}\" был отклонен";
    }

    internal static class Post
    {
        internal const string ScheduleHeader = "Продолжаем вести список синхронов в Санкт-Петербурге.";
        internal const string LjHeader = "Копия поста выкладывается в <a href=\"https://t.me/WeekChgkSPB\" rel=\"nofollow\">Телеграм-канал</a>.";
        internal const string UpdatedAt = "Пост обновлён ";
    }
}
