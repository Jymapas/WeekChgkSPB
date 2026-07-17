using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementReviewDraftRepository
{
    private readonly string _dbPath;

    public AnnouncementReviewDraftRepository(string dbPath)
    {
        _dbPath = dbPath;
        EnsureCreated();
    }

    public void Upsert(AnnouncementReviewDraft draft)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO announcement_review_drafts
                (post_id, tournament_name, place, datetime_utc, cost, failure_code, status,
                 source_message_id, review_message_id, created_at_utc, updated_at_utc)
            VALUES
                (@postId, @name, @place, @dateTimeUtc, @cost, @failureCode, @status,
                 @sourceMessageId, @reviewMessageId, @now, @now)
            ON CONFLICT(post_id) DO UPDATE SET
                tournament_name=excluded.tournament_name,
                place=excluded.place,
                datetime_utc=excluded.datetime_utc,
                cost=excluded.cost,
                failure_code=excluded.failure_code,
                status=excluded.status,
                source_message_id=COALESCE(
                    announcement_review_drafts.source_message_id,
                    excluded.source_message_id),
                review_message_id=COALESCE(
                    announcement_review_drafts.review_message_id,
                    excluded.review_message_id),
                updated_at_utc=excluded.updated_at_utc
            """;
        Add(command, "@postId", draft.PostId);
        Add(command, "@name", NullIfWhiteSpace(draft.TournamentName));
        Add(command, "@place", NullIfWhiteSpace(draft.Place));
        Add(command, "@dateTimeUtc", draft.DateTimeUtc?.ToUniversalTime().ToString("O"));
        Add(command, "@cost", draft.Cost);
        Add(command, "@failureCode", draft.FailureCode);
        Add(command, "@status", draft.Status);
        Add(command, "@sourceMessageId", draft.SourceMessageId);
        Add(command, "@reviewMessageId", draft.ReviewMessageId);
        Add(command, "@now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public AnnouncementReviewDraft? Get(long postId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT post_id, tournament_name, place, datetime_utc, cost, failure_code, status,
                   source_message_id, review_message_id
            FROM announcement_review_drafts
            WHERE post_id=@postId
            """;
        command.Parameters.AddWithValue("@postId", postId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new AnnouncementReviewDraft
        {
            PostId = reader.GetInt64(0),
            TournamentName = reader.IsDBNull(1) ? null : reader.GetString(1),
            Place = reader.IsDBNull(2) ? null : reader.GetString(2),
            DateTimeUtc = reader.IsDBNull(3)
                ? null
                : DateTime.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal),
            Cost = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            FailureCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = reader.GetString(6),
            SourceMessageId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            ReviewMessageId = reader.IsDBNull(8) ? null : reader.GetInt32(8)
        };
    }

    public void SetSourceMessageId(long postId, int messageId) =>
        SetMessageId(postId, "source_message_id", messageId);

    public void SetReviewMessageId(long postId, int messageId) =>
        SetMessageId(postId, "review_message_id", messageId);

    public void SetStatus(long postId, string status)
    {
        if (status is not (
            AnnouncementReviewStatuses.Pending or
            AnnouncementReviewStatuses.Added or
            AnnouncementReviewStatuses.Skipped))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE announcement_review_drafts
            SET status=@status, updated_at_utc=@now
            WHERE post_id=@postId
            """;
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@postId", postId);
        command.ExecuteNonQuery();
    }

    private void EnsureCreated()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS announcement_review_drafts (
                post_id INTEGER PRIMARY KEY,
                tournament_name TEXT,
                place TEXT,
                datetime_utc TEXT,
                cost INTEGER,
                failure_code TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                source_message_id INTEGER,
                review_message_id INTEGER,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }

    private void SetMessageId(long postId, string column, int messageId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             UPDATE announcement_review_drafts
             SET {column}=@messageId, updated_at_utc=@now
             WHERE post_id=@postId AND {column} IS NULL
             """;
        command.Parameters.AddWithValue("@messageId", messageId);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@postId", postId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
