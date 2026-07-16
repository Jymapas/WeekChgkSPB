using Microsoft.Data.Sqlite;

namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed class AnnouncementParseAttemptsRepository
{
    private readonly string _dbPath;

    public AnnouncementParseAttemptsRepository(string dbPath)
    {
        _dbPath = dbPath;
        EnsureCreated();
    }

    public void Upsert(AnnouncementParseAttempt attempt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO announcement_parse_attempts
                (post_id, mode, outcome, failure_code, cost, cost_evidence, source_length, payload_length,
                 provider, model, input_tokens, output_tokens, candidate_json, created_at_utc)
            VALUES
                (@postId, @mode, @outcome, @failureCode, @cost, @costEvidence, @sourceLength, @payloadLength,
                 @provider, @model, @inputTokens, @outputTokens, @candidateJson, @createdAtUtc)
            ON CONFLICT(post_id) DO UPDATE SET
                mode=excluded.mode, outcome=excluded.outcome, failure_code=excluded.failure_code,
                cost=excluded.cost, cost_evidence=excluded.cost_evidence, source_length=excluded.source_length,
                payload_length=excluded.payload_length, provider=excluded.provider, model=excluded.model,
                input_tokens=excluded.input_tokens, output_tokens=excluded.output_tokens,
                candidate_json=excluded.candidate_json, created_at_utc=excluded.created_at_utc
            """;
        Add(command, "@postId", attempt.PostId);
        Add(command, "@mode", attempt.Mode);
        Add(command, "@outcome", attempt.Outcome);
        Add(command, "@failureCode", attempt.FailureCode);
        Add(command, "@cost", attempt.Cost);
        Add(command, "@costEvidence", attempt.CostEvidence);
        Add(command, "@sourceLength", attempt.SourceLength);
        Add(command, "@payloadLength", attempt.PayloadLength);
        Add(command, "@provider", attempt.Provider);
        Add(command, "@model", attempt.Model);
        Add(command, "@inputTokens", attempt.InputTokens);
        Add(command, "@outputTokens", attempt.OutputTokens);
        Add(command, "@candidateJson", attempt.CandidateJson);
        Add(command, "@createdAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void MarkSaved(long postId) => Mark(postId, "saved_at_utc");
    public void MarkChannelUpdated(long postId) => Mark(postId, "channel_updated_at_utc");
    public void MarkNotified(long postId) => Mark(postId, "notified_at_utc");

    public bool Exists(long postId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM announcement_parse_attempts WHERE post_id=@postId)";
        command.Parameters.AddWithValue("@postId", postId);
        return (long)command.ExecuteScalar()! == 1;
    }

    private void EnsureCreated()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS announcement_parse_attempts (
                post_id INTEGER PRIMARY KEY,
                mode TEXT NOT NULL,
                outcome TEXT NOT NULL,
                failure_code TEXT,
                cost INTEGER,
                cost_evidence TEXT,
                source_length INTEGER NOT NULL DEFAULT 0,
                payload_length INTEGER NOT NULL DEFAULT 0,
                provider TEXT NOT NULL DEFAULT 'alibaba_model_studio',
                model TEXT NOT NULL,
                input_tokens INTEGER,
                output_tokens INTEGER,
                candidate_json TEXT,
                created_at_utc TEXT NOT NULL,
                saved_at_utc TEXT,
                channel_updated_at_utc TEXT,
                notified_at_utc TEXT
            )
            """;
        command.ExecuteNonQuery();
        EnsureColumns(connection);
    }

    private static void EnsureColumns(SqliteConnection connection)
    {
        var columns = new Dictionary<string, string>
        {
            ["cost"] = "INTEGER", ["cost_evidence"] = "TEXT", ["source_length"] = "INTEGER NOT NULL DEFAULT 0",
            ["payload_length"] = "INTEGER NOT NULL DEFAULT 0", ["provider"] = "TEXT NOT NULL DEFAULT 'alibaba_model_studio'",
            ["model"] = "TEXT NOT NULL DEFAULT ''", ["input_tokens"] = "INTEGER", ["output_tokens"] = "INTEGER",
            ["candidate_json"] = "TEXT", ["failure_code"] = "TEXT", ["saved_at_utc"] = "TEXT",
            ["channel_updated_at_utc"] = "TEXT", ["notified_at_utc"] = "TEXT"
        };
        foreach (var column in columns)
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('announcement_parse_attempts') WHERE name=@name";
            check.Parameters.AddWithValue("@name", column.Key);
            if ((long)check.ExecuteScalar()! == 0)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE announcement_parse_attempts ADD COLUMN {column.Key} {column.Value}";
                alter.ExecuteNonQuery();
            }
        }
    }

    private void Mark(long postId, string column)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE announcement_parse_attempts SET {column}=@now WHERE post_id=@postId";
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

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
