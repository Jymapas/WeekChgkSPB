using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

internal class Program
{
    private const string DbPath = "posts.db";
    private const string RssUrl = "https://chgk-spb.livejournal.com/data/rss";

    public static async Task Main()
    {
        var repo = new PostsRepository(DbPath);
        var fetcher = new RssFetcher(RssUrl);

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        var chatIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(chatIdVar, out var chatId))
        {
            Console.WriteLine("Telegram notifier disabled: set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID");
            return;
        }

        INotifier notifier = new TelegramNotifier(token, chatId);
        Console.WriteLine("Telegram notifier enabled");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await CheckOnceAsync(fetcher, repo, notifier, cts.Token);

        var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                await CheckOnceAsync(fetcher, repo, notifier, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            /* graceful shutdown */
        }
    }

    private static async Task CheckOnceAsync(RssFetcher fetcher, PostsRepository repo, INotifier notifier, CancellationToken ct)
    {
        try
        {
            var posts = fetcher.FetchPosts();
            foreach (var post in posts.Where(p => p.Id != 0 && !repo.Exists(p.Id)))
            {
                repo.Insert(post);
                Console.WriteLine($"New post: {post.Id} — {post.Title}");

                try
                {
                    await notifier.NotifyNewPostAsync(post, ct);
                    await Task.Delay(250, ct);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Telegram send failed: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"RSS/DB error: {e.Message}");
        }
    }
}