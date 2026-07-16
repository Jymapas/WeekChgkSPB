using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
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
    public async Task Shadow_ShowsCandidateThenKeepsManualFlowWithoutSaving()
    {
        var context = CreateContext(AnnouncementAutomationMode.Shadow);
        var post = CreatePost(101, "Команда — 1900 ₽");
        context.Posts.Insert(post);

        await context.Processor.ProcessAsync(post, Now, CancellationToken.None);

        Assert.Null(context.Announcements.Get(post.Id));
        context.Notifier.Verify(notifier => notifier.NotifyAutomationCandidateAsync(post, It.Is<Announcement>(a => a.Cost == 1900), It.IsAny<CancellationToken>()), Times.Once);
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
        context.Notifier.Verify(notifier => notifier.NotifyAutomationCandidateAsync(
            It.IsAny<Post>(), It.IsAny<Announcement>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var extraction = new FakeExtractionClient();
        var notifier = new Mock<INotifier>();
        var channel = new Mock<IChannelPostUpdater>();
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
            posts,
            announcements,
            channel.Object,
            notifier.Object,
            PostFormatter.Moscow);
        return new ProcessorContext(processor, posts, announcements, extraction, notifier, channel);
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
        FakeExtractionClient Extraction,
        Mock<INotifier> Notifier,
        Mock<IChannelPostUpdater> Channel);

    private sealed class FakeExtractionClient : IAnnouncementExtractionClient
    {
        public int CallCount { get; private set; }

        public Task<AnnouncementExtractionResult> ExtractAsync(
            Post post,
            AnnouncementPreParseResult preParse,
            IReadOnlyList<AnnouncementNameExample> examples,
            DateTime moscowToday,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AnnouncementExtractionResult(true, null, Candidate, 100, 40, 180));
        }
    }
}
