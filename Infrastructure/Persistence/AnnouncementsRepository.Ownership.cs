using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

public partial class AnnouncementsRepository
{
    internal bool CanManage(long id, AnnouncementActor actor)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var announcement = GetInTransaction(connection, transaction, id);
        return announcement is not null && CanManageInTransaction(connection, transaction, announcement, actor);
    }

    internal AnnouncementMutationResult EditAuthorized(long id, AnnouncementActor actor,
        Func<Announcement, (bool Success, string Message)> mutator)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var announcement = GetInTransaction(connection, transaction, id);
        if (announcement is null) return new(AnnouncementMutationStatus.NotFound);
        if (!CanManageInTransaction(connection, transaction, announcement, actor)) return new(AnnouncementMutationStatus.Denied);
        var before = EditableValues(announcement);
        var userId = announcement.UserId;
        var (success, message) = mutator(announcement);
        if (!success) return new(AnnouncementMutationStatus.Invalid, message, announcement);
        // Владелец и идентификатор не являются редактируемыми полями.
        announcement.Id = id;
        announcement.UserId = userId;
        if (before == EditableValues(announcement))
            return new(AnnouncementMutationStatus.Unchanged, message, announcement);
        UpdateInTransaction(connection, transaction, announcement, actor.UserId);
        transaction.Commit();
        return new(AnnouncementMutationStatus.Changed, message, announcement);
    }

    internal AnnouncementMutationResult DeleteAuthorized(long id, AnnouncementActor actor)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var announcement = GetInTransaction(connection, transaction, id);
        if (announcement is null) return new(AnnouncementMutationStatus.NotFound);
        if (!CanManageInTransaction(connection, transaction, announcement, actor)) return new(AnnouncementMutationStatus.Denied);
        var source = GetSourceLink(connection, transaction, id);
        if (!string.IsNullOrWhiteSpace(source))
        {
            var isLiveJournal = LiveJournalIdentity.TryNormalizeLink(source, out var link, out var postId);
            using var marker = connection.CreateCommand();
            marker.Transaction = transaction;
            marker.CommandText = """
                INSERT INTO announcement_deletions(normalized_link,post_id,deleted_by_user_id,deleted_at_utc)
                VALUES(@link,@post,@user,@now)
                ON CONFLICT(normalized_link) DO UPDATE SET deleted_by_user_id=excluded.deleted_by_user_id,
                    deleted_at_utc=excluded.deleted_at_utc
                """;
            marker.Parameters.AddWithValue("@link", isLiveJournal ? link : LinkNormalizer.Normalize(source));
            marker.Parameters.AddWithValue("@post", isLiveJournal ? postId : DBNull.Value);
            marker.Parameters.AddWithValue("@user", (object?)actor.UserId ?? DBNull.Value);
            marker.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            marker.ExecuteNonQuery();
        }
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM external_posts WHERE announcementId=@id; DELETE FROM announcements WHERE id=@id";
        delete.Parameters.AddWithValue("@id", id);
        delete.ExecuteNonQuery();
        transaction.Commit();
        return new(AnnouncementMutationStatus.Changed, Announcement: announcement);
    }

    internal bool IsAutomationSuppressed(long postId, string? link = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM announcement_deletions WHERE post_id=@post OR normalized_link=@link)";
        command.Parameters.AddWithValue("@post", postId);
        command.Parameters.AddWithValue("@link", LiveJournalIdentity.TryNormalizeLink(link, out var canonical, out _)
            ? canonical : LinkNormalizer.Normalize(link));
        return (long)command.ExecuteScalar()! != 0;
    }

    internal bool TryInsertAutomatically(Announcement announcement, string? sourceLink)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var link = LiveJournalIdentity.TryNormalizeLink(sourceLink, out var canonical, out _) ? canonical : LinkNormalizer.Normalize(sourceLink);
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM announcement_deletions WHERE post_id=@id OR normalized_link=@link)
                OR EXISTS(SELECT 1 FROM announcements WHERE id=@id)
                OR EXISTS(SELECT 1 FROM external_posts e JOIN announcements a ON a.id=e.announcementId
                    WHERE e.normalizedLink=@link OR e.link=@link)
            """;
        command.Parameters.AddWithValue("@id", announcement.Id);
        command.Parameters.AddWithValue("@link", link);
        if ((long)command.ExecuteScalar()! != 0) return false;
        command.CommandText = """
            INSERT INTO announcements(id,tournamentName,place,dateTimeUtc,cost,cost_text,userId)
            VALUES(@id,@name,@place,@date,@cost,@costText,@user)
            """;
        command.Parameters.AddWithValue("@name", announcement.TournamentName);
        command.Parameters.AddWithValue("@place", announcement.Place);
        command.Parameters.AddWithValue("@date", announcement.DateTimeUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@cost", announcement.Cost);
        command.Parameters.AddWithValue("@costText", (object?)announcement.CostLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("@user", (object?)announcement.UserId ?? DBNull.Value);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    /// <summary>Проверяет текущий бан, прежнее владение и разрешение по автору исходной ссылки.</summary>
    private static bool CanManageInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        Announcement announcement, AnnouncementActor actor)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM banned_users WHERE userId=@user)";
        command.Parameters.AddWithValue("@user", (object?)actor.UserId ?? DBNull.Value);
        if ((long)command.ExecuteScalar()! != 0) return false;
        if (actor.IsAdminChat) return true;
        if (actor.UserId is null) return false;
        if (announcement.UserId == actor.UserId) return true;
        if (!actor.IsPrivateChat) return false;
        var source = GetSourceLink(connection, transaction, announcement.Id);
        if (!LiveJournalIdentity.TryNormalizeLink(source, out var link, out _)) return false;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM allowed_users a
                JOIN telegram_lj_users l ON l.telegram_user_id=a.userId
                JOIN lj_post_authors p ON p.lj_user_id=l.lj_user_id
                WHERE a.userId=@user AND p.normalized_link=@link)
            """;
        command.Parameters.AddWithValue("@link", link);
        return (long)command.ExecuteScalar()! != 0;
    }

    /// <summary>Возвращает фактическую внешнюю ссылку, отдавая ей приоритет перед совпадением внутренних ID.</summary>
    private static string? GetSourceLink(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='posts')";
        var hasPosts = (long)command.ExecuteScalar()! != 0;
        command.CommandText = hasPosts
            ? "SELECT COALESCE(e.link,p.link) FROM announcements a LEFT JOIN external_posts e ON e.announcementId=a.id LEFT JOIN posts p ON p.id=a.id WHERE a.id=@id"
            : "SELECT link FROM external_posts WHERE announcementId=@id";
        command.Parameters.AddWithValue("@id", id);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Снимок изменяемых полей для определения фактических правок.</summary>
    private static (string, string, DateTime, int, string?) EditableValues(Announcement a) =>
        (a.TournamentName, a.Place, a.DateTimeUtc, a.Cost, a.CostLabel);
}
