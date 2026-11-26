using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB;

internal sealed class RssPoller(RssFetcher fetcher, PostsRepository repo,
    AnnouncementsRepository announcementsRepo, LiveJournalIdentityRepository identities,
    AnnouncementAutomationProcessor automationProcessor)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> CheckAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var newCount = 0;
            var failures = 0;
            var feedPosts = fetcher.FetchPosts();
            var feedIds = feedPosts
                .Where(p => p.Id != 0)
                .Select(p => p.Id)
                .ToList();

            foreach (var post in feedPosts.Where(p => p.Id != 0))
            {
                identities.SaveAuthor(post);
                var isNewPost = !repo.Exists(post.Id);
                var isVenueChange = AnnouncementPreParser.IsVenueChange(post);
                var hasAnnouncement = announcementsRepo.Exists(post.Id);
                if (!hasAnnouncement && !string.IsNullOrWhiteSpace(post.Link))
                {
                    hasAnnouncement = announcementsRepo.GetByLink(post.Link) is not null;
                }

                if (isNewPost)
                {
                    repo.Insert(post);
                    newCount++;
                }

                if (hasAnnouncement && !isVenueChange)
                {
                    continue;
                }

                if (!automationProcessor.ShouldProcessPost(post.Id, isNewPost, isVenueChange))
                {
                    continue;
                }

                Console.WriteLine($"Processing post: {post.Id} — {post.Title}");
                try
                {
                    await automationProcessor.ProcessAsync(post, DateTime.UtcNow, ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    failures++;
                    Console.WriteLine($"Announcement processing failed: {e.Message}");
                }

                await Task.Delay(250, ct);
            }

            repo.DeleteWithoutAnnouncementsNotInFeed(feedIds);
            return $"Проверка ЖЖ завершена. Новых постов: {newCount}. Ошибок обработки: {failures}.";
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"RSS/DB error: {e.Message}");
            return "Не удалось завершить проверку ЖЖ.";
        }
        finally
        {
            gate.Release();
        }
    }
}
