using System;
using WeekChgkSPB;
using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;
using Xunit;

namespace WeekChgkSPB.Tests.Infrastructure.Persistence;

public class PostsRepositoryTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public PostsRepositoryTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DeleteWithoutAnnouncementsNotInFeed_RemovesOnlyOrphansOutsideFeed()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();
        var announcements = _fixture.CreateAnnouncementsRepository();

        posts.Insert(new Post { Id = 1, Title = "Keep A", Link = "https://example.com/a" });
        posts.Insert(new Post { Id = 2, Title = "Keep B", Link = "https://example.com/b" });
        posts.Insert(new Post { Id = 3, Title = "Drop", Link = "https://example.com/drop" });

        announcements.Insert(new Announcement
        {
            Id = 2,
            TournamentName = "Upcoming",
            Place = "Somewhere",
            DateTimeUtc = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Cost = 100
        });

        var removed = posts.DeleteWithoutAnnouncementsNotInFeed(new long[] { 1, 4 });

        Assert.Equal(1, removed);
        Assert.True(posts.Exists(1));
        Assert.True(posts.Exists(2));
        Assert.False(posts.Exists(3));
    }

    [Fact]
    public void DeleteWithoutAnnouncementsNotInFeed_DoesNothing_WhenFeedIdsMissing()
    {
        _fixture.Reset();
        var posts = _fixture.CreatePostsRepository();

        posts.Insert(new Post { Id = 10, Title = "Post", Link = "https://example.com/post" });

        var removed = posts.DeleteWithoutAnnouncementsNotInFeed(Array.Empty<long>());

        Assert.Equal(0, removed);
        Assert.True(posts.Exists(10));
    }
}
