using System;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using HtmlAgilityPack;

namespace atfot.core.services;

public class ScraperService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string[] _userAgents = new[]
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    };
    private readonly Random _random = new();

    public ScraperService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient();
        var userAgent = _userAgents[_random.Next(_userAgents.Length)];
        client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        return client;
    }

    public async Task<string> FetchHtmlAsync(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    // Fixed: return IDocument directly (AngleSharp's main document interface)
    public async Task<IDocument?> FetchAngleSharpDocumentAsync(string url)
    {
        var html = await FetchHtmlAsync(url);
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(req => req.Content(html));
    }

    public async Task<HtmlDocument> FetchHtmlAgilityDocumentAsync(string url)
    {
        var html = await FetchHtmlAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }
}
