using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

namespace WeekChgkSPB;

public class RssFetcher(string url)
{
    public List<Post> FetchPosts()
    {
        using var reader = new XmlTextReader(url);
        var feed = SyndicationFeed.Load(reader);

        return (from it in feed.Items
            let link = it.Links.FirstOrDefault()?.Uri.ToString()
            let id = ExtractId(link)
            select new Post
            {
                Id = id, Title = it.Title.Text, Link = link, Description = it.Summary.Text
            }).ToList();
    }

    private static long ExtractId(string? link)
    {
        var match = Regex.Match(link ?? "", @"(\d+)\.html");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}