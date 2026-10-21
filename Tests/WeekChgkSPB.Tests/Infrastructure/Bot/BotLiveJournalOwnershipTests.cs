using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Auditing;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public sealed class BotLiveJournalOwnershipTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lj-runner-{Guid.NewGuid():N}.db");
    private readonly Mock<ITelegramBotClient> _bot = new();
    private readonly Mock<IChannelPostUpdater> _updater = new();
    private readonly BotConversationState _states = new();
    private readonly AnnouncementsRepository _announcements;
    private readonly PostsRepository _posts;
    private readonly FootersRepository _footers;
    private readonly UserManagementRepository _users;
    private readonly LiveJournalIdentityRepository _identities;
    private readonly BotRunner _runner;
    private int _updateId;

    public BotLiveJournalOwnershipTests()
    {
        _announcements = new(_path);
        _posts = new(_path);
        _footers = new(_path);
        _users = new(_path);
        _identities = new(_path);
        _bot.Setup(x => x.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());
        _updater.Setup(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _posts.Insert(new Post { Id = 100, Link = "https://chgk-spb.livejournal.com/100.html" });
        _identities.SaveAuthor(new Post { Id = 100, Link = "https://chgk-spb.livejournal.com/100.html", LjAuthorId = 500, LjAuthorUsername = "author_name" });
        _announcements.Insert(new Announcement
        {
            Id = 100, TournamentName = "Original", Place = "Venue", Cost = 100,
            DateTimeUtc = new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        _identities.Bind(42, "author_name", 1, true);
        _runner = CreateRunner(new EditNameCommandHandler(_updater.Object));
    }

    [Fact]
    public async Task AllowedLinkedEditor_InlineEdit_UpdatesAnnouncementAndAuditsActor()
    {
        await SendAsync(_runner, "/edit_name 100 Updated");
        Assert.Equal("Updated", _announcements.Get(100)!.TournamentName);
        Assert.Null(_announcements.Get(100)!.UserId);
        var row = Assert.Single(Read());
        Assert.Equal("handled", row["outcome"]);
        Assert.Equal("42", row["user_id"]);
        Assert.Equal("announcement", row["entity_type"]);
        Assert.Contains("Original", row["before_json"]!);
        Assert.Contains("Updated", row["after_json"]!);
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT edited_by_user_id FROM announcement_edits WHERE announcement_id=100 AND field='tournamentName'";
        Assert.Equal(42L, command.ExecuteScalar());
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokedBinding_DialogInput_IsDeniedWithoutContentOrSnapshots()
    {
        await SendAsync(_runner, "/edit_name 100");
        Assert.True(_states.TryGet(42, out _));
        _identities.Unbind(42, "author_name");
        await SendAsync(_runner, "Private replacement title");
        Assert.Equal("Original", _announcements.Get(100)!.TournamentName);
        Assert.False(_states.TryGet(42, out _));
        var row = Read()[1];
        AssertDeniedWithoutContent(row);
        Assert.DoesNotContain("Private replacement title", string.Join(" ", row.Values));
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PermissionRevokedAfterInitialAuditWrite_DeniedActionRedactsAlreadyStoredSnapshot()
    {
        var runner = CreateRunner(new RevokingEditHandler(_identities, new EditNameCommandHandler(_updater.Object)));
        await SendAsync(runner, "/edit_name 100 Private inline title");
        Assert.Equal("Original", _announcements.Get(100)!.TournamentName);
        AssertDeniedWithoutContent(Assert.Single(Read()));
        _updater.Verify(x => x.UpdateLastPostAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DenialReplyFailure_AfterPermissionRevocation_StillRedactsAudit()
    {
        var runner = CreateRunner(new RevokingEditHandler(_identities, new EditNameCommandHandler(_updater.Object)));
        _bot.Setup(x => x.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Telegram failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(runner, "/edit_name 100 Private inline title"));

        var row = Assert.Single(Read());
        Assert.Equal("error", row["outcome"]);
        Assert.Null(row["input_text"]);
        Assert.Null(row["before_json"]);
        Assert.Null(row["after_json"]);
        Assert.Null(row["source_link"]);
        Assert.Equal("Original", _announcements.Get(100)!.TournamentName);
    }

    [Fact]
    public async Task AdminDialogContinuedInDifferentChat_IsDeniedWithoutSnapshots()
    {
        await SendAsync(_runner, "/edit_name 100", chatId: 1);
        await SendAsync(_runner, "Private title from another chat", chatId: 99);
        Assert.Equal("Original", _announcements.Get(100)!.TournamentName);
        AssertDeniedWithoutContent(Read()[1]);
    }

    [Fact]
    public async Task IdentityCommands_AuditActorInputAndBindingsBeforeAndAfter()
    {
        var runner = CreateRunner(new LiveJournalCommandsHandler(_identities));
        await SendAsync(runner, "/lj_bind 43 author_name --allow", chatId: 1);
        await SendAsync(runner, "/lj_list 43", chatId: 1);
        await SendAsync(runner, "/lj_unbind 43 author_name", chatId: 1);

        var rows = Read();
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal("handled", row["outcome"]);
            Assert.Equal("42", row["user_id"]);
            Assert.Equal("lj_user", row["entity_type"]);
            Assert.Equal("43", row["entity_id"]);
            Assert.Equal(nameof(LiveJournalCommandsHandler), row["handler"]);
        }
        Assert.Equal("/lj_bind 43 author_name --allow", rows[0]["input_text"]);
        Assert.Equal("/lj_list 43", rows[1]["input_text"]);
        Assert.Equal("/lj_unbind 43 author_name", rows[2]["input_text"]);

        using var bindBefore = JsonDocument.Parse(rows[0]["before_json"]!);
        using var bindAfter = JsonDocument.Parse(rows[0]["after_json"]!);
        Assert.Equal(0, bindBefore.RootElement.GetProperty("entity").GetArrayLength());
        var savedLink = Assert.Single(bindAfter.RootElement.GetProperty("entity").EnumerateArray());
        Assert.Equal(43L, savedLink.GetProperty("TelegramUserId").GetInt64());
        Assert.Equal(500L, savedLink.GetProperty("LjUserId").GetInt64());
        Assert.Equal(42L, savedLink.GetProperty("LinkedByUserId").GetInt64());
        Assert.True(savedLink.GetProperty("IsAllowed").GetBoolean());
        using var listBefore = JsonDocument.Parse(rows[1]["before_json"]!);
        using var listAfter = JsonDocument.Parse(rows[1]["after_json"]!);
        Assert.Equal(1, listBefore.RootElement.GetProperty("entity").GetArrayLength());
        Assert.Equal(1, listAfter.RootElement.GetProperty("entity").GetArrayLength());
        using var unbindBefore = JsonDocument.Parse(rows[2]["before_json"]!);
        using var unbindAfter = JsonDocument.Parse(rows[2]["after_json"]!);
        Assert.Equal(1, unbindBefore.RootElement.GetProperty("entity").GetArrayLength());
        Assert.Equal(0, unbindAfter.RootElement.GetProperty("entity").GetArrayLength());
        Assert.True(_users.IsAllowed(43));
    }

    [Theory]
    [InlineData("/lj_bind 43 private_login --allow")]
    [InlineData("/lj_unbind 43 author_name")]
    [InlineData("/lj_list 43")]
    public async Task IdentityCommand_OutsideAdminChat_AuditsOnlyMetadata(string text)
    {
        _identities.Bind(43, "author_name", 1, true);
        var runner = CreateRunner(new LiveJournalCommandsHandler(_identities));
        await SendAsync(runner, text);
        AssertDeniedWithoutContent(Assert.Single(Read()));
        Assert.Single(_identities.List(43));
    }

    private BotRunner CreateRunner(IBotCommandHandler edit)
    {
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        var moderation = new ModerationHandler(_bot.Object, _announcements, _users, _posts,
            _updater.Object, _footers, 1, _states);
        var auditor = new BotManualActionAuditor(new(_path), _states, _announcements, _posts,
            _footers, _users, new AnnouncementReviewDraftRepository(_path), helper, 1, _identities);
        return new BotRunner(_bot.Object, 1, _posts, _announcements, _footers, _users, moderation,
            helper, _states, new[] { edit }, new IConversationFlowHandler[] { new EditAnnouncementFlow(_updater.Object) },
            manualActionAuditor: auditor);
    }

    private Task SendAsync(BotRunner runner, string text, long chatId = 99) => runner.HandleUpdate(_bot.Object,
        new Update { Id = ++_updateId, Message = new Message
        {
            Id = _updateId, Text = text, Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = 42 }
        } }, CancellationToken.None);

    private List<Dictionary<string, string?>> Read()
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM manual_actions ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, string?>>();
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i).ToString()));
        return rows;
    }

    private static void AssertDeniedWithoutContent(Dictionary<string, string?> row)
    {
        Assert.Equal("denied", row["outcome"]);
        Assert.Null(row["input_text"]);
        Assert.Null(row["before_json"]);
        Assert.Null(row["after_json"]);
        Assert.Equal("42", row["user_id"]);
    }

    private sealed class RevokingEditHandler(LiveJournalIdentityRepository identities, IBotCommandHandler inner) : IBotCommandHandler
    {
        public bool CanHandle(BotCommandContext context) => inner.CanHandle(context);
        public Task HandleAsync(BotCommandContext context)
        {
            identities.Unbind(42, "author_name");
            return inner.HandleAsync(context);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
