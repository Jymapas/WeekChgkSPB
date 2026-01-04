using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public bool TryGetIdByLink(string link, out long id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id FROM posts WHERE link=@link LIMIT 1";
        cmd.Parameters.AddWithValue("@link", link);
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            id = 0;
            return false;
        }

        id = (long)result;
        return true;
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

    public int DeleteWithoutAnnouncementsNotInFeed(IReadOnlyCollection<long> rssIds)
    {
        if (rssIds is null || rssIds.Count == 0)
        {
            return 0;
        }

        var keepIds = rssIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (keepIds.Count == 0)
        {
            return 0;
        }

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        var placeholders = new List<string>(keepIds.Count);
        for (var i = 0; i < keepIds.Count; i++)
        {
            var paramName = $"@id{i}";
            placeholders.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, keepIds[i]);
        }

        var keepClause = string.Join(", ", placeholders);
        cmd.CommandText =
            $@"DELETE FROM posts
               WHERE NOT EXISTS (
                   SELECT 1 FROM announcements AS a
                   WHERE a.id = posts.id
               )
               AND posts.id NOT IN ({keepClause})";
        return cmd.ExecuteNonQuery();
    }
}
