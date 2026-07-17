using System;
using System.IO;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementReviewDraftRepositoryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"weekchgk-review-{Guid.NewGuid():N}.db");

    [Fact]
    public void CreatesSchemaAndPersistsNullableDraft()
    {
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        repository.Upsert(new AnnouncementReviewDraft
        {
            PostId = 42,
            Place = "Rossi's",
            Cost = 1950,
            FailureCode = "json_invalid"
        });

        var stored = repository.Get(42);

        Assert.NotNull(stored);
        Assert.Null(stored!.TournamentName);
        Assert.Equal("Rossi's", stored.Place);
        Assert.Equal(1950, stored.Cost);
        Assert.False(stored.IsComplete);
        Assert.Equal(AnnouncementReviewStatuses.Pending, stored.Status);
    }

    [Fact]
    public void UpsertPreservesAlreadyStoredTelegramMessageIds()
    {
        var repository = new AnnouncementReviewDraftRepository(_dbPath);
        var draft = new AnnouncementReviewDraft { PostId = 43, Cost = 1800 };
        repository.Upsert(draft);
        repository.SetSourceMessageId(43, 10);
        repository.SetReviewMessageId(43, 11);

        draft.TournamentName = "Кубок";
        repository.Upsert(draft);

        var stored = repository.Get(43);
        Assert.Equal(10, stored!.SourceMessageId);
        Assert.Equal(11, stored.ReviewMessageId);
        Assert.Equal("Кубок", stored.TournamentName);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
