using DotNetEnv;
using System.Runtime.InteropServices;
using Telegram.Bot;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Notifications;

internal class Program
{
    private const string RssUrl = "https://chgk-spb.livejournal.com/data/rss";

    public static async Task Main()
    {
        Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

        var dbPath = ResolveDbPath(
            Environment.GetEnvironmentVariable("DB_PATH"),
            AppContext.BaseDirectory);
        Console.WriteLine($"DB_PATH resolved to: {dbPath}");

        var repo = new PostsRepository(dbPath);
        var annRepo = new AnnouncementsRepository(dbPath);
        var fetcher = new RssFetcher(RssUrl);

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        var chatIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(chatIdVar, out var chatId))
        {
            Console.WriteLine("Telegram notifier disabled: set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID");
            return;
        }

        var notifier = new TelegramNotifier(token, chatId);
        Console.WriteLine("Telegram notifier enabled");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var botClient = new TelegramBotClient(token);

        Console.WriteLine(botClient.GetMe().Result.Username); // бот не обновился!

        var runner = new BotRunner(botClient, chatId, repo, annRepo);
        runner.Start(cts.Token);

        await CheckOnceAsync(fetcher, repo, notifier, cts.Token);

        var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
                await CheckOnceAsync(fetcher, repo, notifier, cts.Token);
        }
        catch (OperationCanceledException)
        {
            /* bye */
        }
    }

    private static async Task CheckOnceAsync(RssFetcher fetcher, PostsRepository repo, INotifier notifier,
        CancellationToken ct)
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
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Telegram send failed: {e.Message}");
                }

                await Task.Delay(250, ct);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"RSS/DB error: {e.Message}");
        }
    }

    private static string ResolveDbPath(string? envPath, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(envPath))
            return Path.Combine(baseDir, "posts.db");

        if (!Path.IsPathRooted(envPath))
            return Path.Combine(baseDir, envPath);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !envPath.StartsWith('/'))
        {
            return envPath;
        }

        var trimmed = envPath.TrimStart('/', '\\');
        return Path.Combine(baseDir, trimmed);

    }
}