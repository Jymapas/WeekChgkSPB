using System.IO;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public class PostsRepository
{
    private readonly string _dbPath;

    public PostsRepository(string dbPath)
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
            @"CREATE TABLE IF NOT EXISTS posts (
                id INTEGER PRIMARY KEY,
                title TEXT,
                link TEXT,
                description TEXT
            )";
        cmd.ExecuteNonQuery();
    }

    public bool Exists(long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM posts WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var count = (long)cmd.ExecuteScalar()!;
        return count > 0;
    }

    public void Insert(Post post)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO posts (id, title, link, description) VALUES (@id, @title, @link, @description)";
        cmd.Parameters.AddWithValue("@id", post.Id);
        cmd.Parameters.AddWithValue("@title", post.Title);
        cmd.Parameters.AddWithValue("@link", post.Link);
        cmd.Parameters.AddWithValue("@description", post.Description);
        cmd.ExecuteNonQuery();
    }

    public int DeleteWithoutAnnouncements()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"DELETE FROM posts
              WHERE NOT EXISTS (
                  SELECT 1 FROM announcements AS a
                  WHERE a.id = posts.id
              )";
        return cmd.ExecuteNonQuery();
    }
}
