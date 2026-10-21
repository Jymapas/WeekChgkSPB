using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Commands;

public sealed class LiveJournalEditingTests : IClassFixture<SqliteFixture>
{
    private const string Link = "https://chgk-spb.livejournal.com/100.html";
    private readonly AnnouncementsRepository _announcements;
    private readonly PostsRepository _posts;
    private readonly FootersRepository _footers;
    private readonly UserManagementRepository _users;
    private readonly LiveJournalIdentityRepository _identities;
    private readonly BotConversationState _states = new();
    private readonly BotCommandHelper _helper = new(PostFormatter.Moscow);
    private readonly Mock<IChannelPostUpdater> _updater = new();

    public LiveJournalEditingTests(SqliteFixture fixture)
    {
        fixture.Reset();
        _announcements = fixture.CreateAnnouncementsRepository();
        _posts = fixture.CreatePostsRepository();
        _footers = fixture.CreateFootersRepository();
        _users = fixture.CreateUserManagementRepository();
        _identities = new LiveJournalIdentityRepository(fixture.DatabasePath);
        _identities.SaveAuthor(new Post { Id = 100, Link = Link, LjAuthorId = 500, LjAuthorUsername = "author_name" });
        _posts.Insert(new Post { Id = 100, Link = Link });
        _announcements.Insert(new Announcement
        {
            Id = 100, TournamentName = "Old", Place = "Venue", Cost = 100,
            DateTimeUtc = new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        _updater.Setup(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData(BotCommands.LjBind)]
    [InlineData(BotCommands.LjUnbind)]
    [InlineData(BotCommands.LjList)]
    public async Task IdentityCommand_OutsideAdminChat_IsDenied(string command)
    {
        var (context, sent) = Context(command + " 42 author_name --allow", admin: false);
        var handler = new LiveJournalCommandsHandler(_identities);
        Assert.True(handler.CanHandle(context));
        await handler.HandleAsync(context);
        Assert.True(context.AccessDenied);
        Assert.Empty(_identities.List(null));
        Assert.False(_users.IsAllowed(42));
        Assert.Single(sent);
        Assert.Equal(Messages.Bot.AccessDenied, sent[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bind_OnlyExplicitAllowFlagChangesWhitelist(bool allow)
    {
        var (context, sent) = Context("/lj_bind 42 AUTHOR_NAME" + (allow ? " --allow" : ""));
        await new LiveJournalCommandsHandler(_identities).HandleAsync(context);
        var link = Assert.Single(_identities.List(42));
        Assert.Equal(500, link.LjUserId);
        Assert.Equal("author_name", link.LjUsername);
        Assert.Equal(allow, _users.IsAllowed(42));
        Assert.Single(sent);
    }

    [Theory]
    [InlineData("/lj_bind 42 author_name --unknown")]
    [InlineData("/lj_bind 42 author_name --allow unexpected")]
    [InlineData("/lj_bind -42 author_name --allow")]
    [InlineData("/lj_bind 42 https://example.com --allow")]
    public async Task Bind_InvalidArguments_DoesNotGrantAccess(string text)
    {
        var (context, sent) = Context(text);
        await new LiveJournalCommandsHandler(_identities).HandleAsync(context);
        Assert.Empty(_identities.List(null));
        Assert.False(_users.IsAllowed(42));
        Assert.Single(sent);
    }

    [Fact]
    public async Task Bind_BannedAdminActor_IsDeniedEvenWhenHandlerInvokedDirectly()
    {
        _users.BanUser(1);
        var (context, sent) = Context("/lj_bind 42 author_name --allow");
        await new LiveJournalCommandsHandler(_identities).HandleAsync(context);
        Assert.True(context.AccessDenied);
        Assert.False(_users.IsAllowed(42));
        Assert.Empty(_identities.List(null));
        Assert.Equal(Messages.Bot.AccessDenied, Assert.Single(sent));
    }

    [Fact]
    public async Task Bind_BannedUser_DoesNotUnbanOrAllow()
    {
        _users.BanUser(42);
        var (context, sent) = Context("/lj_bind 42 author_name --allow");
        await new LiveJournalCommandsHandler(_identities).HandleAsync(context);
        Assert.True(_users.IsBanned(42));
        Assert.False(_users.IsAllowed(42));
        Assert.Empty(_identities.List(null));
        Assert.Equal(Messages.LiveJournal.BannedUser, Assert.Single(sent));
    }

    [Fact]
    public async Task ListAndUnbind_OperateOnSpecificEditor_KeepOtherEditorsAndWhitelist()
    {
        _identities.Bind(42, "author_name", 1, true);
        _identities.Bind(43, "author_name", 1, true);
        var handler = new LiveJournalCommandsHandler(_identities);
        var (listContext, listed) = Context("/lj_list 42");
        await handler.HandleAsync(listContext);
        Assert.Contains("42 → author_name", Assert.Single(listed));
        Assert.DoesNotContain("43 →", listed[0]);
        var (unbindContext, _) = Context("/lj_unbind 42 author_name");
        await handler.HandleAsync(unbindContext);
        Assert.Empty(_identities.List(42));
        Assert.Single(_identities.List(43));
        Assert.True(_users.IsAllowed(42));
    }

    [Fact]
    public async Task BoundAllowedEditor_CanEditAndDeleteAdminAddedAnnouncement()
    {
        _identities.Bind(42, "author_name", 1, true);
        var (editContext, _) = Context("/edit_name 100 New", admin: false, userId: 42);
        await new EditNameCommandHandler(_updater.Object).HandleAsync(editContext);
        Assert.Equal("New", _announcements.Get(100)!.TournamentName);
        Assert.Null(_announcements.Get(100)!.UserId);
        var (deleteContext, _) = Context("/delete 100", admin: false, userId: 42);
        await new DeleteAnnouncementCommandHandler(_updater.Object).HandleAsync(deleteContext);
        Assert.Null(_announcements.Get(100));
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RevokedBinding_BeforeFlowSave_DeniesAndClearsState()
    {
        _identities.Bind(42, "author_name", 1, true);
        var (begin, _) = Context("/edit_name 100", admin: false, userId: 42);
        await new EditNameCommandHandler(_updater.Object).HandleAsync(begin);
        Assert.True(_states.TryGet(42, out var state));
        _identities.Unbind(42, "author_name");
        var (save, _) = Context("New", admin: false, userId: 42);
        await new EditAnnouncementFlow(_updater.Object).HandleAsync(save, state!);
        Assert.True(save.AccessDenied);
        Assert.Equal("Old", _announcements.Get(100)!.TournamentName);
        Assert.False(_states.TryGet(42, out _));
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flow_DifferentChat_CannotContinueAdminEditing()
    {
        var (begin, _) = Context("/edit_name 100", chatId: 77, userId: 42);
        await new EditNameCommandHandler(_updater.Object).HandleAsync(begin);
        Assert.True(_states.TryGet(42, out var state));
        var (save, sent) = Context("New", admin: false, userId: 42, chatId: 78);
        await new EditAnnouncementFlow(_updater.Object).HandleAsync(save, state!);
        Assert.True(save.AccessDenied);
        Assert.Equal("Old", _announcements.Get(100)!.TournamentName);
        Assert.Equal(Messages.Edit.DifferentChat, Assert.Single(sent));
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flow_DoesNotOverwriteFieldsChangedAfterPrompt()
    {
        var (begin, _) = Context("/edit_name 100");
        await new EditNameCommandHandler(_updater.Object).HandleAsync(begin);
        Assert.True(_states.TryGet(1, out var state));
        var fresh = _announcements.Get(100)!;
        fresh.Place = "Updated by another editor";
        _announcements.Update(fresh);
        var (save, _) = Context("New");
        await new EditAnnouncementFlow(_updater.Object).HandleAsync(save, state!);
        var updated = _announcements.Get(100)!;
        Assert.Equal("New", updated.TournamentName);
        Assert.Equal("Updated by another editor", updated.Place);
    }

    [Fact]
    public async Task UnchangedEdit_DoesNotRefreshChannel()
    {
        var (context, _) = Context("/edit_name 100 Old");
        await new EditNameCommandHandler(_updater.Object).HandleAsync(context);
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private (BotCommandContext Context, List<string> Sent) Context(
        string text, bool admin = true, long userId = 1, long chatId = 1)
    {
        var (context, sent, _) = CommandTestContextFactory.Create(text, _announcements, _posts, _footers,
            _helper, _states, chatId, userId, admin, _users);
        return (context, sent);
    }
}
