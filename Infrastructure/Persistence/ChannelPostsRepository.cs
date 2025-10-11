using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public class ChannelPostsRepository
{
    private readonly string _dbPath;

    public ChannelPostsRepository(string dbPath)
    {
        _dbPath = dbPath;
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS channel_posts (
                scheduled_at_utc TEXT PRIMARY KEY,
                posted_at_utc TEXT NOT NULL,
                message_id INTEGER
            )";
        cmd.ExecuteNonQuery();

        EnsureMessageIdColumn(connection);
    }

    private static void EnsureMessageIdColumn(SqliteConnection connection)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(channel_posts)";
        using var reader = pragma.ExecuteReader();
        var hasMessageId = false;
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, "message_id", StringComparison.OrdinalIgnoreCase))
            {
                hasMessageId = true;
                break;
            }
        }

        if (hasMessageId)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE channel_posts ADD COLUMN message_id INTEGER";
        alter.ExecuteNonQuery();
    }

    public bool HasPosted(DateTime scheduledUtc)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM channel_posts WHERE scheduled_at_utc=@scheduled";
        cmd.Parameters.AddWithValue("@scheduled", scheduledUtc.ToUniversalTime().ToString("O"));
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void MarkPosted(DateTime scheduledUtc, DateTime? postedUtc = null, int? messageId = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO channel_posts (scheduled_at_utc, posted_at_utc, message_id)
              VALUES (@scheduled, @posted, @message)
              ON CONFLICT(scheduled_at_utc) DO UPDATE SET
                  posted_at_utc=excluded.posted_at_utc,
                  message_id=excluded.message_id";
        cmd.Parameters.AddWithValue("@scheduled", scheduledUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@posted", (postedUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("O"));
        if (messageId is null)
        {
            cmd.Parameters.AddWithValue("@message", DBNull.Value);
        }
        else
        {
            cmd.Parameters.AddWithValue("@message", messageId.Value);
        }
        cmd.ExecuteNonQuery();
    }

    public ChannelPostEntry? GetLatest()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT scheduled_at_utc, posted_at_utc, message_id
              FROM channel_posts
              ORDER BY posted_at_utc DESC
              LIMIT 1";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var scheduled = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.AdjustToUniversal);
        var posted = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.AdjustToUniversal);
        int? messageId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
        return new ChannelPostEntry(scheduled, posted, messageId);
    }
}

public sealed record ChannelPostEntry(DateTime ScheduledAtUtc, DateTime PostedAtUtc, int? MessageId);
