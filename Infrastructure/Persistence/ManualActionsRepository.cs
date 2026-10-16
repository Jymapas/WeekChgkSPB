using Microsoft.Data.Sqlite;
using WeekChgkSPB.Infrastructure.Bot.Auditing;

namespace WeekChgkSPB;

internal sealed class ManualActionsRepository
{
    private readonly string _dbPath;

    internal ManualActionsRepository(string dbPath)
    {
        _dbPath = dbPath;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS manual_actions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                update_id INTEGER NOT NULL,
                user_id INTEGER,
                chat_id INTEGER,
                message_id INTEGER,
                callback_query_id TEXT,
                action_kind TEXT NOT NULL,
                action_name TEXT NOT NULL,
                input_text TEXT,
                handler TEXT,
                outcome TEXT NOT NULL DEFAULT 'received',
                entity_type TEXT,
                entity_id INTEGER,
                source_link TEXT,
                step_before TEXT NOT NULL,
                step_after TEXT,
                before_json TEXT,
                after_json TEXT,
                error_type TEXT,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_manual_actions_started ON manual_actions(started_at_utc);
            CREATE INDEX IF NOT EXISTS ix_manual_actions_entity ON manual_actions(entity_type, entity_id);
            CREATE INDEX IF NOT EXISTS ix_manual_actions_action ON manual_actions(action_kind, action_name);
            """;
        command.ExecuteNonQuery();
    }

    internal long Begin(ManualAction action)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO manual_actions
                (update_id, user_id, chat_id, message_id, callback_query_id, action_kind, action_name,
                 input_text, entity_type, entity_id, source_link, step_before, before_json, started_at_utc)
            VALUES (@update, @user, @chat, @message, @callback, @kind, @name, @input,
                    @entityType, @entityId, @link, @step, @before, @now);
            SELECT last_insert_rowid();
            """;
        Add(command, "@update", action.UpdateId);
        Add(command, "@user", action.UserId);
        Add(command, "@chat", action.ChatId);
        Add(command, "@message", action.MessageId);
        Add(command, "@callback", action.CallbackQueryId);
        Add(command, "@kind", action.Kind);
        Add(command, "@name", action.Name);
        Add(command, "@input", action.Input);
        Add(command, "@entityType", action.EntityType);
        Add(command, "@entityId", action.EntityId);
        Add(command, "@link", action.SourceLink);
        Add(command, "@step", action.StepBefore);
        Add(command, "@before", action.BeforeJson);
        Add(command, "@now", DateTime.UtcNow.ToString("O"));
        return (long)command.ExecuteScalar()!;
    }

    internal void Complete(ManualAction action, string? errorType)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE manual_actions SET handler=@handler, outcome=@outcome,
                entity_type=@entityType, entity_id=@entityId, source_link=@link,
                step_after=@step, after_json=@after, error_type=@error, completed_at_utc=@now
            WHERE id=@id
            """;
        Add(command, "@id", action.Id);
        Add(command, "@handler", action.Handler);
        Add(command, "@outcome", action.Outcome);
        Add(command, "@entityType", action.EntityType);
        Add(command, "@entityId", action.EntityId);
        Add(command, "@link", action.SourceLink);
        Add(command, "@step", action.StepAfter);
        Add(command, "@after", action.AfterJson);
        Add(command, "@error", errorType);
        Add(command, "@now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>Открывает соединение с журналом ручных действий.</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    /// <summary>Добавляет параметр SQL с поддержкой пустого значения.</summary>
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
