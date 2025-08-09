using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Notifications;

const string dbPath = "posts.db";
const string rssUrl = "https://chgk-spb.livejournal.com/data/rss";

var repo = new PostsRepository(dbPath);
var fetcher = new RssFetcher(rssUrl);

var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var chatIdVar = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
INotifier? notifier = null;

if (!string.IsNullOrWhiteSpace(token) && long.TryParse(chatIdVar, out var chatId))
{
    notifier = new TelegramNotifier(token, chatId);
    Console.WriteLine("Telegram notifier enabled");
}
else
{
    Console.WriteLine("Telegram notifier disabled: set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID");
    return;
}

while (true)
{
    try
    {
        var posts = fetcher.FetchPosts();
        foreach (var post in posts.Where(post => post.Id != 0 && !repo.Exists(post.Id)))
        {
            repo.Insert(post);
            Console.WriteLine($"New post: {post.Id} — {post.Title}");

            try
            {
                await notifier.NotifyNewPostAsync(post);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Telegram send failed: {e.Message}");
            }
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }

    Thread.Sleep(TimeSpan.FromHours(1));
}