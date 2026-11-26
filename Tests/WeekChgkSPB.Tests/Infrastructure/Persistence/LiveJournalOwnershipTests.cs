using System;
using Microsoft.Data.Sqlite;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Persistence;

public sealed class LiveJournalOwnershipTests : IClassFixture<SqliteFixture>
{
    private const long EditorId = 55;
    private const long AuthorId = 700;
    private const string AuthorLogin = "game_author";
    private const string SourceLink = "https://chgk-spb.livejournal.com/42.html";
    private readonly SqliteFixture _fixture;

    public LiveJournalOwnershipTests(SqliteFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public void Migration_PreservesLegacyDataAndCanRunRepeatedly()
    {
        Execute("""
            CREATE TABLE posts (id INTEGER PRIMARY KEY, title TEXT, link TEXT, description TEXT);
            CREATE TABLE announcements (
                id INTEGER PRIMARY KEY, tournamentName TEXT NOT NULL, place TEXT,
                dateTimeUtc TEXT NOT NULL, cost INTEGER NOT NULL);
            CREATE TABLE external_posts (announcementId INTEGER PRIMARY KEY, link TEXT NOT NULL UNIQUE);
            INSERT INTO posts VALUES (42, 'Original', 'https://chgk-spb.livejournal.com/42.html', 'Body');
            INSERT INTO announcements VALUES (42, 'Original', 'Place', '2026-11-01T12:00:00.0000000Z', 100);
            """);

        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        _ = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        _ = _fixture.CreateAnnouncementsRepository();

        Assert.Equal("Original", announcements.Get(42)!.TournamentName);
        Assert.Null(announcements.Get(42)!.UserId);
        Assert.NotNull(posts.Get(42));
        Assert.Single(identities.List());
        Assert.True(announcements.CanManage(42, PrivateEditor()));
    }

    [Fact]
    public void Migration_RecanonicalizesAlreadyPopulatedLiveJournalNormalizedLinks()
    {
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        posts.Insert(SourcePost());
        announcements.Insert(Announcement(42));
        var external = Announcement(1);
        announcements.InsertExternal(external, "https://chgk-spb.livejournal.com/43.html");
        Execute("""
            UPDATE posts SET link='https://chgk-spb.livejournal.com/42.html?view=comments',
                normalizedLink='https://chgk-spb.livejournal.com/42.html?view=comments' WHERE id=42;
            UPDATE external_posts SET link='http://chgk-spb.livejournal.com/43.html?view=comments',
                normalizedLink='https://chgk-spb.livejournal.com/43.html?view=comments';
            """);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT normalizedLink FROM external_posts";
            Assert.Equal("https://chgk-spb.livejournal.com/43.html?view=comments", command.ExecuteScalar());
        }

        var migratedPosts = _fixture.CreatePostsRepository();
        var migratedAnnouncements = _fixture.CreateAnnouncementsRepository();

        Assert.True(migratedPosts.TryGetIdByLink(SourceLink, out var postId));
        Assert.Equal(42, postId);
        Assert.Equal(42, migratedAnnouncements.GetByLink(SourceLink)!.Id);
        Assert.Equal(external.Id,
            migratedAnnouncements.GetByLink("https://chgk-spb.livejournal.com/43.html")!.Id);
        Assert.False(migratedAnnouncements.TryInsertAutomatically(
            Announcement(43), "https://chgk-spb.livejournal.com/43.html"));
        Assert.Equal(2, Count("announcements"));
        _ = _fixture.CreatePostsRepository();
        _ = _fixture.CreateAnnouncementsRepository();
        Assert.Equal(2, Count("announcements"));
    }

    [Fact]
    public void SaveAuthor_DoesNotReplaceKnownIdentityWithMissingOrConflictingMetadata()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        Assert.True(identities.SaveAuthor(SourcePost()));
        Assert.False(identities.SaveAuthor(new Post { Id = 42, Link = SourceLink }));
        Assert.False(identities.SaveAuthor(SourcePost(authorId: 999, login: "other_author")));

        Assert.Equal(LjBindingResult.Linked, identities.Bind(EditorId, AuthorLogin, 99, allow: false));
        Assert.Equal(LjBindingResult.UnknownAuthor, identities.Bind(56, "other_author", 99, allow: false));
        Assert.Equal(AuthorId, Assert.Single(identities.List()).LjUserId);
    }

    [Theory]
    [InlineData("https://evil.example/42.html")]
    [InlineData("https://chgk-spb.livejournal.com.evil.example/42.html")]
    [InlineData("https://user@chgk-spb.livejournal.com/42.html")]
    [InlineData("https://chgk-spb.livejournal.com:444/42.html")]
    [InlineData("https://chgk-spb.livejournal.com/43.html")]
    public void SaveAuthor_RejectsUntrustedSourcesAndMismatchedPostId(string link)
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        var post = SourcePost();
        post.Link = link;

        Assert.False(identities.SaveAuthor(post));
        Assert.Equal(LjBindingResult.UnknownAuthor, identities.Bind(EditorId, AuthorLogin, 99, allow: true));
        Assert.False(_fixture.CreateUserManagementRepository().IsAllowed(EditorId));
    }

    [Fact]
    public void Bind_SupportsManyToManyAndRequiresExplicitWhitelistGrant()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        identities.SaveAuthor(SourcePost(43, 701, "second_author"));

        Assert.Equal(LjBindingResult.Linked, identities.Bind(EditorId, " GAME_AUTHOR ", 99, allow: false));
        Assert.Equal(LjBindingResult.Linked, identities.Bind(EditorId, "second_author", 99, allow: false));
        Assert.Equal(LjBindingResult.Linked, identities.Bind(56, AuthorLogin, 99, allow: true));
        identities.Bind(EditorId, AuthorLogin, 99, allow: false);

        Assert.Equal(3, identities.List().Count);
        Assert.Equal(2, identities.List(EditorId).Count);
        var users = _fixture.CreateUserManagementRepository();
        Assert.False(users.IsAllowed(EditorId));
        Assert.True(users.IsAllowed(56));
        var link = Assert.Single(identities.List(56));
        Assert.Equal(99, link.LinkedByUserId);
        Assert.True(link.IsAllowed);
        Assert.False(link.IsBanned);
        Assert.True(identities.Unbind(56, "GAME_AUTHOR"));
        Assert.True(users.IsAllowed(56));
        Assert.Equal(2, new LiveJournalIdentityRepository(_fixture.DatabasePath).List().Count);
    }

    [Fact]
    public void Bind_UnknownAndAmbiguousAuthorsDoNotGrantWhitelistAccess()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        identities.SaveAuthor(SourcePost(43, 701, AuthorLogin));

        Assert.Equal(LjBindingResult.UnknownAuthor, identities.Bind(EditorId, "unknown", 99, allow: true));
        Assert.Equal(LjBindingResult.AmbiguousAuthor, identities.Bind(EditorId, AuthorLogin, 99, allow: true));
        Assert.Empty(identities.List());
        Assert.False(_fixture.CreateUserManagementRepository().IsAllowed(EditorId));
    }

    [Fact]
    public void Bind_DoesNotUnbanUserEvenWithAllowFlag()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        var users = _fixture.CreateUserManagementRepository();
        users.BanUser(EditorId);

        Assert.Equal(LjBindingResult.BannedUser, identities.Bind(EditorId, AuthorLogin, 99, allow: true));
        Assert.True(users.IsBanned(EditorId));
        Assert.False(users.IsAllowed(EditorId));
        Assert.Empty(identities.List());
    }

    [Fact]
    public void Bind_FailureRollsBackExplicitWhitelistGrant()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        Execute("""
            CREATE TRIGGER fail_binding BEFORE INSERT ON telegram_lj_users
            BEGIN SELECT RAISE(ABORT, 'binding unavailable'); END;
            """);

        Assert.Throws<SqliteException>(() => identities.Bind(EditorId, AuthorLogin, 99, allow: true));

        Assert.False(_fixture.CreateUserManagementRepository().IsAllowed(EditorId));
        Assert.Empty(identities.List());
    }

    [Fact]
    public void Unbind_AmbiguousCurrentLoginPreservesBothBindings()
    {
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost(42, AuthorId, "first_author"));
        identities.SaveAuthor(SourcePost(43, 701, "second_author"));
        identities.Bind(EditorId, "first_author", 99, allow: true);
        identities.Bind(EditorId, "second_author", 99, allow: false);
        identities.SaveAuthor(SourcePost(42, AuthorId, "shared_name"));
        identities.SaveAuthor(SourcePost(43, 701, "shared_name"));

        Assert.False(identities.Unbind(EditorId, "shared_name"));

        Assert.Equal(2, identities.List(EditorId).Count);
        Assert.True(_fixture.CreateUserManagementRepository().IsAllowed(EditorId));
        Assert.True(identities.Unbind(EditorId, "first_author"));
        Assert.Equal(701, Assert.Single(identities.List(EditorId)).LjUserId);
    }

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)]
    public void Access_RequiresWhitelistBindingPrivateChatAndNoBan(
        bool allowed, bool linked, bool privateChat, bool banned, bool expected)
    {
        var (announcements, identities) = CreateRssAnnouncement();
        var users = _fixture.CreateUserManagementRepository();
        if (linked) identities.Bind(EditorId, AuthorLogin, 99, allow: allowed);
        else if (allowed) users.AllowUser(EditorId);
        if (banned) users.BanUser(EditorId);

        Assert.Equal(expected, announcements.CanManage(42, new AnnouncementActor(EditorId, false, privateChat)));
        Assert.False(announcements.CanManage(42, new AnnouncementActor(null, false, privateChat)));
    }

    [Fact]
    public void Access_PreservesDirectOwnershipAndAdminPrivilegesButBanTakesPriority()
    {
        var (announcements, _) = CreateRssAnnouncement(owner: EditorId);

        Assert.True(announcements.CanManage(42, PrivateEditor()));
        Assert.True(announcements.CanManage(42, new AnnouncementActor(null, true, false)));
        Assert.False(announcements.CanManage(42, new AnnouncementActor(56, false, true)));
        _fixture.CreateUserManagementRepository().BanUser(EditorId);

        Assert.False(announcements.CanManage(42, PrivateEditor()));
        Assert.False(announcements.CanManage(42, new AnnouncementActor(EditorId, true, false)));
    }

    [Fact]
    public void Access_UsesExternalSourceLinkWhenInternalIdDiffersAndPostRowIsMissing()
    {
        _ = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost(3416195));
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        var announcement = Announcement(1);
        announcements.InsertExternal(announcement, "http://chgk-spb.livejournal.com/3416195.html?utm_source=tg#comments");

        Assert.NotEqual(3416195, announcement.Id);
        Assert.Null(_fixture.CreatePostsRepository().Get(3416195));
        Assert.True(announcements.CanManage(announcement.Id, PrivateEditor()));
    }

    [Fact]
    public void Access_DoesNotMatchUnrelatedAnnouncementByNameOrPlace()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        _fixture.CreatePostsRepository().Insert(new Post
        {
            Id = 43, Link = "https://chgk-spb.livejournal.com/43.html", Title = "Same tournament"
        });
        announcements.Insert(Announcement(43));
        identities.SaveAuthor(SourcePost(43, 701, "other_author"));

        Assert.True(announcements.CanManage(42, PrivateEditor()));
        Assert.False(announcements.CanManage(43, PrivateEditor()));
    }

    [Fact]
    public void SourceCollision_UsesExternalLinkForLookupDisplayAndPermissions()
    {
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        var announcement = Announcement(1);
        const string externalLink = "https://chgk-spb.livejournal.com/43.html";
        announcements.InsertExternal(announcement, externalLink);
        posts.Insert(new Post { Id = announcement.Id, Title = "Unrelated RSS source", Link = SourceLink });

        Assert.Null(announcements.GetByLink(SourceLink));
        Assert.Equal(announcement.Id, announcements.GetByLink(externalLink)!.Id);
        var row = Assert.Single(announcements.GetWithLinksInRange(announcement.DateTimeUtc.AddDays(-1)));
        Assert.Equal(externalLink, row.Link);
        Assert.False(announcements.CanManage(announcement.Id, PrivateEditor()));

        identities.SaveAuthor(SourcePost(43, 701, "other_author"));
        identities.Bind(56, "other_author", 99, allow: true);
        Assert.True(announcements.CanManage(announcement.Id, new AnnouncementActor(56, false, true)));
    }

    [Fact]
    public void Edit_RechecksRevokedBindingBeforeInvokingMutator()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        Assert.True(announcements.CanManage(42, PrivateEditor()));
        identities.Unbind(EditorId, AuthorLogin);
        var invoked = false;

        var result = announcements.EditAuthorized(42, PrivateEditor(), announcement =>
        {
            invoked = true;
            announcement.TournamentName = "Unauthorized";
            return (true, "updated");
        });

        Assert.Equal(AnnouncementMutationStatus.Denied, result.Status);
        Assert.False(invoked);
        Assert.Equal("Tournament", announcements.Get(42)!.TournamentName);
        Assert.Equal(0, Count("announcement_edits"));
    }

    [Fact]
    public void Edit_RechecksRemovedWhitelistAndDisappearedAnnouncement()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        Execute($"DELETE FROM allowed_users WHERE userId={EditorId}");

        Assert.Equal(AnnouncementMutationStatus.Denied,
            announcements.EditAuthorized(42, PrivateEditor(), _ => (true, "updated")).Status);
        announcements.Delete(42);
        Assert.Equal(AnnouncementMutationStatus.NotFound,
            announcements.EditAuthorized(42, PrivateEditor(), _ => (true, "updated")).Status);
        Assert.Equal(0, Count("announcement_edits"));
    }

    [Fact]
    public void Edit_ReadsFreshValuesAndDoesNotOverwriteConcurrentFieldChange()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        var stale = announcements.Get(42)!;
        var newer = announcements.Get(42)!;
        newer.Place = "New place";
        newer.Cost = 250;
        announcements.Update(newer);
        string? observedPlace = null;

        var result = announcements.EditAuthorized(stale.Id, PrivateEditor(), fresh =>
        {
            observedPlace = fresh.Place;
            fresh.TournamentName = "Updated title";
            return (true, "updated");
        });

        Assert.Equal(AnnouncementMutationStatus.Changed, result.Status);
        Assert.Equal("New place", observedPlace);
        var saved = announcements.Get(42)!;
        Assert.Equal("Updated title", saved.TournamentName);
        Assert.Equal("New place", saved.Place);
        Assert.Equal(250, saved.Cost);
        Assert.Null(saved.UserId);
    }

    [Fact]
    public void Edit_RecordsChangedFieldsWithActorAndDoesNotAuditIdenticalValues()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        (bool Success, string Message) ChangeCost(Announcement announcement)
        {
            announcement.Cost = 0;
            announcement.CostLabel = "донат";
            return (true, "updated");
        }

        var changed = announcements.EditAuthorized(42, PrivateEditor(), ChangeCost);
        var unchanged = announcements.EditAuthorized(42, PrivateEditor(), ChangeCost);

        Assert.Equal(AnnouncementMutationStatus.Changed, changed.Status);
        Assert.Equal(AnnouncementMutationStatus.Unchanged, unchanged.Status);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT field, old_value, new_value, edited_by_user_id, edited_at_utc FROM announcement_edits ORDER BY id";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("cost", reader.GetString(0));
        Assert.Equal("100", reader.GetString(1));
        Assert.Equal("0", reader.GetString(2));
        Assert.Equal(EditorId, reader.GetInt64(3));
        Assert.EndsWith("Z", reader.GetString(4));
        Assert.True(reader.Read());
        Assert.Equal("cost_text", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal("донат", reader.GetString(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Edit_InvalidMutationDoesNotPersistPartialValues()
    {
        var (announcements, _) = CreateRssAnnouncement(owner: EditorId);

        var result = announcements.EditAuthorized(42, PrivateEditor(), announcement =>
        {
            announcement.Cost = 999;
            return (false, "invalid input");
        });

        Assert.Equal(AnnouncementMutationStatus.Invalid, result.Status);
        Assert.Equal("invalid input", result.Message);
        Assert.Equal(100, announcements.Get(42)!.Cost);
        Assert.Equal(0, Count("announcement_edits"));
    }

    [Fact]
    public void Edit_AuditFailureRollsBackAnnouncementChange()
    {
        var (announcements, _) = CreateRssAnnouncement(owner: EditorId);
        Execute("""
            CREATE TRIGGER fail_edit_audit BEFORE INSERT ON announcement_edits
            BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;
            """);

        Assert.Throws<SqliteException>(() => announcements.EditAuthorized(42, PrivateEditor(), announcement =>
        {
            announcement.Cost = 999;
            return (true, "updated");
        }));

        Assert.Equal(100, announcements.Get(42)!.Cost);
        Assert.Equal(0, Count("announcement_edits"));
    }

    [Fact]
    public void Delete_MarkerSurvivesRestartAndPostCleanupAndAllowsExplicitManualRestore()
    {
        var (announcements, identities) = CreateRssAnnouncement();
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);

        var result = announcements.DeleteAuthorized(42, PrivateEditor());

        Assert.Equal(AnnouncementMutationStatus.Changed, result.Status);
        Assert.Null(announcements.Get(42));
        Assert.Equal(1, _fixture.CreatePostsRepository().DeleteWithoutAnnouncementsNotInFeed(new long[] { 999 }));
        var restarted = _fixture.CreateAnnouncementsRepository();
        Assert.True(restarted.IsAutomationSuppressed(42));
        Assert.True(restarted.IsAutomationSuppressed(42, "http://chgk-spb.livejournal.com/42.html?utm_source=tg#comments"));
        Assert.False(restarted.IsAutomationSuppressed(43, "https://chgk-spb.livejournal.com/43.html"));
        Assert.Single(new LiveJournalIdentityRepository(_fixture.DatabasePath).List());
        restarted.Insert(Announcement(42));
        Assert.NotNull(restarted.Get(42));
    }

    [Fact]
    public void Delete_UsesActualSourcePostIdForExternalAnnouncement()
    {
        _ = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost(3416195));
        identities.Bind(EditorId, AuthorLogin, 99, allow: true);
        var announcement = Announcement(1);
        announcements.InsertExternal(announcement, "https://chgk-spb.livejournal.com/3416195.html");

        Assert.Equal(AnnouncementMutationStatus.Changed,
            announcements.DeleteAuthorized(announcement.Id, PrivateEditor()).Status);

        Assert.True(announcements.IsAutomationSuppressed(3416195));
        Assert.False(announcements.IsAutomationSuppressed(announcement.Id));
        Assert.Equal(0, Count("external_posts"));
    }

    [Fact]
    public void Delete_DeniedOperationDoesNotRemoveDataOrLeaveSuppressionMarker()
    {
        var (announcements, _) = CreateRssAnnouncement();

        Assert.Equal(AnnouncementMutationStatus.Denied,
            announcements.DeleteAuthorized(42, PrivateEditor()).Status);

        Assert.NotNull(announcements.Get(42));
        Assert.False(announcements.IsAutomationSuppressed(42));
        Assert.Equal(0, Count("announcement_deletions"));
    }

    [Fact]
    public void Delete_MarkerFailureRollsBackAnnouncementAndExternalLinkDeletion()
    {
        _ = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var announcement = Announcement(1, EditorId);
        announcements.InsertExternal(announcement, SourceLink);
        Execute("""
            CREATE TRIGGER fail_deletion_marker BEFORE INSERT ON announcement_deletions
            BEGIN SELECT RAISE(ABORT, 'marker unavailable'); END;
            """);

        Assert.Throws<SqliteException>(() => announcements.DeleteAuthorized(announcement.Id, PrivateEditor()));

        Assert.NotNull(announcements.Get(announcement.Id));
        Assert.NotNull(announcements.GetByLink(SourceLink));
        Assert.Equal(1, Count("external_posts"));
        Assert.False(announcements.IsAutomationSuppressed(42));
    }

    [Fact]
    public void AutomaticInsert_RejectsDeletionMadeAfterProcessingStarted()
    {
        var (announcements, _) = CreateRssAnnouncement(owner: EditorId);
        Assert.False(announcements.IsAutomationSuppressed(42));
        announcements.DeleteAuthorized(42, PrivateEditor());

        Assert.False(announcements.TryInsertAutomatically(Announcement(42), SourceLink));
        Assert.Null(announcements.Get(42));
        Assert.True(announcements.IsAutomationSuppressed(42));
    }

    [Fact]
    public void AutomaticInsert_DoesNotOverwriteExistingIdOrDuplicateExternalSource()
    {
        var (announcements, _) = CreateRssAnnouncement();
        var replacement = Announcement(42);
        replacement.TournamentName = "Replacement";
        Assert.False(announcements.TryInsertAutomatically(replacement, SourceLink));
        Assert.Equal("Tournament", announcements.Get(42)!.TournamentName);

        var external = Announcement(1);
        announcements.InsertExternal(external, "https://chgk-spb.livejournal.com/43.html");
        Assert.False(announcements.TryInsertAutomatically(Announcement(43), "https://chgk-spb.livejournal.com/43.html"));
        Assert.Equal(2, Count("announcements"));
        Assert.True(announcements.TryInsertAutomatically(Announcement(44), "https://chgk-spb.livejournal.com/44.html"));
        Assert.Equal(3, Count("announcements"));
    }

    [Fact]
    public void AutomaticInsert_DeduplicatesExternalLiveJournalLinkWithViewQuery()
    {
        _ = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();
        var external = Announcement(1);
        announcements.InsertExternal(external, SourceLink + "?view=comments");

        Assert.False(announcements.TryInsertAutomatically(Announcement(42), SourceLink));
        Assert.Equal(1, Count("announcements"));
    }

    private (AnnouncementsRepository Announcements, LiveJournalIdentityRepository Identities) CreateRssAnnouncement(long? owner = null)
    {
        _fixture.CreatePostsRepository().Insert(SourcePost());
        var announcements = _fixture.CreateAnnouncementsRepository();
        announcements.Insert(Announcement(42, owner));
        var identities = new LiveJournalIdentityRepository(_fixture.DatabasePath);
        identities.SaveAuthor(SourcePost());
        return (announcements, identities);
    }

    private static AnnouncementActor PrivateEditor() => new(EditorId, false, true);

    private static Post SourcePost(long postId = 42, long authorId = AuthorId, string login = AuthorLogin) => new()
    {
        Id = postId,
        Link = $"https://chgk-spb.livejournal.com/{postId}.html",
        Title = "Source title",
        Description = "Source body",
        LjAuthorId = authorId,
        LjAuthorUsername = login
    };

    private static Announcement Announcement(long id, long? owner = null) => new()
    {
        Id = id,
        TournamentName = "Tournament",
        Place = "Place",
        Cost = 100,
        UserId = owner,
        DateTimeUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        connection.Open();
        return connection;
    }

    private void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Count(string table)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }
}
