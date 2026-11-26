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

    [Theory]
    [InlineData("Off")]
    [InlineData("Shadow")]
    [InlineData("Active")]
    public async Task RssCheck_RefreshesAuthorOfExistingAnnouncementWithoutReparsing(string modeName)
    {
        var mode = Enum.Parse<AnnouncementAutomationMode>(modeName);
        var context = CreateContext(mode);
        var identities = new LiveJournalIdentityRepository(_dbPath);
        context.Posts.Insert(CreatePost(42, "Команда — 1800 ₽"));
        context.Announcements.Insert(CreateAnnouncement(42, "Rossi's", 1800));
        var rssPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(rssPath, """
                <rss version="2.0" xmlns:lj="http://www.livejournal.org/rss/lj/1.0/"><channel>
                <title>Community</title><link>https://chgk-spb.livejournal.com</link><description>Posts</description>
                <item><title>Game</title><link>https://chgk-spb.livejournal.com/42.html</link>
                <description>Body</description><author>author_name</author>
                <lj:poster>author_name</lj:poster><lj:posterid>700</lj:posterid></item></channel></rss>
                """);
            await new RssPoller(new RssFetcher(rssPath), context.Posts,
                context.Announcements, identities, context.Processor).CheckAsync(CancellationToken.None);
            Assert.Equal(LjBindingResult.Linked, identities.Bind(55, "author_name", 1, true));
            Assert.True(context.Announcements.CanManage(42, new AnnouncementActor(55, false, true)));
            Assert.Equal(0, context.Extraction.CallCount);
            Assert.Equal("Кубок знаний", context.Announcements.Get(42)!.TournamentName);
        }
        finally { File.Delete(rssPath); }
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("Shadow")]
    [InlineData("Active")]
    public async Task DeletedAnnouncement_IsNotProcessedAgain(string modeName)
    {
        var mode = Enum.Parse<AnnouncementAutomationMode>(modeName);
        var context = CreateContext(mode);
        var post = CreatePost(42, "Команда — 1800 ₽");
        context.Posts.Insert(post);
        context.Announcements.Insert(CreateAnnouncement(42, "Rossi's", 1800));
        context.Announcements.DeleteAuthorized(42, new AnnouncementActor(1, true, true));

        Assert.False(context.Processor.ShouldProcessPost(42, isNewPost: true));
        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(42));
        Assert.Equal(0, context.Extraction.CallCount);
        context.Channel.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Notifier.Verify(n => n.NotifyNewPostAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletionDuringExtraction_PreventsAutomaticRecreation()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        var post = CreatePost(42, "Команда — 1800 ₽");
        context.Posts.Insert(post);
        context.Extraction.DuringExtraction = () =>
        {
            context.Announcements.Insert(CreateAnnouncement(42, "Rossi's", 1800));
            context.Announcements.DeleteAuthorized(42, new AnnouncementActor(1, true, true));
        };

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal(1, context.Extraction.CallCount);
        Assert.Null(context.Announcements.Get(42));
        context.Channel.Verify(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Notifier.Verify(n => n.NotifyAutomationSavedAsync(It.IsAny<Post>(), It.IsAny<Announcement>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictingTime_RequiresManualDateBeforeSaving(bool active)
    {
        var context = CreateContext(active ? AnnouncementAutomationMode.Active : AnnouncementAutomationMode.Shadow);
        var post = CreatePost(900, "Команда — 1800 ₽");
        post.Description += "<br>Начало регистрации 19:00, начало турнира 20:00";
        context.Posts.Insert(post);
        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);
        var draft = context.Drafts.Get(post.Id);
        Assert.NotNull(draft);
        Assert.Equal("datetime_conflict", draft!.FailureCode);
        Assert.Null(draft.DateTimeUtc);
        Assert.False(draft.IsComplete);
        Assert.Null(context.Announcements.Get(post.Id));
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

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

    [Theory]
    [InlineData("Rossi's", true)]
    [InlineData("Другая площадка", false)]
    public async Task Active_ChecksSameTournamentAndVenueAcrossDifferentLinks(string place, bool needsReview)
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Announcements.Insert(CreateAnnouncement(200, place, 1800));
        var post = CreatePost(201, "Команда — 1800 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal(needsReview, context.Drafts.Get(201)?.FailureCode == "possible_duplicate");
        Assert.Equal(!needsReview, context.Announcements.Exists(201));
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
    public async Task AmbiguousCost_CreatesReviewWithoutCallingApi()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        var post = CreatePost(102, "Команда — 1800 ₽<br>Команда до 6 человек — 2000 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(post.Id));
        Assert.Equal(0, context.Extraction.CallCount);
        var draft = context.Drafts.Get(post.Id);
        Assert.NotNull(draft);
        Assert.Null(draft!.Cost);
        Assert.Equal("cost_ambiguous", draft.FailureCode);
        Assert.NotNull(draft.ReviewMessageId);
        Assert.False(draft.IsComplete);
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

    [Fact]
    public async Task RegistrationUpdate_FallsBackWithoutCallingApi()
    {
        var context = CreateContext(AnnouncementAutomationMode.Shadow);
        var post = CreatePost(103, "Команда — 1800 ₽");
        post.Title = "Продолжается регистрация: Кубок знаний";
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal(0, context.Extraction.CallCount);
        Assert.Null(context.Announcements.Get(post.Id));
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Перенос площадки")]
    [InlineData("Замена площадки")]
    [InlineData("Смена площадки")]
    public async Task Shadow_VenueChange_IsAuditedAndNotApplied(string marker)
    {
        var context = CreateContext(AnnouncementAutomationMode.Shadow);
        var source = CreateAnnouncement(90, "Rossi's", 1800);
        context.Announcements.Insert(source);
        var post = CreateVenueChangePost(103, marker);
        context.Posts.Insert(post);
        context.Extraction.Result = SuccessfulVenueChangeExtraction();

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal("Rossi's", context.Announcements.Get(source.Id)!.Place);
        Assert.Null(context.Announcements.Get(post.Id));
        Assert.Equal(1, context.Extraction.CallCount);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Active_VenueChange_ReplacesFieldsAndRssId()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Announcements.Insert(CreateAnnouncement(90, "Rossi's", 1800));
        var post = CreateVenueChangePost(103, "Перенос площадки");
        context.Posts.Insert(post);
        context.Extraction.Result = SuccessfulVenueChangeExtraction();

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(90));
        var updated = context.Announcements.Get(103);
        Assert.NotNull(updated);
        Assert.Equal("Carrot", updated!.Place);
        Assert.Equal(2100, updated.Cost);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Active_VenueChange_MergesExistingCorrectionDuplicate()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Announcements.Insert(CreateAnnouncement(90, "Rossi's", 1800));
        context.Announcements.Insert(CreateAnnouncement(103, "Carrot", 2100));
        var post = CreateVenueChangePost(103, "Замена площадки");
        context.Posts.Insert(post);
        context.Extraction.Result = SuccessfulVenueChangeExtraction();

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(90));
        Assert.Equal("Carrot", context.Announcements.Get(103)!.Place);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Active_VenueChange_WithAmbiguousMatch_DoesNotChangeAnnouncementsOrNotifyTwice()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Announcements.Insert(CreateAnnouncement(90, "Rossi's", 1800));
        context.Announcements.Insert(CreateAnnouncement(91, "Other", 1800));
        var post = CreateVenueChangePost(103, "Смена площадки");
        context.Posts.Insert(post);
        context.Extraction.Result = SuccessfulVenueChangeExtraction();

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);
        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Equal("Rossi's", context.Announcements.Get(90)!.Place);
        Assert.Equal("Other", context.Announcements.Get(91)!.Place);
        Assert.Null(context.Announcements.Get(103));
        context.Notifier.Verify(notifier => notifier.NotifyNewPostAsync(post, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileVenueChangesAsync_IsIdempotent()
    {
        var context = CreateContext(AnnouncementAutomationMode.Active);
        context.Announcements.Insert(CreateAnnouncement(90, "Rossi's", 1800));
        var post = CreateVenueChangePost(103, "Перенос площадки");
        context.Posts.Insert(post);
        context.Extraction.Result = SuccessfulVenueChangeExtraction();

        await context.Processor.ReconcileVenueChangesAsync(Now, CancellationToken.None);
        await context.Processor.ReconcileVenueChangesAsync(Now, CancellationToken.None);

        Assert.NotNull(context.Announcements.Get(103));
        Assert.Equal(1, context.Extraction.CallCount);
        context.Channel.Verify(update => update.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
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

    private static Post CreateVenueChangePost(long id, string marker) => new()
    {
        Id = id,
        Link = $"https://chgk-spb.livejournal.com/{id}.html",
        Title = $"Внимание! {marker}! Кубок знаний 12 июля в 19:30",
        Description = $"{marker}!<br>12 июля в 19:30 турнир «Кубок знаний» пройдет в кафе «Carrot»<br>Стоимость турнира<br>Команда до 6 человек — 2100 ₽"
    };

    private static Announcement CreateAnnouncement(long id, string place, int cost) => new()
    {
        Id = id,
        TournamentName = "Кубок знаний",
        Place = place,
        DateTimeUtc = new DateTime(2026, 7, 12, 16, 30, 0, DateTimeKind.Utc),
        Cost = cost
    };

    private static AnnouncementExtractionResult SuccessfulVenueChangeExtraction() => new(
        true,
        null,
        new AnnouncementExtractionCandidate(
            "Кубок знаний",
            "Кубок знаний",
            "Carrot",
            "2026-07-12T19:30",
            new AnnouncementExtractionEvidence("Кубок знаний", "Carrot", "12 июля в 19:30")),
        100,
        40,
        180);

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
        public Action? DuringExtraction { get; set; }
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
            DuringExtraction?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
