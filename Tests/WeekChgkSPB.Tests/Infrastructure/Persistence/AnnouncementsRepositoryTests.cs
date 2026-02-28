using System;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Persistence;

public class AnnouncementsRepositoryTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public AnnouncementsRepositoryTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void InsertGetUpdateDelete_Works()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        var postsRepo = _fixture.CreatePostsRepository();

        var announcement = new Announcement
        {
            Id = 42,
            TournamentName = "Name",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        };

        repo.Insert(announcement);

        Assert.True(repo.Exists(42));

        var fetched = repo.Get(42);
        Assert.NotNull(fetched);
        Assert.Equal("Name", fetched!.TournamentName);
        Assert.Equal("Place", fetched.Place);
        Assert.Equal(100, fetched.Cost);

        fetched.TournamentName = "Updated";
        fetched.Cost = 200;
        fetched.Place = "New Place";
        repo.Update(fetched);

        var updated = repo.Get(42);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.TournamentName);
        Assert.Equal("New Place", updated.Place);
        Assert.Equal(200, updated.Cost);

        postsRepo.Insert(new Post { Id = 42, Title = "Title", Link = "https://example.com", Description = "desc" });

        var list = repo.GetWithLinksInRange(
            fetched.DateTimeUtc.AddHours(-1),
            fetched.DateTimeUtc.AddHours(1));
        Assert.Single(list);
        Assert.Equal(42, list[0].Id);

        Assert.True(repo.Delete(42));
        Assert.False(repo.Exists(42));
        Assert.Null(repo.Get(42));
    }

    [Fact]
    public void GetByLink_NormalizesTrackingParameters()
    {
        _fixture.Reset();
        var repo = _fixture.CreateAnnouncementsRepository();
        _ = _fixture.CreatePostsRepository();

        var announcement = new Announcement
        {
            TournamentName = "Name",
            Place = "Place",
            DateTimeUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        };

        repo.InsertExternal(announcement, "https://example.com/post?utm_source=a&b=1");

        var fetched = repo.GetByLink("https://example.com/post?b=1");

        Assert.NotNull(fetched);
        Assert.Equal("Name", fetched!.TournamentName);
    }
}
