using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementAutomationProcessorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"weekchgk-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Active_SavesLocallyParsedPriceAndUpdatesChannel()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        var post = CreatePost(100, "Команда до 6 человек — 1800 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        var saved = context.Announcements.Get(post.Id);
        Assert.NotNull(saved);
        Assert.Equal(1800, saved.Cost);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
        context.Notifier.Verify(notifier => notifier.NotifyAutomationSavedAsync(post, It.Is<Announcement>(a => a.Cost == 1800), It.IsAny<CancellationToken>()), Times.Once);
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Shadow_CreatesCompleteReviewDraftWithoutSaving()
    {
        var context = CreateContext(AnnouncementAutomationMode.Shadow);
        var post = CreatePost(101, "Команда — 1900 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(post.Id));
        var draft = context.Drafts.Get(post.Id);
        Assert.NotNull(draft);
        Assert.True(draft!.IsComplete);
        Assert.Equal(1900, draft.Cost);
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(context.Processor.ShouldProcessPost(post.Id, isNewPost: false));
    }

    [Fact]
    public void ExistingPost_IsEligibleOnlyWhenAutomationEnabledAndNoAttemptExists()
    {
        var active = CreateContext(AnnouncementAutomationMode.Active);
        var off = CreateContext(AnnouncementAutomationMode.Off);

        Assert.True(active.Processor.ShouldProcessPost(777, isNewPost: false));
        Assert.True(off.Processor.ShouldProcessPost(777, isNewPost: true));
        Assert.False(off.Processor.ShouldProcessPost(777, isNewPost: false));
    }

    [Fact]
    public async Task AmbiguousCost_FallsBackWithoutCallingApi()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        var post = CreatePost(102, "Команда — 1800 ₽<br>Команда до 6 человек — 2000 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(post.Id));
        Assert.Equal(0, context.Extraction.CallCount);
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApiFailure_WithLocalPrice_CreatesIncompleteReviewDraft()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Extraction.Result = AnnouncementExtractionResult.Failed("api_timeout", 120);
        var post = CreatePost(104, "Команда до 6 человек — 1800 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(post.Id));
        var draft = context.Drafts.Get(post.Id);
        Assert.NotNull(draft);
        Assert.Null(draft!.TournamentName);
        Assert.Equal("Rossi's", draft.Place);
        Assert.Equal(1800, draft.Cost);
        Assert.False(draft.IsComplete);
        context.Notifier.Verify(
            notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MissingLocalPlace_WithPrice_StillCallsApiAndCreatesReview()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        var post = new Post
        {
            Id = 105,
            Link = "https://chgk-spb.livejournal.com/105.html",
            Title = "Кубок знаний 12 июля в 19:30",
            Description =
                "12 июля в 19:30 турнир «Кубок знаний»<br>" +
                "Стоимость турнира<br>Команда до 6 человек — 1800 ₽"
        };
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal(1, context.Extraction.CallCount);
        Assert.Null(context.Announcements.Get(post.Id));
        var draft = context.Drafts.Get(post.Id);
        Assert.NotNull(draft);
        Assert.Equal("Rossi's", draft!.Place);
        Assert.True(draft.IsComplete);
        Assert.Equal("place_not_found", draft.FailureCode);
    }

    [Theory]
    [InlineData("Перенос площадки")]
    [InlineData("Продолжается регистрация")]
    public async Task UpdatePost_FallsBackWithoutCallingApi(string marker)
    {
        var context = CreateContext(AnnouncementAutomationMode.Shadow);
        var post = CreatePost(103, "Команда — 1800 ₽");
        post.Title = $"{marker}: Кубок знаний";
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal(0, context.Extraction.CallCount);
        Assert.Null(context.Announcements.Get(post.Id));
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private ProcessorContext CreateContext(AnnouncementAutomationMode mode)
    {
        var posts = new PostsRepository(_dbPath);
        var announcements = new AnnouncementsRepository(_dbPath);
        var attempts = new AnnouncementParseAttemptsRepository(_dbPath);
        var drafts = new AnnouncementReviewDraftRepository(_dbPath);
        var extraction = new FakeExtractionClient();
        var notifier = new Mock<INotifier>();
        notifier
            .Setup(n => n.NotifyNewPostAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        var channel = new Mock<IChannelPostUpdater>();
        var bot = new Mock<ITelegramBotClient>();
        bot.Setup(b => b.SendRequest<Message>(
                It.IsAny<IRequest<Message>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());
        var reviewHandler = new AnnouncementReviewHandler(
            bot.Object,
            1,
            drafts,
            attempts,
            posts,
            announcements,
            channel.Object,
            new BotConversationState(),
            notifier.Object);
        var options = new AnnouncementAutomationOptions(
            mode, new Uri("https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"), "key",
            AnnouncementAutomationOptions.DefaultModel, TimeSpan.FromSeconds(30));
        var normalizer = new TournamentNameNormalizer();
        var processor = new AnnouncementAutomationProcessor(
            options,
            new AnnouncementPreParser(PostFormatter.Moscow),
            extraction,
            new AnnouncementCandidateValidator(normalizer, PostFormatter.Moscow),
            attempts,
            drafts,
            reviewHandler,
            posts,
            announcements,
            channel.Object,
            notifier.Object,
            PostFormatter.Moscow);
        return new ProcessorContext(processor, posts, announcements, drafts, extraction, notifier, channel);
    }

    private static Post CreatePost(long id, string prices) => new()
    {
        Id = id,
        Link = $"https://chgk-spb.livejournal.com/{id}.html",
        Title = "Кубок знаний 12 июля в 19:30",
        Description = $"12 июля в 19:30 турнир «Кубок знаний» в клубе «Rossi's»<br>Стоимость турнира<br>{prices}<br>Депозит 500 ₽"
    };

    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly AnnouncementExtractionCandidate Candidate = new(
        "Кубок знаний", "Кубок знаний", "Rossi's", "2026-07-12T19:30",
        new AnnouncementExtractionEvidence("Кубок знаний", "Rossi's", "12 июля в 19:30"));

    private sealed record ProcessorContext(
        AnnouncementAutomationProcessor Processor,
        PostsRepository Posts,
        AnnouncementsRepository Announcements,
        AnnouncementReviewDraftRepository Drafts,
        FakeExtractionClient Extraction,
        Mock<INotifier> Notifier,
        Mock<IChannelPostUpdater> Channel);

    private sealed class FakeExtractionClient : IAnnouncementExtractionClient
    {
        public int CallCount { get; private set; }
        public AnnouncementExtractionResult Result { get; set; } =
            new(true, null, Candidate, 100, 40, 180);

        public Task<AnnouncementExtractionResult> ExtractAsync(
            Post post,
            AnnouncementPreParseResult preParse,
            IReadOnlyList<AnnouncementNameExample> examples,
            DateTime moscowToday,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
