using System;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

public sealed class SqliteFixture : IDisposable
{
    private string _databasePath;
    public string DatabasePath => _databasePath;

    public SqliteFixture()
    {
        _databasePath = CreateNewPath();
    }

    public void Reset()
    {
        TryDeleteCurrent();
        _databasePath = CreateNewPath();
    }

    public PostsRepository CreatePostsRepository() => new(_databasePath);
    public AnnouncementsRepository CreateAnnouncementsRepository() => new(_databasePath);
    public FootersRepository CreateFootersRepository() => new(_databasePath);
    public ChannelPostsRepository CreateChannelPostsRepository() => new(_databasePath);
    public UserManagementRepository CreateUserManagementRepository() => new(_databasePath);

    public void Dispose()
    {
        TryDeleteCurrent();
    }

    private static string CreateNewPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"weekchgk_tests_{Guid.NewGuid():N}.db");

    private void TryDeleteCurrent()
    {
        try
        {
            if (System.IO.File.Exists(_databasePath))
            {
                System.IO.File.Delete(_databasePath);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}
