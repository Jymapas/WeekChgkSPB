using Microsoft.Data.Sqlite;

namespace WeekChgkSPB;

internal static class AnnouncementOwnershipSchema
{
    internal static void EnsureCreated(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS lj_post_authors (
                normalized_link TEXT PRIMARY KEY,
                post_id INTEGER NOT NULL UNIQUE CHECK(post_id > 0),
                lj_user_id INTEGER NOT NULL CHECK(lj_user_id > 0),
                lj_username TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_lj_post_authors_username ON lj_post_authors(lj_username COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS telegram_lj_users (
                telegram_user_id INTEGER NOT NULL CHECK(telegram_user_id > 0),
                lj_user_id INTEGER NOT NULL CHECK(lj_user_id > 0),
                lj_username TEXT NOT NULL,
                linked_by_user_id INTEGER,
                linked_at_utc TEXT NOT NULL,
                PRIMARY KEY(telegram_user_id, lj_user_id)
            );
            CREATE TABLE IF NOT EXISTS announcement_deletions (
                normalized_link TEXT PRIMARY KEY,
                post_id INTEGER,
                deleted_by_user_id INTEGER,
                deleted_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_announcement_deletions_post ON announcement_deletions(post_id);
            """;
        command.ExecuteNonQuery();
    }
}
