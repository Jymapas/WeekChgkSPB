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
                posted_at_utc TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
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

    public void MarkPosted(DateTime scheduledUtc, DateTime? postedUtc = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO channel_posts (scheduled_at_utc, posted_at_utc)
              VALUES (@scheduled, @posted)
              ON CONFLICT(scheduled_at_utc) DO UPDATE SET posted_at_utc=excluded.posted_at_utc";
        cmd.Parameters.AddWithValue("@scheduled", scheduledUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@posted", (postedUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
