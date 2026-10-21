using System;
using Microsoft.Data.Sqlite;

namespace WeekChgkSPB.Tests.Infrastructure.Persistence;

public sealed class NativeSqliteSecurityTests
{
    [Fact]
    public void LoadedNativeEngine_IncludesSecurityFixes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);

        // Check the engine that actually loaded, including the 3.54.1 overflow fix.
        Assert.True(version >= new Version(3, 54, 1), $"Loaded SQLite {version}");
    }
}
