using System.Net.Http;
using System.Xml.Linq;

namespace SquareDeck.Services;

/// <summary>
/// Reads an RSS feed (IGN's PC articles on the News page) into news posts. Each read uses its own short-lived
/// connection, and nothing is kept here: the News page holds the posts while it's open and lets them go when it closes.
/// </summary>
public static class RssFeed
{
    public const string IgnPc = "https://feeds.feedburner.com/ign/pc-articles";
    public const string IgnGames = "https://feeds.feedburner.com/ign/games-all";

    public static async Task<List<NewsItem>> ReadAsync(string url, string feedName, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/1.0 (+https://github.com/tetraguy/SquareDeck)");
        var xml = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        XNamespace media = "http://search.yahoo.com/mrss/", content = "http://purl.org/rss/1.0/modules/content/";
        var posts = new List<NewsItem>();
        foreach (var item in doc.Descendants("item").Take(40))
        {
            string title = SteamStoreClient.CleanText((string?)item.Element("title") ?? "");
            string link = ((string?)item.Element("link") ?? "").Trim();
            if (title.Length == 0 || link.Length == 0) continue;
            string html = (string?)item.Element(content + "encoded") ?? (string?)item.Element("description") ?? "";
            DateTime date = DateTime.TryParse((string?)item.Element("pubDate"), out var d) ? d : DateTime.Now;
            // The picture: media:content / media:thumbnail, an enclosure, or the first <img> in the text.
            string image = (string?)item.Element(media + "content")?.Attribute("url")
                           ?? (string?)item.Element(media + "thumbnail")?.Attribute("url")
                           ?? (string?)item.Elements("enclosure").FirstOrDefault(e => ((string?)e.Attribute("type") ?? "").StartsWith("image"))?.Attribute("url")
                           ?? System.Text.RegularExpressions.Regex.Match(html, "<img[^>]+src=\"([^\"]+)\"").Groups[1].Value;
            string text = SteamStoreClient.CleanText(html);
            posts.Add(new NewsItem(title, link, text.Length > 220 ? text[..220] + "…" : text, date, feedName,
                SteamStoreClient.CleanArticle(html), Tags: null, AppId: 0, FeedName: feedName, Image: image ?? ""));
        }
        return posts.OrderByDescending(p => p.Date).ToList();
    }
}
