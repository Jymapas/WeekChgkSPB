using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public class FootersRepository
{
    private readonly string _dbPath;
    public FootersRepository(string dbPath) { _dbPath = dbPath; EnsureCreated(); }

    private void EnsureCreated()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();

        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText =
                @"CREATE TABLE IF NOT EXISTS footers (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    text       TEXT NOT NULL,
                    expires_at TEXT
                )";
            cmd.ExecuteNonQuery();
        }

        try
        {
            using var alter = c.CreateCommand();
            alter.CommandText = "ALTER TABLE footers ADD COLUMN expires_at TEXT";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // column already exists — expected on existing databases
        }
    }

    public long Insert(string text, DateTime? expiresAt = null)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO footers(text, expires_at) VALUES(@t, @e); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@t", text);
        cmd.Parameters.AddWithValue("@e", expiresAt.HasValue ? (object)expiresAt.Value.ToString("o") : DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void Delete(long id)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM footers WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<(long Id, string Text, DateTime? ExpiresAt)> ListAllDesc()
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, text, expires_at FROM footers ORDER BY id DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<(long, string, DateTime?)>();
        while (r.Read())
        {
            DateTime? exp = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
            list.Add((r.GetInt64(0), r.GetString(1), exp));
        }
        return list;
    }

    public (long Id, string Text, DateTime? ExpiresAt)? Get(long id)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, text, expires_at FROM footers WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        DateTime? exp = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
        return (r.GetInt64(0), r.GetString(1), exp);
    }

    public void UpdateText(long id, string text)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE footers SET text=@t WHERE id=@id";
        cmd.Parameters.AddWithValue("@t", text);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateExpiry(long id, DateTime? expiresAt)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE footers SET expires_at=@e WHERE id=@id";
        cmd.Parameters.AddWithValue("@e", expiresAt.HasValue ? (object)expiresAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<string> GetAllTextsDesc()
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT text FROM footers WHERE expires_at IS NULL OR expires_at > @now ORDER BY id DESC";
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
