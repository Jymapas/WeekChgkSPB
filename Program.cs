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

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        var chatIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(chatIdVar, out var chatId))
        {
            Console.WriteLine("Telegram notifier disabled: set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID");
            return;
        }

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new PostsRepository(dbPath));
                services.AddSingleton(new FootersRepository(dbPath));
                services.AddSingleton(new AnnouncementsRepository(dbPath));
                services.AddSingleton(new RssFetcher(RssUrl));
                services.AddSingleton<INotifier>(_ => new TelegramNotifier(token, chatId));
                services.AddSingleton(_ => new BotCommandHelper(PostFormatter.Moscow));
                services.AddSingleton<BotConversationState>();
                services.AddSingleton<IConversationFlowHandler, AddAnnouncementFlow>();
                services.AddSingleton<IConversationFlowHandler, EditAnnouncementFlow>();
                services.AddSingleton<IConversationFlowHandler, FooterFlow>();
                services.AddSingleton<IBotCommandHandler>(sp => new MakePostCommandHandler(BotCommands.MakePostLJ, true));
                services.AddSingleton<IBotCommandHandler>(sp => new MakePostCommandHandler(BotCommands.MakePost, false));
                services.AddSingleton<IBotCommandHandler, AddLinesCommandHandler>();
                services.AddSingleton<IBotCommandHandler, AddCommandHandler>();
                services.AddSingleton<IBotCommandHandler, EditNameCommandHandler>();
                services.AddSingleton<IBotCommandHandler, EditPlaceCommandHandler>();
                services.AddSingleton<IBotCommandHandler, EditDateTimeCommandHandler>();
                services.AddSingleton<IBotCommandHandler, EditCostCommandHandler>();
                services.AddSingleton<IBotCommandHandler, EditCommandHandler>();
                services.AddSingleton<IBotCommandHandler, DeleteAnnouncementCommandHandler>();
                services.AddSingleton<IBotCommandHandler, FooterAddCommandHandler>();
                services.AddSingleton<IBotCommandHandler, FooterListCommandHandler>();
                services.AddSingleton<IBotCommandHandler, FooterDeleteCommandHandler>();
                services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));
                services.AddSingleton(sp => new BotRunner(
                    sp.GetRequiredService<ITelegramBotClient>(),
                    chatId,
                    sp.GetRequiredService<PostsRepository>(),
                    sp.GetRequiredService<AnnouncementsRepository>(),
                    sp.GetRequiredService<FootersRepository>(),
                    sp.GetRequiredService<BotCommandHelper>(),
                    sp.GetRequiredService<BotConversationState>(),
                    sp.GetServices<IBotCommandHandler>(),
                    sp.GetServices<IConversationFlowHandler>()));
            })
            .Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        var repo = services.GetRequiredService<PostsRepository>();
        var fetcher = services.GetRequiredService<RssFetcher>();
        var notifier = services.GetRequiredService<INotifier>();
        var botRunner = services.GetRequiredService<BotRunner>();
        var botClient = services.GetRequiredService<ITelegramBotClient>();

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
