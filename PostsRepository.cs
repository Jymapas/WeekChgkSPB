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

    public bool Exists(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM posts WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var count = (int)cmd.ExecuteScalar()!;
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
}