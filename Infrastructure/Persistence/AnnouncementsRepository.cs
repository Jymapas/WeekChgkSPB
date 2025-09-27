using System.Globalization;
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
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS announcements (
                id INTEGER PRIMARY KEY,
                tournamentName TEXT NOT NULL,
                place TEXT,
                dateTimeUtc TEXT NOT NULL,
                cost INTEGER NOT NULL
            )";
        cmd.ExecuteNonQuery();
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
            @"INSERT INTO announcements (id, tournamentName, place, dateTimeUtc, cost)
              VALUES (@id, @name, @place, @dt, @cost)";
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@name", a.TournamentName);
        cmd.Parameters.AddWithValue("@place", a.Place);
        cmd.Parameters.AddWithValue("@dt", a.DateTimeUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", a.Cost);
        cmd.ExecuteNonQuery();
    }

    public Announcement? Get(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT id, tournamentName, place, dateTimeUtc, cost
              FROM announcements
              WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var place = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var dt = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.AdjustToUniversal);

        return new Announcement
        {
            Id = reader.GetInt64(0),
            TournamentName = reader.GetString(1),
            Place = place,
            DateTimeUtc = dt,
            Cost = reader.GetInt32(4)
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

    public IReadOnlyList<AnnouncementRow> GetWithLinksInRange(DateTime fromUtc, DateTime toUtc)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT a.id, a.tournamentName, a.place, a.dateTimeUtc, a.cost, p.link
          FROM announcements AS a
          JOIN posts AS p ON p.id = a.id
          WHERE a.dateTimeUtc >= @from AND a.dateTimeUtc <= @to
          ORDER BY a.dateTimeUtc, a.id;";
        cmd.Parameters.AddWithValue("@from", fromUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@to", toUtc.ToString("O"));

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
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM announcements WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }
}
