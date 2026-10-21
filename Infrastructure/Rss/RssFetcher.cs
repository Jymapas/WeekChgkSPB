using System.ServiceModel.Syndication;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace WeekChgkSPB;

public class RssFetcher(string url)
{
    private const string LiveJournalNamespace = "http://www.livejournal.org/rss/lj/1.0/";

    public List<Post> FetchPosts()
    {
        using var reader = XmlReader.Create(url, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        return Parse(reader);
    }

    internal static List<Post> Parse(XmlReader reader)
    {
        var feed = SyndicationFeed.Load(reader);
        var posts = feed.Items.Select(item =>
        {
            var link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty;
            var (authorId, username) = ReadAuthor(item);
            return new Post
            {
                Id = ExtractId(link), Title = item.Title?.Text ?? string.Empty,
                Link = link, Description = item.Summary?.Text ?? string.Empty,
                LjAuthorId = authorId, LjAuthorUsername = username
            };
        }).ToList();
        RemoveConflictingAuthors(posts);
        return posts;
    }

    private static void RemoveConflictingAuthors(List<Post> posts)
    {
        var sourceGroups = posts.Select(post =>
                (Post: post, Link: LiveJournalIdentity.TryNormalizeLink(post.Link, out var link, out _) ? link : null))
            .Where(source => source.Link is not null)
            .GroupBy(source => source.Link);
        foreach (var group in sourceGroups)
        {
            var authors = group.Select(source => (source.Post.LjAuthorId, source.Post.LjAuthorUsername));
            if (authors.Distinct().Skip(1).Any())
            {
                foreach (var source in group)
                {
                    source.Post.LjAuthorId = null;
                    source.Post.LjAuthorUsername = null;
                }
            }
        }
    }

    private static (long? AuthorId, string? Username) ReadAuthor(SyndicationItem item)
    {
        var ids = ReadValues(item, "posterid");
        var names = ReadValues(item, "poster");
        if (ids.Length != 1 || names.Length != 1 ||
            !long.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return (null, null);

        var username = names[0].Trim().ToLowerInvariant();
        if (username.Length is < 1 or > 64 ||
            username.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
            return (null, null);

        var rssAuthors = item.Authors.SelectMany(author => new[] { author.Name, author.Email })
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return rssAuthors.Any(value => !string.Equals(value!.Trim(), username, StringComparison.OrdinalIgnoreCase))
            ? (null, null) : (id, username);
    }

    private static string[] ReadValues(SyndicationItem item, string name)
    {
        return item.ElementExtensions.Where(extension =>
                extension.OuterNamespace == LiveJournalNamespace && extension.OuterName == name)
            .Select(extension =>
            {
                using var reader = extension.GetReader();
                try
                {
                    reader.MoveToContent();
                    return reader.ReadElementContentAsString().Trim();
                }
                catch (XmlException)
                {
                    return string.Empty;
                }
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static long ExtractId(string? link)
    {
        var match = Regex.Match(link ?? "", @"([0-9]+)\.html", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return match.Success && long.TryParse(match.Groups[1].Value, out var id) ? id : 0;
    }
}
