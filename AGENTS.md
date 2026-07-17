# WeekChgkSPB

## 1. Что это за проект

Консольный `.NET` сервис для ведения и публикации списка синхронов в Санкт-Петербурге.

Приложение:

- читает RSS из ЖЖ,
- сохраняет посты и анонсы в `SQLite`,
- работает как Telegram-бот для админского чата,
- принимает пользовательские заявки на анонсы с модерацией,
- публикует и обновляет сводный пост в Telegram-канале по расписанию.

`AGENTS.md` описывает текущее состояние репозитория. Не нужно записывать сюда желаемую архитектуру, которой еще нет в коде.

## 2. Текущий стек

- `.NET 9`
- `Microsoft.Extensions.Hosting 9.0.0`
- `Telegram.Bot 22.6.0`
- `Microsoft.Data.Sqlite 9.0.7`
- `System.ServiceModel.Syndication 9.0.7`
- `DotNetEnv 3.1.1`
- `xUnit`
- `Moq`
- `Docker Compose`

## 3. Структура репозитория

```text
/
  Domain/
  Infrastructure/
    AnnouncementAutomation/
    Bot/
    Configuration/
    Notifications/
    Persistence/
    Rss/
  Tests/
    WeekChgkSPB.Tests/
  Program.cs
  WeekChgkSPB.csproj
  WeekChgkSPB.sln
  Dockerfile
  docker-compose.yml
```

Это один основной проект `WeekChgkSPB` и один тестовый проект `WeekChgkSPB.Tests`, а не многопроектная layered solution с отдельными csproj на каждый слой.

## 4. Архитектура по слоям

### Domain

Содержит простые модели данных без отдельного слоя application:

- `Announcement`
- `AnnouncementRow`
- `Post`

Ключевые факты:

- `Announcement.DateTimeUtc` хранится в UTC
- `AnnouncementRow` используется как read-model для построения публикаций
- отдельного слоя с use case или value object сейчас в проекте нет

### Infrastructure.Persistence

Содержит SQLite-репозитории и фактическую схему хранения:

- `PostsRepository`
- `AnnouncementsRepository`
- `FootersRepository`
- `ChannelPostsRepository`
- `UserManagementRepository`
- `AnnouncementParseAttemptsRepository`
- `AnnouncementReviewDraftRepository`

Текущие таблицы создаются из кода репозиториев:

- `posts`
- `announcements`
- `external_posts`
- `footers`
- `channel_posts`
- `pending_announcements`
- `allowed_users`
- `banned_users`
- `announcement_parse_attempts`
- `announcement_review_drafts`

Практические детали:

- схема создается и частично мигрируется вручную через `CREATE TABLE IF NOT EXISTS` и `ALTER TABLE`
- `posts` и `external_posts` используют `normalizedLink` для дедупликации и поиска
- `announcements` могут быть связаны либо с RSS-постом по `id`, либо с внешней ссылкой через `external_posts`
- `channel_posts` хранит idempotency публикаций по `scheduled_at_utc` и последний `message_id`

### Infrastructure.Rss

Содержит загрузку и парсинг RSS:

- `RssFetcher`

Ключевые факты:

- RSS читается из `https://chgk-spb.livejournal.com/data/rss`
- `Post.Id` извлекается regex'ом из ссылки вида `12345.html`
- пост без распознанного id получает `Id = 0`

### Infrastructure.AnnouncementAutomation

Содержит безопасную автоматизацию анонсов через Alibaba Qwen:

- `AnnouncementPreParser` локально извлекает полную командную стоимость, дату, время и площадку
- `QwenAnnouncementExtractionClient` отправляет только компактный текст без цены и контактов
- `AnnouncementCandidateValidator` независимо сверяет evidence, дату/время, площадку и нормализацию названия
- `AnnouncementAutomationProcessor` реализует режимы `off`, `shadow`, `active` и ручной fallback
- `AnnouncementParseAttemptsRepository` хранит аудит попыток, token usage и статусы side effects
- `AnnouncementReviewHandler` и `announcement_review_drafts` обеспечивают редактирование и подтверждение RSS-кандидатов в админ-чате

Qwen никогда не определяет стоимость. Неоднозначная или отсутствующая цена запрещает API-вызов и включает прежний ручной flow.

В `shadow` кандидат всегда требует подтверждения кнопкой. В `active` полностью проверенный кандидат сохраняется автоматически, а ошибки API/JSON/валидации при найденной цене создают редактируемый черновик.

### Infrastructure.Notifications

Содержит подготовку и отправку сообщений:

- `TelegramNotifier`
- `PostFormatter`
- `ScheduledPostPublisher`
- `ChannelPostUpdater`
- `ChannelPostScheduleOptions`

Ключевые факты:

- Telegram-посты формируются в HTML
- для дат анонсов используется таймзона `Europe/Moscow` или `Russian Standard Time` на Windows
- scheduler публикует в канал только если слот попал в trigger window и еще не отмечен в `channel_posts`
- `ChannelPostUpdater` нужен для обновления последнего поста канала после ручных изменений

### Infrastructure.Bot

Содержит Telegram runtime и бизнес-логику взаимодействия:

- `BotRunner`
- `BotCommands`
- `BotCommandHelper`
- `BotConversationState`
- `ModerationHandler`
- `Commands/*`
- `Flows/*`

Ключевые факты:

- используется long polling через `StartReceiving`
- бот принимает `Message` и `CallbackQuery`
- команды и flow обрабатываются отдельно
- состояние диалога хранится в памяти в `BotConversationState`, не в БД
- для пользовательских анонсов есть модерация через inline callback-кнопки

### Infrastructure.Configuration

Содержит composition root:

- `AppSettings`
- `ServiceCollectionExtensions`

Именно здесь собираются зависимости, репозитории, bot handlers, flow handlers и scheduler.

### Program

`Program.cs` содержит orchestration приложения:

- загрузку `.env`
- чтение env-переменных
- создание `Host`
- проверку доступа бота к каналу
- запуск polling
- часовой опрос RSS
- минутный цикл проверки scheduled-публикаций

Это консольное приложение без HTTP API и без ASP.NET слоя.

## 5. Что уже реализовано

### RSS и посты

Есть:

- периодический опрос RSS
- сохранение новых RSS-постов в `posts`
- дедуп по `id`
- удаление устаревших `posts`, которых нет в RSS и на которые не ссылаются `announcements`
- при `shadow`/`active` однократная обработка backlog-постов из текущего RSS без анонса и без записи аудита
- посты с фразами `Перенос площадки` и `Продолжается регистрация` не отправляются в Qwen и переходят в ручной fallback

### Анонсы

Поддерживаются:

- ручное добавление анонсов по ссылке
- добавление анонсов для внешних ссылок без RSS-id
- редактирование названия, места, даты/времени и стоимости
- удаление анонсов
- выборка анонсов по диапазону дат для построения поста

### Footer-строки

Поддерживаются:

- добавление footer-строк
- просмотр списка footer-строк
- удаление footer-строк

### Пользовательская модерация

Есть:

- `pending_announcements`
- белый список пользователей `allowed_users`
- бан-лист `banned_users`
- кнопки `approve`, `allow`, `reject`, `ban`
- уведомление пользователя о результате модерации

### Telegram-канал

Есть:

- генерация сводного поста из `announcements + footers`
- публикация в канал по расписанию
- хранение `message_id` последнего опубликованного сообщения
- обновление последнего канального поста после ручных изменений

### Команды бота

По коду есть как минимум:

- `/add`
- `/addlines`
- `/makepost`
- `/makepostlj`
- `/edit`
- `/editname`
- `/editplace`
- `/editdatetime`
- `/editcost`
- `/deleteannouncement`
- `/footeradd`
- `/footerlist`
- `/footerdelete`

Если добавляется новая команда, нужно обновлять `BotCommands`, handler'ы и тесты.

## 6. Правила даты, времени и TZ

Все persisted даты хранятся в UTC.

Ключевые правила:

- в `Announcement` и таблицах БД используется UTC
- для отображения в постах используется московская таймзона из `PostFormatter.Moscow`
- scheduler считает окна публикации в `TimeZoneInfo.Local`
- `TELEGRAM_CHANNEL_POST_TIME` трактуется как локальное серверное время

Это важно: в проекте одновременно используются две TZ-логики:

- локальная серверная TZ для расписания публикации
- московская TZ для форматирования анонсов в тексте поста

При изменениях в датах и времени это нужно проверять отдельно.

## 7. Правила публикации в канал

Настройки берутся из env:

- `TELEGRAM_CHANNEL_ID`
- `TELEGRAM_CHANNEL_POSTS_PER_WEEK`
- `TELEGRAM_CHANNEL_POST_DAYS`
- `TELEGRAM_CHANNEL_POST_TIME`
- `TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES`

Ключевые правила:

- scheduler ищет слоты публикации в текущей локальной неделе
- публикация допускается только если текущее локальное время уже прошло слот
- если с момента слота прошло больше `TriggerWindow`, публикация пропускается
- если слот уже есть в `channel_posts`, повторной отправки быть не должно
- текст публикации берется из анонсов начиная с начала текущего московского дня
- после успешной публикации выполняется cleanup старых анонсов

Это поведение нельзя менять без обновления тестов и ручной проверки.

## 8. Конфигурация и окружение

Обязательные env-переменные для запуска:

- `TELEGRAM_BOT_TOKEN`
- `TELEGRAM_CHAT_ID`

Дополнительно используются:

- `DB_PATH`
- `TELEGRAM_CHANNEL_ID`
- `TELEGRAM_CHANNEL_POSTS_PER_WEEK`
- `TELEGRAM_CHANNEL_POST_DAYS`
- `TELEGRAM_CHANNEL_POST_TIME`
- `TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES`
- `ANNOUNCEMENT_AUTO_PARSE_MODE`
- `QWEN_API_KEY`
- `QWEN_API_BASE_URL`
- `QWEN_MODEL`
- `QWEN_TIMEOUT_SECONDS`

Практические детали:

- `.env` грузится из `AppContext.BaseDirectory`
- если `DB_PATH` не задан, используется `posts.db` рядом с приложением
- если `TELEGRAM_CHANNEL_ID` не задан или невалиден, scheduler канала отключается
- при старте приложение проверяет, что бот имеет права администратора в канале
- без `TELEGRAM_BOT_TOKEN` и `TELEGRAM_CHAT_ID` приложение фактически не стартует рабочий режим
- автоматический разбор по умолчанию выключен; `shadow` и `active` требуют API key и HTTPS endpoint Alibaba International

## 9. Docker и запуск

Текущие операционные файлы:

- `Dockerfile`
- `docker-compose.yml`
- `run.sh`

Практические детали:

- контейнер собирается на `mcr.microsoft.com/dotnet/sdk:9.0` и запускается на `mcr.microsoft.com/dotnet/runtime:9.0`
- runtime-образ настраивает `TZ=Europe/Moscow` и `ru_RU.UTF-8`
- `run.sh` ждет доступности `api.telegram.org` через `ping`, и только потом запускает `dotnet WeekChgkSPB.dll`
- в `docker-compose.yml` база хранится в bind volume `/opt/WeekChgkSPB/data:/data`

## 10. Тесты

Основной тестовый проект:

- `Tests/WeekChgkSPB.Tests/WeekChgkSPB.Tests.csproj`

Сейчас тестами покрыты как минимум:

- bot commands
- conversation flows
- `BotRunner`
- `BotCommandHelper`
- `LinkNormalizer`
- `ChannelPostScheduleOptions`
- `PostFormatter`
- `ChannelPostUpdater`
- `ScheduledPostPublisher`
- SQLite-репозитории

Тесты используют:

- `xUnit`
- `Moq`
- SQLite fixture
- stub/mock для `ITelegramBotClient`

### Текущее состояние тестов

На момент последнего локального прогона:

- `dotnet test` -> `169` прошло, `0` упало

## 11. Что обязательно тестировать при изменениях

### Если меняется `Persistence`

Нужно проверять:

- создание схемы на пустой БД
- совместимость со старой SQLite, где колонок может еще не быть
- backfill `normalizedLink`
- поиск по ссылке и нормализованной ссылке
- корректность удаления `external_posts` сирот после cleanup

### Если меняется `Bot`

Нужно проверять:

- распознавание команд
- happy path и невалидный ввод
- переходы состояний в `BotConversationState`
- доступ только в нужном чате
- callback-кнопки модерации
- очистку состояния после успешного завершения flow

### Если меняется `Notifications`

Нужно проверять:

- форматирование HTML для Telegram
- экранирование через `WrapAsCodeForTelegram`
- сортировку и группировку анонсов по датам
- timezone-конвертацию в текстах
- защиту от повторной публикации слота
- cleanup старых анонсов после публикации
- обновление последнего поста канала

### Если меняется `Program` или конфигурация

Нужно проверять:

- чтение `.env`
- разрешение `DB_PATH`
- запуск без channel scheduler
- запуск с channel scheduler
- проверку прав бота в канале
- поведение при невалидных env-переменных

### Если меняется `RssFetcher` или логика сверки RSS

Нужно проверять:

- парсинг RSS
- извлечение `post id` из ссылки
- отсутствие дублей
- сценарий с новыми постами без анонсов
- сценарий, где анонс уже существует по `id` или по ссылке

### Ручные проверки перед прод-изменениями

Нужно прогонять:

- запуск приложения с реальным `.env`
- проверку команд в админском чате
- сценарий пользовательской модерации
- публикацию в канал
- обновление последнего канального поста после ручного редактирования/добавления/удаления анонса
- работу в Docker, если изменение связано с окружением или стартом

## 12. Команды для локальной работы

```bash
dotnet build WeekChgkSPB.sln
dotnet test Tests/WeekChgkSPB.Tests/WeekChgkSPB.Tests.csproj
docker compose up --build
```

## 13. Правила для будущих изменений

1. Не описывать в `AGENTS.md` несуществующие классы, папки, команды и сценарии как уже реализованные.
2. Не вводить вымышленный `Application`-слой в описания и решения, пока его нет в коде.
3. При изменении схемы SQLite обновлять код и тесты так, чтобы старые БД продолжали открываться.
4. Любые изменения в командах, flow, модерации, scheduler или форматировании поста сопровождать тестами.
5. Любые изменения в логике даты, времени, московской TZ или локальной TZ scheduler'а проверять отдельно.
6. При добавлении новых пользовательских сценариев обновлять `AGENTS.md`, если меняется реальное поведение системы.
