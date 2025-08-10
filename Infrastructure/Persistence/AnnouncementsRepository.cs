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
}