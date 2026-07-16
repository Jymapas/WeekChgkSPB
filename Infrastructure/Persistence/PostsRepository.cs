using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

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
                description TEXT,
                normalizedLink TEXT
            )";
        cmd.ExecuteNonQuery();

        EnsureColumnExists(connection, "posts", "normalizedLink", "TEXT");
        BackfillNormalizedLinks(connection);
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

    private static void BackfillNormalizedLinks(SqliteConnection connection)
    {
        var rows = new List<(long Id, string Normalized)>();
        using var select = connection.CreateCommand();
        select.CommandText = @"SELECT id, link FROM posts WHERE normalizedLink IS NULL OR normalizedLink = ''";
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
        update.CommandText = "UPDATE posts SET normalizedLink=@normalized WHERE id=@id";
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
        var raw = link?.Trim() ?? string.Empty;
        var normalized = LinkNormalizer.Normalize(raw);
        cmd.CommandText = @"SELECT id FROM posts WHERE normalizedLink=@normalized OR link=@raw LIMIT 1";
        cmd.Parameters.AddWithValue("@normalized", normalized);
        cmd.Parameters.AddWithValue("@raw", raw);
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
        cmd.CommandText =
            "INSERT INTO posts (id, title, link, description, normalizedLink) VALUES (@id, @title, @link, @description, @normalizedLink)";
        cmd.Parameters.AddWithValue("@id", post.Id);
        cmd.Parameters.AddWithValue("@title", post.Title);
        cmd.Parameters.AddWithValue("@link", post.Link);
        cmd.Parameters.AddWithValue("@description", post.Description);
        cmd.Parameters.AddWithValue("@normalizedLink", LinkNormalizer.Normalize(post.Link));
        cmd.ExecuteNonQuery();
    }

    internal IReadOnlyList<AnnouncementNameExample> FindNameExamples(string? sourceTitle, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        var tokens = (sourceTitle ?? string.Empty)
            .Split([' ', '-', '—', '–', ':', '«', '»', '"'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Select(token => token.ToLowerInvariant())
            .ToHashSet();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.title, a.tournamentName
            FROM announcements a
            JOIN posts p ON p.id = a.id
            WHERE p.title IS NOT NULL AND p.title <> ''
            ORDER BY a.dateTimeUtc DESC
            LIMIT 100
            """;
        using var reader = command.ExecuteReader();
        var examples = new List<(AnnouncementNameExample Example, int Score)>();
        while (reader.Read())
        {
            var title = reader.GetString(0);
            var score = tokens.Count(token => title.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (score > 0)
            {
                examples.Add((new AnnouncementNameExample(title, reader.GetString(1)), score));
            }
        }

        return examples
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Example.SourceTitle, StringComparer.Ordinal)
            .Take(Math.Min(limit, 3))
            .Select(item => item.Example)
            .ToList();
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
