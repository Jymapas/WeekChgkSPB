using System;
using System.IO;
using Microsoft.Data.Sqlite;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementReviewDraftRepositoryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"weekchgk-review-{Guid.NewGuid():N}.db");

    [Fact]
    public void CreatesSchemaAndPersistsNullableDraft()
    {
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        repository.Upsert(new AnnouncementReviewDraft
        {
            PostId = 42,
            Place = "Rossi's",
            Cost = 1950,
            FailureCode = "json_invalid"
        });

        var stored = repository.Get(42);

        Assert.NotNull(stored);
        Assert.Null(stored!.TournamentName);
        Assert.Equal("Rossi's", stored.Place);
        Assert.Equal(1950, stored.Cost);
        Assert.False(stored.IsComplete);
        Assert.Equal(AnnouncementReviewStatuses.Pending, stored.Status);
    }

    [Fact]
    public void UpsertPreservesAlreadyStoredTelegramMessageIds()
    {
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        var draft = new AnnouncementReviewDraft { PostId = 43, Cost = 1800 };
        repository.Upsert(draft);
        repository.SetSourceMessageId(43, 10);
        repository.SetReviewMessageId(43, 11);

        draft.TournamentName = "Кубок";
        repository.Upsert(draft);

        var stored = repository.Get(43);
        Assert.Equal(10, stored!.SourceMessageId);
        Assert.Equal(11, stored.ReviewMessageId);
        Assert.Equal("Кубок", stored.TournamentName);
    }

    [Fact]
    public void ManualEdits_AreRecordedWithoutOverwritingHistory()
    {
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        var draft = new AnnouncementReviewDraft { PostId = 42, Cost = 1800, FailureCode = "json_invalid" };
        repository.Upsert(draft);
        draft.TournamentName = "Кубок";
        repository.Upsert(draft, 123);
        draft.TournamentName = "Кубок-2";
        draft.Cost = 2000;
        repository.Upsert(draft, 123);
        repository.Upsert(draft, 123); // An unchanged submission is not an edit.
        repository.SetStatus(42, AnnouncementReviewStatuses.Added);

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT field, old_value, new_value, edited_by_user_id FROM announcement_review_edits ORDER BY id";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("tournament_name", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal("Кубок", reader.GetString(2));
        Assert.Equal(123, reader.GetInt64(3));
        Assert.True(reader.Read());
        Assert.Equal("Кубок", reader.GetString(1));
        Assert.Equal("Кубок-2", reader.GetString(2));
        Assert.True(reader.Read());
        Assert.Equal("cost", reader.GetString(0));
        Assert.Equal("1800", reader.GetString(1));
        Assert.Equal("2000", reader.GetString(2));
        Assert.False(reader.Read());
        Assert.Equal("json_invalid", repository.Get(42)!.FailureCode);
    }

    [Fact]
    public void Constructor_AddsAuditToExistingDatabaseWithoutLosingDrafts()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE announcement_review_drafts (
                    post_id INTEGER PRIMARY KEY, tournament_name TEXT, place TEXT, datetime_utc TEXT,
                    cost INTEGER, failure_code TEXT, status TEXT NOT NULL DEFAULT 'pending',
                    source_message_id INTEGER, review_message_id INTEGER,
                    created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
                INSERT INTO announcement_review_drafts
                    (post_id, cost, created_at_utc, updated_at_utc) VALUES (42, 1800, 'old', 'old');
                """;
            command.ExecuteNonQuery();
        }
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        var draft = repository.Get(42)!;
        Assert.Equal(1800, draft.Cost);
        draft.Cost = 1900;
        repository.Upsert(draft, 123);
        Assert.Equal(1900, new AnnouncementReviewDraftRepository(_dbPath).Get(42)!.Cost);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
