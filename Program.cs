using WeekChgkSPB;

const string dbPath = "posts.db";
const string rssUrl = "https://chgk-spb.livejournal.com/data/rss";

var repo = new PostsRepository(dbPath);

var fetcher = new RssFetcher(rssUrl);

while (true)
{
    try
    {
        var posts = fetcher.FetchPosts();
        foreach (var post in posts.Where(post => post.Id != 0 && !repo.Exists(post.Id)))
        {
            repo.Insert(post);
            Console.WriteLine($"New post: {post.Id} — {post.Title}");
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
    
    Thread.Sleep(TimeSpan.FromHours(1));
}