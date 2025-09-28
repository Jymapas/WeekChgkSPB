using System;
using System.IO;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

internal sealed class SqliteTempFile : IDisposable
{
    public string Path { get; }

    public SqliteTempFile()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"weekchgk_tests_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}
