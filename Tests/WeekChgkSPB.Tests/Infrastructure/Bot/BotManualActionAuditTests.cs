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
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Auditing;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public sealed class BotManualActionAuditTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"manual-actions-{Guid.NewGuid():N}.db");
    private readonly Mock<ITelegramBotClient> _bot = new();
    private readonly BotConversationState _states = new();
    private readonly AnnouncementsRepository _announcements;
    private readonly PostsRepository _posts;
    private readonly FootersRepository _footers;
    private readonly UserManagementRepository _users;
    private readonly AnnouncementReviewDraftRepository _drafts;
    private readonly BotManualActionAuditor _auditor;
    private readonly BotRunner _runner;
    private int _updateId;

    public BotManualActionAuditTests()
    {
        _announcements = new(_path);
        _posts = new(_path);
        _footers = new(_path);
        _users = new(_path);
        _drafts = new(_path);
        var helper = new BotCommandHelper(PostFormatter.Moscow);
        _auditor = new(new(_path), _states, _announcements, _posts, _footers, _users, _drafts, helper, 1);
        _bot.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());
        _bot.Setup(b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var updater = new Mock<IChannelPostUpdater>();
        updater.Setup(u => u.UpdateLastPostAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var moderation = new ModerationHandler(_bot.Object, _announcements, _users, _posts,
            updater.Object, _footers, 1, _states);
        var review = new AnnouncementReviewHandler(_bot.Object, 1, _drafts, new(_path), _posts,
            _announcements, updater.Object, _states, new Mock<INotifier>().Object);
        _runner = new(_bot.Object, 1, _posts, _announcements, _footers, _users, moderation, helper, _states,
            new IBotCommandHandler[]
            {
                new AddCommandHandler(), new AddLinesCommandHandler(), new CancelCommandHandler(),
                new EditNameCommandHandler(updater.Object), new DeleteAnnouncementCommandHandler(updater.Object),
                new FooterAddCommandHandler(), new FooterDeleteCommandHandler(),
                new FooterListCommandHandler(), new FooterEditCommandHandler(),
                new MakePostCommandHandler(BotCommands.MakePost, false),
                new MakePostCommandHandler(BotCommands.MakePostLJ, true)
            },
            new IConversationFlowHandler[] { new AddAnnouncementFlow(updater.Object), new FooterFlow(), new AutomationReviewEditFlow(_drafts, review) },
            review, new ForcePostHandler(_announcements, _footers, new ChannelPostsRepository(_path), _bot.Object, null),
            manualActionAuditor: _auditor);
    }

    [Fact]
    public async Task FailedInitialAuditWrite_DoesNotStartBusinessAction()
    {
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE manual_actions";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => SendAsync("/add_lines"));
        Assert.False(_states.TryGet(10, out _));
        _bot.Verify(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()), Times.Never);
        _ = new ManualActionsRepository(_path);
        await SendAsync("/add_lines");
        Assert.Single(Read());
    }

    [Fact]
    public void AddingAuditSchema_PreservesExistingBusinessData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"old-manual-audit-{Guid.NewGuid():N}.db");
        try
        {
            var announcements = new AnnouncementsRepository(path);
            announcements.Insert(new Announcement
            {
                Id = 42, TournamentName = "Кубок", Place = "Соус", Cost = 1800, DateTimeUtc = DateTime.UtcNow
            });
            _ = new ManualActionsRepository(path);
            _ = new ManualActionsRepository(path);
            Assert.Equal("Кубок", announcements.Get(42)!.TournamentName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailedReplyAfterSaving_RecordsErrorAndActualSavedValues()
    {
        _posts.Insert(new Post { Id = 42, Title = "Кубок", Link = "https://chgk-spb.livejournal.com/42.html", Description = "Текст" });
        _announcements.Insert(new Announcement { Id = 42, TournamentName = "Кубок", Place = "Соус", Cost = 1800, DateTimeUtc = DateTime.UtcNow });
        _bot.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync("/edit_name 42 Кубок-2"));
        var row = Assert.Single(Read());
        Assert.Equal("error", row["outcome"]);
        using var after = JsonDocument.Parse(row["after_json"]!);
        Assert.Equal("Кубок-2", after.RootElement.GetProperty("entity").GetProperty("TournamentName").GetString());
        Assert.Equal("Кубок-2", _announcements.Get(42)!.TournamentName);
    }

    [Fact]
    public async Task UserSubmissionAndApproval_PreservePendingAndSavedValues()
    {
        _posts.Insert(new Post { Id = 42, Title = "Кубок", Link = "https://chgk-spb.livejournal.com/42.html", Description = "Текст" });
        foreach (var text in new[] { "/add_lines", "42", "Кубок", "Соус", "27.10.2026 19:30", "1800" })
        {
            var update = MessageUpdate(text);
            update.Message!.Chat.Id = 99;
            await _runner.HandleUpdate(_bot.Object, update, CancellationToken.None);
        }
        var submitted = Read()[5];
        Assert.Equal("pending_announcement", submitted["entity_type"]);
        Assert.Equal("https://chgk-spb.livejournal.com/42.html", submitted["source_link"]);
        using var pending = JsonDocument.Parse(submitted["after_json"]!);
        Assert.Equal("Кубок", pending.RootElement.GetProperty("entity").GetProperty("TournamentName").GetString());
        Assert.Null(_announcements.Get(42));
        await ClickAsync($"mod_approve_{submitted["entity_id"]}");
        var approval = Read()[6];
        using var approved = JsonDocument.Parse(approval["after_json"]!);
        Assert.Equal(JsonValueKind.Null, approved.RootElement.GetProperty("entity").ValueKind);
        Assert.Equal("Кубок", approved.RootElement.GetProperty("savedAnnouncement").GetProperty("TournamentName").GetString());
        Assert.NotNull(_announcements.Get(42));
    }

    [Fact]
    public async Task ManualAddition_RecordsEveryInputAndRepeatedStep()
    {
        _posts.Insert(new Post { Id = 42, Title = "Кубок", Link = "https://chgk-spb.livejournal.com/42.html", Description = "Текст" });
        var interrupted = _states.AddOrUpdate(10);
        interrupted.Step = AddStep.PendingEditWaitingName;
        interrupted.PendingEditId = 99;
        interrupted.SubmittedPendingId = 98;
        await SendAsync("/add_lines");
        await SendAsync("42");
        await SendAsync("Кубок");
        await SendAsync("Соус");
        await SendAsync("не дата");
        await SendAsync("27.10.2026 19:30");
        await SendAsync("1800/2100 ₽");

        var rows = Read();
        Assert.Equal(7, rows.Count);
        Assert.Equal("/add_lines", rows[0]["action_name"]);
        Assert.Equal("WaitingDateTime", rows[4]["step_before"]);
        Assert.Equal("awaiting_input", rows[4]["outcome"]);
        Assert.Equal("не дата", rows[4]["input_text"]);
        Assert.Equal("WaitingDateTime", rows[5]["step_before"]);
        Assert.Equal("WaitingCost", rows[5]["step_after"]);
        Assert.Equal("42", rows[6]["entity_id"]);
        Assert.Equal("announcement", rows[6]["entity_type"]);
        Assert.Equal("None", rows[6]["step_after"]);
        Assert.Equal("10", rows[6]["user_id"]);
        Assert.Contains("1800/2100", rows[6]["after_json"]!);
        Assert.NotNull(_announcements.Get(42));
    }

    [Fact]
    public async Task ReviewButtonAndEdit_AreLinkedToPostWithOriginalAndNewValues()
    {
        _posts.Insert(new Post { Id = 42, Title = "Кубок", Link = "https://chgk-spb.livejournal.com/42.html", Description = "Текст" });
        _drafts.Upsert(new AnnouncementReviewDraft { PostId = 42, TournamentName = "Старое", FailureCode = "name_normalization_mismatch" });
        await ClickAsync("autoreviewedit_name_42");
        await SendAsync("Новое");
        var rows = Read();
        Assert.Equal("callback", rows[0]["action_kind"]);
        Assert.Equal("autoreviewedit_name", rows[0]["action_name"]);
        Assert.Equal("review_draft", rows[1]["entity_type"]);
        Assert.Equal("42", rows[1]["entity_id"]);
        using var before = JsonDocument.Parse(rows[1]["before_json"]!);
        using var after = JsonDocument.Parse(rows[1]["after_json"]!);
        Assert.Equal("Старое", before.RootElement.GetProperty("entity").GetProperty("TournamentName").GetString());
        Assert.Equal("Новое", after.RootElement.GetProperty("entity").GetProperty("TournamentName").GetString());
        Assert.Equal("name_normalization_mismatch", after.RootElement.GetProperty("entity").GetProperty("FailureCode").GetString());
    }

    [Fact]
    public async Task InlineEditAndDeletion_PreserveValuesBeforeDeletion()
    {
        _posts.Insert(new Post { Id = 42, Title = "Кубок", Link = "https://chgk-spb.livejournal.com/42.html", Description = "Текст" });
        _announcements.Insert(new Announcement { Id = 42, TournamentName = "Кубок", Place = "Соус", Cost = 1800, DateTimeUtc = DateTime.UtcNow });
        await SendAsync("/edit_name 42 Кубок-2");
        await SendAsync("/delete 42");
        var rows = Read();
        Assert.Equal("42", rows[0]["entity_id"]);
        using var before = JsonDocument.Parse(rows[1]["before_json"]!);
        using var after = JsonDocument.Parse(rows[1]["after_json"]!);
        Assert.Equal("Кубок-2", before.RootElement.GetProperty("entity").GetProperty("TournamentName").GetString());
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("entity").ValueKind);
        Assert.Null(_announcements.Get(42));
    }

    [Fact]
    public async Task FooterAndCancellation_AreIncludedInAudit()
    {
        await SendAsync("/footer_add");
        await SendAsync("Новая строка");
        await SendAsync("/skip");
        await SendAsync("/add_lines");
        await SendAsync("/cancel");
        var rows = Read();
        Assert.Equal(5, rows.Count);
        Assert.Equal("/skip", rows[2]["action_name"]);
        Assert.Equal("FooterWaitingExpiry", rows[2]["step_before"]);
        Assert.Equal("/cancel", rows[4]["action_name"]);
        Assert.Equal("None", rows[4]["step_after"]);
        Assert.Single(_footers.ListAllDesc());
        Assert.Equal("footer", rows[2]["entity_type"]);
        using var footer = JsonDocument.Parse(rows[2]["after_json"]!);
        Assert.Equal("Новая строка", footer.RootElement.GetProperty("entity").GetProperty("Text").GetString());
    }

    [Fact]
    public async Task DeniedCallback_DoesNotStoreInputOrEntitySnapshot()
    {
        await ClickAsync("autoreviewedit_name_42", 99);
        var row = Assert.Single(Read());
        Assert.Equal("denied", row["outcome"]);
        Assert.Null(row["input_text"]);
        Assert.Null(row["before_json"]);
        Assert.Null(row["after_json"]);
    }

    [Theory]
    [InlineData("/footer_add")]
    [InlineData("/FOOTER_ADD@WeekAnnouncementBot")]
    [InlineData("/footer_del 1")]
    [InlineData("/footer_list")]
    [InlineData("/footer_edit")]
    [InlineData("/makepost")]
    [InlineData("/makepostlj")]
    public async Task AdminCommandOutsideAdminChat_DoesNotExecuteEvenForAllowedUser(string command)
    {
        _users.AllowUser(10);
        _footers.Insert("Original footer", null);
        var update = MessageUpdate(command);
        update.Message!.Chat.Id = 99;

        await _runner.HandleUpdate(_bot.Object, update, CancellationToken.None);

        Assert.Equal("Original footer", Assert.Single(_footers.ListAllDesc()).Text);
        Assert.False(_states.TryGet(10, out _));
        _bot.Verify(b => b.SendRequest<Message>(
            It.Is<Telegram.Bot.Requests.SendMessageRequest>(r => r.Text == Messages.Bot.AccessDenied),
            It.IsAny<CancellationToken>()), Times.Once);
        var row = Assert.Single(Read());
        Assert.Equal("denied", row["outcome"]);
        Assert.Null(row["input_text"]);
        Assert.Null(row["before_json"]);
        Assert.Null(row["after_json"]);
    }

    [Theory]
    [InlineData("FooterWaitingText", "Injected footer")]
    [InlineData("FooterWaitingExpiry", "/skip")]
    [InlineData("FooterEditWaitingText", "Injected footer")]
    [InlineData("FooterEditWaitingExpiry", "/skip")]
    [InlineData("PendingEditWaitingName", "Injected name")]
    [InlineData("PendingEditWaitingPlace", "Injected place")]
    [InlineData("PendingEditWaitingDateTime", "27.10.2026 19:30")]
    [InlineData("PendingEditWaitingCost", "1")]
    [InlineData("AutomationReviewWaitingName", "Injected name")]
    [InlineData("AutomationReviewWaitingPlace", "Injected place")]
    [InlineData("AutomationReviewWaitingDateTime", "27.10.2026 19:30")]
    [InlineData("AutomationReviewWaitingCost", "1")]
    public async Task AdminFlowContinuedOutsideAdminChat_IsDeniedAndRedacted(string step, string input)
    {
        var footerId = _footers.Insert("Original footer", DateTime.UtcNow.AddDays(1));
        var state = _states.AddOrUpdate(10);
        state.Step = Enum.Parse<AddStep>(step);
        state.FooterEditId = footerId;
        state.FooterDraftText = "Injected footer";
        var update = MessageUpdate(input);
        update.Message!.Chat.Id = 99;

        await _runner.HandleUpdate(_bot.Object, update, CancellationToken.None);

        var footer = Assert.Single(_footers.ListAllDesc());
        Assert.Equal("Original footer", footer.Text);
        Assert.NotNull(footer.ExpiresAt);
        Assert.False(_states.TryGet(10, out _));
        var row = Assert.Single(Read());
        Assert.Equal("denied", row["outcome"]);
        Assert.Null(row["input_text"]);
        Assert.Null(row["before_json"]);
        Assert.Null(row["after_json"]);
    }

    [Fact]
    public async Task AdminFlowCanBeCancelledFromAnotherChat()
    {
        _states.AddOrUpdate(10).Step = AddStep.FooterWaitingText;
        var update = MessageUpdate("/cancel");
        update.Message!.Chat.Id = 99;
        await _runner.HandleUpdate(_bot.Object, update, CancellationToken.None);
        Assert.False(_states.TryGet(10, out _));
        Assert.Equal("handled", Assert.Single(Read())["outcome"]);
    }

    [Theory]
    [InlineData("mod_reject_42", "ModerationHandler", "pending_announcement")]
    [InlineData("footeredit_text_42", "ModerationHandler", "footer")]
    [InlineData("forcepost_cancel", "ForcePostHandler", null)]
    public async Task OtherCallbacks_AreIncluded(string data, string handler, string? entityType)
    {
        await ClickAsync(data);
        var row = Assert.Single(Read());
        Assert.Equal(handler, row["handler"]);
        Assert.Equal("handled", row["outcome"]);
        Assert.Equal(entityType, row["entity_type"]);
    }

    [Fact]
    public async Task BannedUser_ProducesMetadataWithoutInput()
    {
        _users.BanUser(10);
        await SendAsync("private message");
        var row = Assert.Single(Read());
        Assert.Equal("denied", row["outcome"]);
        Assert.Null(row["input_text"]);
    }

    [Fact]
    public async Task Cancellation_IsRecordedAndRethrown()
    {
        _bot.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => SendAsync("/add_lines"));
        var row = Assert.Single(Read());
        Assert.Equal("cancelled", row["outcome"]);
        Assert.NotNull(row["completed_at_utc"]);
    }

    [Fact]
    public async Task UnknownAndNonTextMessages_AreRecordedAsIgnored()
    {
        await SendAsync("/unknown");
        await _runner.HandleUpdate(_bot.Object, new Update { Id = ++_updateId,
            Message = new Message { Id = _updateId, Chat = new Chat { Id = 1 }, From = new User { Id = 10 } } }, CancellationToken.None);
        var rows = Read();
        Assert.Equal("ignored", rows[0]["outcome"]);
        Assert.Equal("unsupported_message", rows[1]["action_kind"]);
        Assert.Equal("ignored", rows[1]["outcome"]);
    }

    [Fact]
    public async Task HandlerFailure_IsRecordedAndOriginalExceptionIsRethrown()
    {
        await SendAsync("/add_lines");
        _bot.Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("private failure text"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync("42"));
        Assert.Equal("private failure text", exception.Message);
        var row = Read()[1];
        Assert.Equal("error", row["outcome"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, row["error_type"]);
        Assert.Equal("AddAnnouncementFlow", row["handler"]);
        Assert.NotNull(row["completed_at_utc"]);
        Assert.DoesNotContain("private failure text", string.Join(" ", row.Values));
    }

    [Fact]
    public void InterruptedAction_RemainsVisibleAfterRepositoryRecreation()
    {
        var action = _auditor.Begin(MessageUpdate("/add"));
        Assert.NotNull(action);
        _ = new ManualActionsRepository(_path);
        var row = Assert.Single(Read());
        Assert.Equal("received", row["outcome"]);
        Assert.Null(row["completed_at_utc"]);
    }

    [Theory]
    [InlineData("password=very-private", "very-private")]
    [InlineData("{\"password\":\"very-private\"}", "very-private")]
    [InlineData("Bearer my-private-token", "my-private-token")]
    [InlineData("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ_123456789", "ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("https://example.org/?token=private-value", "private-value")]
    public async Task CommonSecrets_AreRedactedInInputsAndSnapshots(string text, string secret)
    {
        _states.AddOrUpdate(10).Step = AddStep.WaitingName;
        await SendAsync(text);
        var row = Assert.Single(Read());
        Assert.DoesNotContain(secret, string.Join(" ", row.Values));
        Assert.Contains("[REDACTED]", row["input_text"]!);
    }

    private Task SendAsync(string text) => _runner.HandleUpdate(_bot.Object, MessageUpdate(text), CancellationToken.None);

    private Update MessageUpdate(string text) => new()
    {
        Id = ++_updateId,
        Message = new Message { Id = _updateId, Text = text, Chat = new Chat { Id = 1 }, From = new User { Id = 10 } }
    };

    private Task ClickAsync(string data, long chat = 1) => _runner.HandleUpdate(_bot.Object,
        new Update { Id = ++_updateId, CallbackQuery = new CallbackQuery
        {
            Id = $"callback-{_updateId}", From = new User { Id = 10 }, Data = data,
            Message = new Message { Id = 20, Chat = new Chat { Id = chat } }
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

    public void Dispose() => File.Delete(_path);
}
