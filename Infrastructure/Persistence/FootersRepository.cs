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

        using var cmd = c.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS footers (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                text TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    public long Insert(string text)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO footers(text) VALUES(@t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@t", text);
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

    public List<(long Id, string Text)> ListAllDesc()
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, text FROM footers ORDER BY id DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<(long, string)>();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public List<string> GetAllTextsDesc()
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT text FROM footers ORDER BY id DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
