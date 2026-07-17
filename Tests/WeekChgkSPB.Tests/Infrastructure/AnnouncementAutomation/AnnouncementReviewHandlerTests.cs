using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementReviewHandlerTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"weekchgk-review-handler-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EnsureNotification_HidesAddButtonUntilDraftIsComplete()
    {
        var context = CreateContext();
        var post = CreatePost(42);
        context.Posts.Insert(post);
        var draft = new AnnouncementReviewDraft
        {
            PostId = post.Id,
            Place = "Rossi's",
            Cost = 1800,
            FailureCode = "json_invalid"
        };
        context.Drafts.Upsert(draft);

        await context.Handler.EnsureNotificationAsync(post, draft, CancellationToken.None);

        Assert.Single(context.SentRequests);
        var keyboard = GetKeyboard(context.SentRequests[0]);
        var callbackData = keyboard.InlineKeyboard
            .SelectMany(row => row)
            .Select(button => button.CallbackData)
            .ToList();
        Assert.DoesNotContain($"autoreview_add_{post.Id}", callbackData);
        Assert.Contains($"autoreview_skip_{post.Id}", callbackData);
        var stored = context.Drafts.Get(post.Id);
        Assert.Equal(10, stored!.SourceMessageId);
        Assert.Equal(0, stored.ReviewMessageId);
    }

    [Fact]
    public async Task AddCallback_SavesOnceAndIsIdempotent()
    {
        var context = CreateContext();
        var post = CreatePost(43);
        context.Posts.Insert(post);
        context.Drafts.Upsert(new AnnouncementReviewDraft
        {
            PostId = post.Id,
            TournamentName = "Кубок знаний",
            Place = "Rossi's",
            DateTimeUtc = new DateTime(2026, 7, 20, 16, 0, 0, DateTimeKind.Utc),
            Cost = 1900
        });
        var callback = CreateCallback($"autoreview_add_{post.Id}");

        Assert.True(await context.Handler.HandleCallbackQuery(callback, CancellationToken.None));
        Assert.True(await context.Handler.HandleCallbackQuery(callback, CancellationToken.None));

        var saved = context.Announcements.Get(post.Id);
        Assert.NotNull(saved);
        Assert.Equal(1900, saved!.Cost);
        Assert.Equal(
            AnnouncementReviewStatuses.Added,
            context.Drafts.Get(post.Id)!.Status);
        context.Channel.Verify(
            updater => updater.UpdateLastPostAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EditFlow_CompletesDraftAndRefreshesReviewMessage()
    {
        var context = CreateContext();
        var post = CreatePost(44);
        context.Posts.Insert(post);
        context.Drafts.Upsert(new AnnouncementReviewDraft
        {
            PostId = post.Id,
            Place = "Rossi's",
            DateTimeUtc = new DateTime(2026, 7, 20, 16, 0, 0, DateTimeKind.Utc),
            Cost = 1900,
            FailureCode = "json_invalid"
        });

        await context.Handler.HandleCallbackQuery(
            CreateCallback($"autoreviewedit_name_{post.Id}"),
            CancellationToken.None);
        Assert.True(context.StateStore.TryGet(1000, out var state));
        Assert.Equal(AddStep.AutomationReviewWaitingName, state!.Step);

        var flow = new AutomationReviewEditFlow(context.Drafts, context.Handler);
        var botContext = FlowTestContextFactory.CreateContext(
            context.Bot,
            "Исправленное название",
            1,
            1000,
            context.Announcements,
            context.Posts,
            new FootersRepository(_dbPath),
            context.StateStore,
            new BotCommandHelper(PostFormatter.Moscow));

        Assert.True(await flow.HandleAsync(botContext, state));

        var stored = context.Drafts.Get(post.Id);
        Assert.Equal("Исправленное название", stored!.TournamentName);
        Assert.True(stored.IsComplete);
        Assert.False(context.StateStore.TryGet(1000, out _));
    }

    private ReviewContext CreateContext()
    {
        var posts = new PostsRepository(_dbPath);
        var announcements = new AnnouncementsRepository(_dbPath);
        var drafts = new AnnouncementReviewDraftRepository(_dbPath);
        var attempts = new AnnouncementParseAttemptsRepository(_dbPath);
        var notifier = new Mock<INotifier>();
        notifier
            .Setup(n => n.NotifyNewPostAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        var channel = new Mock<IChannelPostUpdater>();
        channel
            .Setup(updater => updater.UpdateLastPostAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sentRequests = new List<IRequest<Message>>();
        var bot = new Mock<ITelegramBotClient>();
        bot.Setup(client => client.SendRequest<Message>(
                It.IsAny<IRequest<Message>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IRequest<Message> request, CancellationToken _) =>
            {
                sentRequests.Add(request);
                return new Message();
            });
        bot.Setup(client => client.SendRequest<bool>(
                It.IsAny<IRequest<bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var stateStore = new BotConversationState();
        var handler = new AnnouncementReviewHandler(
            bot.Object,
            1,
            drafts,
            attempts,
            posts,
            announcements,
            channel.Object,
            stateStore,
            notifier.Object);
        return new ReviewContext(
            handler,
            posts,
            announcements,
            drafts,
            channel,
            sentRequests,
            bot.Object,
            stateStore);
    }

    private static InlineKeyboardMarkup GetKeyboard(IRequest<Message> request) =>
        Assert.IsType<InlineKeyboardMarkup>(
            request.GetType().GetProperty("ReplyMarkup")?.GetValue(request));

    private static Post CreatePost(long id) => new()
    {
        Id = id,
        Title = "Кубок знаний",
        Description = "Описание",
        Link = $"https://chgk-spb.livejournal.com/{id}.html"
    };

    private static CallbackQuery CreateCallback(string data)
    {
        var payload = new
        {
            id = "callback-1",
            from = new { id = 1000, is_bot = false, first_name = "Admin" },
            data,
            message = new
            {
                message_id = 99,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                chat = new { id = 1, type = "private" }
            }
        };
        return JsonSerializer.Deserialize<CallbackQuery>(
            JsonSerializer.Serialize(payload),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private sealed record ReviewContext(
        AnnouncementReviewHandler Handler,
        PostsRepository Posts,
        AnnouncementsRepository Announcements,
        AnnouncementReviewDraftRepository Drafts,
        Mock<IChannelPostUpdater> Channel,
        List<IRequest<Message>> SentRequests,
        ITelegramBotClient Bot,
        BotConversationState StateStore);
}
