using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public class PendingAnnouncement
{
    public long Id { get; set; }
    public required string TournamentName { get; set; }
    public required string Place { get; set; }
    public DateTime DateTimeUtc { get; set; }
    public int Cost { get; set; }
    public string? CostLabel { get; set; }
    public long UserId { get; set; }
    public string? UserName { get; set; }
    public string? Link { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserManagementRepository
{
    private readonly string _dbPath;

    public UserManagementRepository(string dbPath)
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
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS pending_announcements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tournamentName TEXT NOT NULL,
                place TEXT,
                dateTimeUtc TEXT NOT NULL,
                cost INTEGER NOT NULL,
                userId INTEGER NOT NULL,
                link TEXT,
                createdAt TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS allowed_users (
                userId INTEGER PRIMARY KEY
            )";
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS banned_users (
                userId INTEGER PRIMARY KEY
            )";
        cmd.ExecuteNonQuery();

        try
        {
            cmd.CommandText = "ALTER TABLE pending_announcements ADD COLUMN cost_text TEXT";
            cmd.ExecuteNonQuery();
        }
        catch
        {
        }

        try
        {
            cmd.CommandText = "ALTER TABLE pending_announcements ADD COLUMN userName TEXT";
            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    public bool IsAllowed(long userId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM allowed_users WHERE userId=@userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public bool IsBanned(long userId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM banned_users WHERE userId=@userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void AllowUser(long userId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO allowed_users (userId) VALUES (@userId)";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }

    public void BanUser(long userId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var tx = connection.BeginTransaction();
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        
        cmd.CommandText = "DELETE FROM allowed_users WHERE userId=@userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = "INSERT OR IGNORE INTO banned_users (userId) VALUES (@userId)";
        cmd.ExecuteNonQuery();
        
        tx.Commit();
    }

    public long AddPending(PendingAnnouncement pending)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO pending_announcements (tournamentName, place, dateTimeUtc, cost, cost_text, userId, userName, link, createdAt)
              VALUES (@name, @place, @dt, @cost, @costText, @userId, @userName, @link, @createdAt)";
        cmd.Parameters.AddWithValue("@name", pending.TournamentName);
        cmd.Parameters.AddWithValue("@place", pending.Place);
        cmd.Parameters.AddWithValue("@dt", pending.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", pending.Cost);
        cmd.Parameters.AddWithValue("@costText", pending.CostLabel is null ? DBNull.Value : pending.CostLabel);
        cmd.Parameters.AddWithValue("@userId", pending.UserId);
        cmd.Parameters.AddWithValue("@userName", pending.UserName is null ? DBNull.Value : pending.UserName);
        cmd.Parameters.AddWithValue("@link", pending.Link is null ? DBNull.Value : pending.Link);
        cmd.Parameters.AddWithValue("@createdAt", pending.CreatedAt.ToUniversalTime().ToString("O"));
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    public PendingAnnouncement? GetPending(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT id, tournamentName, place, dateTimeUtc, cost, userId, link, createdAt, cost_text, userName
              FROM pending_announcements
              WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var place = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var dt = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var link = reader.IsDBNull(6) ? null : reader.GetString(6);
        var createdAt = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var costLabel = reader.IsDBNull(8) ? null : reader.GetString(8);
        var userName = reader.IsDBNull(9) ? null : reader.GetString(9);

        return new PendingAnnouncement
        {
            Id = reader.GetInt64(0),
            TournamentName = reader.GetString(1),
            Place = place,
            DateTimeUtc = dt,
            Cost = reader.GetInt32(4),
            CostLabel = costLabel,
            UserId = reader.GetInt64(5),
            UserName = userName,
            Link = link,
            CreatedAt = createdAt
        };
    }

    public bool UpdatePending(PendingAnnouncement pending)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"UPDATE pending_announcements
              SET tournamentName=@name, place=@place, dateTimeUtc=@dt, cost=@cost, cost_text=@costText
              WHERE id=@id";
        cmd.Parameters.AddWithValue("@name", pending.TournamentName);
        cmd.Parameters.AddWithValue("@place", pending.Place);
        cmd.Parameters.AddWithValue("@dt", pending.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", pending.Cost);
        cmd.Parameters.AddWithValue("@costText", pending.CostLabel is null ? DBNull.Value : pending.CostLabel);
        cmd.Parameters.AddWithValue("@id", pending.Id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeletePending(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_announcements WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }
}
