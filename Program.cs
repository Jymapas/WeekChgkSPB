using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;
using WeekChgkSPB.Infrastructure.Configuration;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

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
        var announcementsRepo = services.GetRequiredService<AnnouncementsRepository>();
        var fetcher = services.GetRequiredService<RssFetcher>();
        var automationProcessor = services.GetRequiredService<AnnouncementAutomationProcessor>();
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

        if (settings.HasChannel)
        {
            var hasAccess = await EnsureChannelAccessAsync(
                botClient,
                settings.ChannelId!,
                me.Id,
                cts.Token);

            if (!hasAccess)
            {
                Console.WriteLine("Channel access validation failed.");
                await host.StopAsync();
                return;
            }
        }

        await botClient.DeleteMyCommands(
            scope: new BotCommandScopeAllGroupChats(),
            cancellationToken: cts.Token);

        var userCommands = BotCommands.AsUserBotCommands();
        await botClient.SetMyCommands(
            commands: userCommands,
            scope: new BotCommandScopeAllPrivateChats(),
            cancellationToken: cts.Token);

        var adminCommands = BotCommands.AsAdminBotCommands();
        await botClient.SetMyCommands(
            commands: adminCommands,
            scope: new BotCommandScopeChat { ChatId = settings.ChatId },
            cancellationToken: cts.Token);
        botRunner.Start(cts.Token);

        await CheckOnceAsync(fetcher, repo, announcementsRepo, automationProcessor, cts.Token);
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
                    await CheckOnceAsync(fetcher, repo, announcementsRepo, automationProcessor, cts.Token);
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

    private static async Task CheckOnceAsync(RssFetcher fetcher, PostsRepository repo, AnnouncementsRepository announcementsRepo, AnnouncementAutomationProcessor automationProcessor,
        CancellationToken ct)
    {
        try
        {
            var feedPosts = fetcher.FetchPosts();
            var feedIds = feedPosts
                .Where(p => p.Id != 0)
                .Select(p => p.Id)
                .ToList();

            foreach (var post in feedPosts.Where(p => p.Id != 0 && !repo.Exists(p.Id)))
            {
                var hasAnnouncement = announcementsRepo.Exists(post.Id);
                if (!hasAnnouncement && !string.IsNullOrWhiteSpace(post.Link))
                {
                    hasAnnouncement = announcementsRepo.GetByLink(post.Link) is not null;
                }

                repo.Insert(post);
                if (hasAnnouncement)
                {
                    continue;
                }

                Console.WriteLine($"New post: {post.Id} — {post.Title}");
                try
                {
                    await automationProcessor.ProcessAsync(post, DateTime.UtcNow, ct);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Announcement processing failed: {e.Message}");
                }

                await Task.Delay(250, ct);
            }

            repo.DeleteWithoutAnnouncementsNotInFeed(feedIds);
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

        if (!TryLoadAutomationOptions(out var automationOptions))
        {
            settings = default!;
            return false;
        }

        settings = new AppSettings(dbPath, token, chatId, channelId, scheduleOptions, automationOptions);
        return true;
    }

    private static bool TryLoadAutomationOptions(out AnnouncementAutomationOptions options)
    {
        var modeValue = Environment.GetEnvironmentVariable("ANNOUNCEMENT_AUTO_PARSE_MODE")?.Trim().ToLowerInvariant() ?? "off";
        var mode = modeValue switch
        {
            "off" => AnnouncementAutomationMode.Off,
            "shadow" => AnnouncementAutomationMode.Shadow,
            "active" => AnnouncementAutomationMode.Active,
            _ => (AnnouncementAutomationMode?)null
        };
        if (mode is null)
        {
            Console.WriteLine("Invalid ANNOUNCEMENT_AUTO_PARSE_MODE; expected off, shadow or active.");
            options = AnnouncementAutomationOptions.Disabled;
            return false;
        }

        if (mode == AnnouncementAutomationMode.Off)
        {
            options = AnnouncementAutomationOptions.Disabled;
            Console.WriteLine("Announcement auto parsing is off.");
            return true;
        }

        var endpointText = Environment.GetEnvironmentVariable("QWEN_API_BASE_URL") ??
                           "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/";
        var apiKey = Environment.GetEnvironmentVariable("QWEN_API_KEY");
        var model = Environment.GetEnvironmentVariable("QWEN_MODEL") ?? AnnouncementAutomationOptions.DefaultModel;
        var timeoutText = Environment.GetEnvironmentVariable("QWEN_TIMEOUT_SECONDS") ?? "30";
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !(endpoint.Host.Equals("aliyuncs.com", StringComparison.OrdinalIgnoreCase) ||
              endpoint.Host.EndsWith(".aliyuncs.com", StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            !string.Equals(model, AnnouncementAutomationOptions.DefaultModel, StringComparison.Ordinal) ||
            !int.TryParse(timeoutText, out var timeoutSeconds) ||
            timeoutSeconds is < 1 or > 30)
        {
            Console.WriteLine("Qwen configuration is invalid. Set QWEN_API_KEY, the pinned model and a HTTPS aliyuncs.com endpoint; timeout must be 1..30 seconds.");
            options = AnnouncementAutomationOptions.Disabled;
            return false;
        }

        if (!endpoint.AbsoluteUri.EndsWith('/'))
        {
            endpoint = new Uri(endpoint.AbsoluteUri + "/");
        }

        options = new AnnouncementAutomationOptions(
            mode.Value,
            endpoint,
            apiKey,
            model,
            TimeSpan.FromSeconds(timeoutSeconds));
        Console.WriteLine($"Announcement auto parsing mode: {modeValue}; model: {model}.");
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

    private static async Task<bool> EnsureChannelAccessAsync(
        ITelegramBotClient botClient,
        string channelId,
        long botUserId,
        CancellationToken ct)
    {
        try
        {
            var chatId = BuildChatId(channelId);
            _ = await botClient.GetChat(chatId, cancellationToken: ct);

            var member = await botClient.GetChatMember(chatId, botUserId, cancellationToken: ct);
            if (member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
            {
                return true;
            }

            Console.WriteLine($"Bot lacks administrator rights in channel {channelId}. Current status: {member.Status}.");
            return false;
        }
        catch (ApiRequestException ex)
        {
            Console.WriteLine($"Channel access check failed for {channelId}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error while checking channel access: {ex.Message}");
            return false;
        }
    }

    private static ChatId BuildChatId(string value)
    {
        return long.TryParse(value, out var numeric)
            ? new ChatId(numeric)
            : new ChatId(value);
    }

}
