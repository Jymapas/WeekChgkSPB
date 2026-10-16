using System;
using System.IO;
using Microsoft.Data.Sqlite;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementParseAttemptsRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"weekchgk-audit-{Guid.NewGuid():N}.db");

    [Fact]
    public void Constructor_MigratesEarlierTableAndUpsertWorks()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE announcement_parse_attempts (
                    post_id INTEGER PRIMARY KEY,
                    mode TEXT NOT NULL,
                    outcome TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                )
                """;
            command.ExecuteNonQuery();
        }

        var repository = new AnnouncementParseAttemptsRepository(_dbPath);
        repository.Upsert(new AnnouncementParseAttempt(
            12, "shadow", "fallback", "cost_not_found", null, null,
            900, 0, "alibaba_model_studio", AnnouncementAutomationOptions.DefaultModel, null, null, null));

        using var verify = new SqliteConnection($"Data Source={_dbPath}");
        verify.Open();
        using var select = verify.CreateCommand();
        select.CommandText = "SELECT failure_code, source_length, provider FROM announcement_parse_attempts WHERE post_id=12";
        using var reader = select.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("cost_not_found", reader.GetString(0));
        Assert.Equal(900, reader.GetInt32(1));
        Assert.Equal("alibaba_model_studio", reader.GetString(2));
        Assert.True(repository.Exists(12));
        Assert.False(repository.Exists(13));
    }

    [Fact]
    public void Approval_PreservesOriginalFailureCode()
    {
        var repository = new AnnouncementParseAttemptsRepository(_dbPath);
        repository.Upsert(new AnnouncementParseAttempt(
            42, "shadow", "review_pending", "datetime_mismatch", 1800, "1800 ₽",
            900, 120, "alibaba_model_studio", AnnouncementAutomationOptions.DefaultModel, null, null, null));
        repository.SetOutcome(42, "review_added");
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT failure_code FROM announcement_parse_attempts WHERE post_id=42";
        Assert.Equal("datetime_mismatch", command.ExecuteScalar());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
