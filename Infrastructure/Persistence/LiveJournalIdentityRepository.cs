using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

/// <summary>Хранит достоверное авторство источников ЖЖ и назначенных редакторов.</summary>
internal sealed class LiveJournalIdentityRepository
{
    private readonly string _dbPath;

    internal LiveJournalIdentityRepository(string dbPath)
    {
        _dbPath = dbPath;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var connection = Open();
        AnnouncementOwnershipSchema.EnsureCreated(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS allowed_users (userId INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS banned_users (userId INTEGER PRIMARY KEY);
            """;
        command.ExecuteNonQuery();
    }

    internal bool SaveAuthor(Post post)
    {
        if (post.LjAuthorId is not > 0 || !LiveJournalIdentity.TryNormalizeLogin(post.LjAuthorUsername, out var login) ||
            !LiveJournalIdentity.TryNormalizeLink(post.Link, out var link, out var postId) || postId != post.Id)
            return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lj_post_authors(normalized_link, post_id, lj_user_id, lj_username)
            VALUES(@link, @post, @author, @login)
            ON CONFLICT(normalized_link) DO UPDATE SET lj_username=excluded.lj_username
            WHERE lj_post_authors.lj_user_id=excluded.lj_user_id AND lj_post_authors.post_id=excluded.post_id
            """;
        command.Parameters.AddWithValue("@link", link);
        command.Parameters.AddWithValue("@post", postId);
        command.Parameters.AddWithValue("@author", post.LjAuthorId.Value);
        command.Parameters.AddWithValue("@login", login);
        return command.ExecuteNonQuery() > 0;
    }

    internal LjBindingResult Bind(long telegramUserId, string login, long? linkedByUserId, bool allow)
    {
        if (telegramUserId <= 0 || !LiveJournalIdentity.TryNormalizeLogin(login, out login))
            return LjBindingResult.UnknownAuthor;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM banned_users WHERE userId=@user)";
        command.Parameters.AddWithValue("@user", telegramUserId);
        if ((long)command.ExecuteScalar()! != 0) return LjBindingResult.BannedUser;

        command.CommandText = "SELECT DISTINCT lj_user_id FROM lj_post_authors WHERE lj_username=@login COLLATE NOCASE LIMIT 2";
        command.Parameters.AddWithValue("@login", login);
        var authors = new List<long>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) authors.Add(reader.GetInt64(0));
        if (authors.Count == 0) return LjBindingResult.UnknownAuthor;
        if (authors.Count != 1) return LjBindingResult.AmbiguousAuthor;

        command.CommandText = """
            INSERT INTO telegram_lj_users(telegram_user_id, lj_user_id, lj_username, linked_by_user_id, linked_at_utc)
            VALUES(@user,@author,@login,@editor,@now)
            ON CONFLICT(telegram_user_id,lj_user_id) DO NOTHING
            """;
        command.Parameters.AddWithValue("@author", authors[0]);
        command.Parameters.AddWithValue("@editor", (object?)linkedByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        if (allow)
        {
            command.CommandText = "INSERT OR IGNORE INTO allowed_users(userId) VALUES(@user)";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return LjBindingResult.Linked;
    }

    internal bool Unbind(long telegramUserId, string login)
    {
        if (!LiveJournalIdentity.TryNormalizeLogin(login, out login)) return false;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT lj_user_id FROM telegram_lj_users
            WHERE telegram_user_id=@user AND (lj_username=@login COLLATE NOCASE OR lj_user_id IN (
                SELECT lj_user_id FROM lj_post_authors WHERE lj_username=@login COLLATE NOCASE)) LIMIT 2
            """;
        command.Parameters.AddWithValue("@user", telegramUserId);
        command.Parameters.AddWithValue("@login", login);
        var authors = new List<long>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) authors.Add(reader.GetInt64(0));
        if (authors.Count != 1) return false;
        command.CommandText = "DELETE FROM telegram_lj_users WHERE telegram_user_id=@user AND lj_user_id=@author";
        command.Parameters.AddWithValue("@author", authors[0]);
        var removed = command.ExecuteNonQuery() > 0;
        transaction.Commit();
        return removed;
    }

    internal IReadOnlyList<LiveJournalUserLink> List(long? telegramUserId = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.telegram_user_id, l.lj_user_id, l.lj_username, l.linked_by_user_id, l.linked_at_utc,
                EXISTS(SELECT 1 FROM allowed_users a WHERE a.userId=l.telegram_user_id),
                EXISTS(SELECT 1 FROM banned_users b WHERE b.userId=l.telegram_user_id)
            FROM telegram_lj_users l
            WHERE @user IS NULL OR l.telegram_user_id=@user
            ORDER BY l.telegram_user_id,l.lj_username,l.lj_user_id
            """;
        command.Parameters.AddWithValue("@user", (object?)telegramUserId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var links = new List<LiveJournalUserLink>();
        while (reader.Read())
            links.Add(new LiveJournalUserLink(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                DateTime.Parse(reader.GetString(4), null, DateTimeStyles.AdjustToUniversal),
                reader.GetBoolean(5), reader.GetBoolean(6)));
        return links;
    }

    /// <summary>Открывает соединение с рабочей SQLite.</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }
}
