using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public class AnnouncementsRepository
{
    private readonly string _dbPath;

    public AnnouncementsRepository(string dbPath)
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
            @"CREATE TABLE IF NOT EXISTS announcements (
                id INTEGER PRIMARY KEY,
                tournamentName TEXT NOT NULL,
                place TEXT,
                dateTimeUtc TEXT NOT NULL,
                cost INTEGER NOT NULL,
                userId INTEGER
            )";
        cmd.ExecuteNonQuery();
        
        try
        {
            cmd.CommandText = "ALTER TABLE announcements ADD COLUMN userId INTEGER";
            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
        
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS external_posts (
                announcementId INTEGER PRIMARY KEY,
                link TEXT NOT NULL UNIQUE,
                normalizedLink TEXT
            )";
        cmd.ExecuteNonQuery();
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

        EnsureColumnExists(connection, "external_posts", "normalizedLink", "TEXT");
        BackfillExternalNormalizedLinks(connection);
    }

    private static void EnsureColumnExists(SqliteConnection connection, string table, string column, string type)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    private static void BackfillExternalNormalizedLinks(SqliteConnection connection)
    {
        var rows = new List<(long Id, string Normalized)>();
        using var select = connection.CreateCommand();
        select.CommandText = @"SELECT announcementId, link FROM external_posts WHERE normalizedLink IS NULL OR normalizedLink = ''";
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var link = reader.IsDBNull(1) ? "" : reader.GetString(1);
                rows.Add((id, LinkNormalizer.Normalize(link)));
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE external_posts SET normalizedLink=@normalized WHERE announcementId=@id";
        var idParam = update.CreateParameter();
        idParam.ParameterName = "@id";
        update.Parameters.Add(idParam);
        var normalizedParam = update.CreateParameter();
        normalizedParam.ParameterName = "@normalized";
        update.Parameters.Add(normalizedParam);

        foreach (var row in rows)
        {
            idParam.Value = row.Id;
            normalizedParam.Value = row.Normalized;
            update.ExecuteNonQuery();
        }
    }

    public bool Exists(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM announcements WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void Insert(Announcement a)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO announcements (id, tournamentName, place, dateTimeUtc, cost, userId)
              VALUES (@id, @name, @place, @dt, @cost, @userId)";
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@name", a.TournamentName);
        cmd.Parameters.AddWithValue("@place", a.Place);
        cmd.Parameters.AddWithValue("@dt", a.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", a.Cost);
        cmd.Parameters.AddWithValue("@userId", a.UserId is null ? DBNull.Value : a.UserId);
        cmd.ExecuteNonQuery();
    }

    public void InsertExternal(Announcement a, string link)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var tx = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            @"INSERT INTO announcements (tournamentName, place, dateTimeUtc, cost, userId)
              VALUES (@name, @place, @dt, @cost, @userId)";
        cmd.Parameters.AddWithValue("@name", a.TournamentName);
        cmd.Parameters.AddWithValue("@place", a.Place);
        cmd.Parameters.AddWithValue("@dt", a.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", a.Cost);
        cmd.Parameters.AddWithValue("@userId", a.UserId is null ? DBNull.Value : a.UserId);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        a.Id = (long)cmd.ExecuteScalar()!;

        cmd.CommandText = "INSERT INTO external_posts (announcementId, link, normalizedLink) VALUES (@id, @link, @normalizedLink)";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@link", link);
        cmd.Parameters.AddWithValue("@normalizedLink", LinkNormalizer.Normalize(link));
        cmd.ExecuteNonQuery();

        tx.Commit();
    }

    public Announcement? Get(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT id, tournamentName, place, dateTimeUtc, cost, userId
              FROM announcements
              WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var place = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var dt = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.AdjustToUniversal);
        var userId = reader.IsDBNull(5) ? null : (long?)reader.GetInt64(5);

        return new Announcement
        {
            Id = reader.GetInt64(0),
            TournamentName = reader.GetString(1),
            Place = place,
            DateTimeUtc = dt,
            Cost = reader.GetInt32(4),
            UserId = userId
        };
    }

    public Announcement? GetByLink(string link)
    {
        var raw = link?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var normalized = LinkNormalizer.Normalize(raw);
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT a.id, a.tournamentName, a.place, a.dateTimeUtc, a.cost, a.userId
              FROM announcements AS a
              LEFT JOIN posts AS p ON p.id = a.id
              LEFT JOIN external_posts AS e ON e.announcementId = a.id
              WHERE p.normalizedLink = @normalized
                 OR p.link = @raw
                 OR e.normalizedLink = @normalized
                 OR e.link = @raw
              LIMIT 1";
        cmd.Parameters.AddWithValue("@normalized", normalized);
        cmd.Parameters.AddWithValue("@raw", raw);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var place = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var dt = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.AdjustToUniversal);
        var userId = reader.IsDBNull(5) ? null : (long?)reader.GetInt64(5);

        return new Announcement
        {
            Id = reader.GetInt64(0),
            TournamentName = reader.GetString(1),
            Place = place,
            DateTimeUtc = dt,
            Cost = reader.GetInt32(4),
            UserId = userId
        };
    }

    public void Update(Announcement a)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"UPDATE announcements
              SET tournamentName=@name,
                  place=@place,
                  dateTimeUtc=@dt,
                  cost=@cost
              WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@name", a.TournamentName);
        cmd.Parameters.AddWithValue("@place", a.Place);
        cmd.Parameters.AddWithValue("@dt", a.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", a.Cost);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AnnouncementRow> GetWithLinksInRange(DateTime fromUtc, DateTime? toUtc = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        var toClause = toUtc is null ? string.Empty : " AND a.dateTimeUtc <= @to";
        cmd.CommandText =
            $@"SELECT a.id, a.tournamentName, a.place, a.dateTimeUtc, a.cost, COALESCE(p.link, e.link), a.userId
          FROM announcements AS a
          LEFT JOIN posts AS p ON p.id = a.id
          LEFT JOIN external_posts AS e ON e.announcementId = a.id
          WHERE a.dateTimeUtc >= @from{toClause}
          ORDER BY a.dateTimeUtc, a.id;";
        cmd.Parameters.AddWithValue("@from", fromUtc.ToString("O"));
        if (toUtc is not null)
        {
            cmd.Parameters.AddWithValue("@to", toUtc.Value.ToString("O"));
        }

        var list = new List<AnnouncementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var name = r.GetString(1);
            var place = r.IsDBNull(2) ? "" : r.GetString(2);
            var dt = DateTime.Parse(r.GetString(3), null, DateTimeStyles.AdjustToUniversal);
            var cost = r.GetInt32(4);
            var link = r.IsDBNull(5) ? "" : r.GetString(5);
            list.Add(new AnnouncementRow(id, name, place, dt, cost, link));
        }

        return list;
    }

    public bool Delete(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM external_posts WHERE announcementId=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DELETE FROM announcements WHERE id=@id";
        var removed = cmd.ExecuteNonQuery() > 0;
        tx.Commit();
        return removed;
    }
}
