using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using Telegram.Bot;
using Telegram.Bot.Types;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Infrastructure.Configuration;

internal class Program
{
    private const string RssUrl = "https://chgk-spb.livejournal.com/data/rss";

    public static async Task Main()
    {
        Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

        if (!TryLoadSettings(out var settings))
        {
            return;
        }

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWeekChgkSpbServices(settings, RssUrl))
            .Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        var repo = services.GetRequiredService<PostsRepository>();
        var fetcher = services.GetRequiredService<RssFetcher>();
        var notifier = services.GetRequiredService<INotifier>();
        var botRunner = services.GetRequiredService<BotRunner>();
        var botClient = services.GetRequiredService<ITelegramBotClient>();
        var scheduler = services.GetService<ScheduledPostPublisher>();

        Console.WriteLine("Telegram notifier enabled");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var me = await botClient.GetMe(cts.Token);
        Console.WriteLine(me.Username);

        var commands = BotCommands.AsBotCommands();
        await botClient.SetMyCommands(
            commands: commands,
            scope: BotCommandScope.AllGroupChats(),
            cancellationToken: cts.Token);
        botRunner.Start(cts.Token);

        await CheckOnceAsync(fetcher, repo, notifier, cts.Token);
        var lastRssCheck = DateTime.UtcNow;

        if (scheduler is not null)
        {
            await scheduler.TryPublishAsync(DateTime.UtcNow, cts.Token);
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                if (DateTime.UtcNow - lastRssCheck >= TimeSpan.FromHours(1))
                {
                    await CheckOnceAsync(fetcher, repo, notifier, cts.Token);
                    lastRssCheck = DateTime.UtcNow;
                }

                if (scheduler is not null)
                {
                    await scheduler.TryPublishAsync(DateTime.UtcNow, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* bye */
        }

        await host.StopAsync();
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

    private static bool TryLoadSettings(out AppSettings settings)
    {
        var dbPath = ResolveDbPath(
            Environment.GetEnvironmentVariable("DB_PATH"),
            AppContext.BaseDirectory);
        Console.WriteLine($"DB_PATH resolved to: {dbPath}");

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        var chatIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(chatIdVar, out var chatId))
        {
            Console.WriteLine("Telegram notifier disabled: set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID");
            settings = default!;
            return false;
        }

        string? channelId = null;
        ChannelPostScheduleOptions? scheduleOptions = null;
        var channelIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHANNEL_ID");
        if (!string.IsNullOrWhiteSpace(channelIdVar))
        {
            var trimmedChannelId = channelIdVar.Trim();
            var perWeekVar = Environment.GetEnvironmentVariable("TELEGRAM_CHANNEL_POSTS_PER_WEEK") ?? "2";
            var daysVar = Environment.GetEnvironmentVariable("TELEGRAM_CHANNEL_POST_DAYS") ?? "Monday,Thursday";
            var timeVar = Environment.GetEnvironmentVariable("TELEGRAM_CHANNEL_POST_TIME") ?? "12:00";
            var triggerWindowVar = Environment.GetEnvironmentVariable("TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES");

            try
            {
                scheduleOptions = ChannelPostScheduleOptions.FromStrings(
                    perWeekVar,
                    daysVar,
                    timeVar,
                    triggerWindowVar);

                if (long.TryParse(trimmedChannelId, out var parsedChannelId))
                {
                    channelId = trimmedChannelId;
                    Console.WriteLine($"Telegram channel scheduler configured for chat {parsedChannelId}.");
                }
                else if (trimmedChannelId.StartsWith('@'))
                {
                    channelId = trimmedChannelId;
                    Console.WriteLine($"Telegram channel scheduler configured for channel {trimmedChannelId}.");
                }
                else
                {
                    Console.WriteLine("Telegram channel scheduler disabled: TELEGRAM_CHANNEL_ID is invalid.");
                    channelId = null;
                    scheduleOptions = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram channel scheduler disabled: {ex.Message}");
                scheduleOptions = null;
            }
        }
        else
        {
            Console.WriteLine("Telegram channel scheduler disabled: TELEGRAM_CHANNEL_ID not set.");
        }

        settings = new AppSettings(dbPath, token, chatId, channelId, scheduleOptions);
        return true;
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
