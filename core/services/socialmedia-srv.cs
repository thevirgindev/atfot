using System;
using System.Threading.Tasks;
using pewbot.core.http;
using Newtonsoft.Json.Linq;

namespace pewbot.core.services;

public class SocialMediaService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ApiKeyService _apiKeyService;

    public SocialMediaService(IHttpClientFactory httpFactory, ApiKeyService apiKeyService)
    {
        _httpFactory = httpFactory;
        _apiKeyService = apiKeyService;
    }

    public async Task<JObject?> GetRedditUserAsync(string username)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "PewBot/1.0");
        try
        {
            var url = $"https://www.reddit.com/user/{username}/about.json";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
        catch { return null; }
    }

    public async Task<JObject?> GetGitHubUserAsync(string username)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "PewBot/1.0");
        try
        {
            var url = $"https://api.github.com/users/{username}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
        catch { return null; }
    }

    public async Task<JObject?> GetDiscordUserAsync(string userId)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "PewBot/1.0");
        string[] apis = {
            $"https://discordlookup.com/api/user/{userId}",
            $"https://discord.com/api/v9/users/{userId}",
            $"https://discord.js.org/api/users/{userId}"
        };
        foreach (var url in apis)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(json);
                }
            }
            catch { continue; }
        }
        return null;
    }

    public async Task<JObject?> GetTwitterUserAsync(string username, string discordUserId)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(discordUserId, "twitter");
        if (string.IsNullOrEmpty(apiKey)) return null;
        var client = new XClient(_httpFactory, apiKey);
        return await client.GetUserProfile(username);
    }

    public async Task<JObject?> GetTikTokUserAsync(string username, string discordUserId)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(discordUserId, "tiktok");
        if (string.IsNullOrEmpty(apiKey)) return null;
        var client = new TiktokClient(_httpFactory, apiKey);
        return await client.GetUserProfile(username);
    }

    public async Task<JObject?> GetLinkedInUserAsync(string username, string discordUserId)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(discordUserId, "linkedin");
        if (string.IsNullOrEmpty(apiKey)) return null;
        var client = new LinkedinClient(_httpFactory, apiKey);
        return await client.GetUserProfile(username);
    }

    public async Task<JObject?> GetPinterestUserAsync(string username, string discordUserId)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(discordUserId, "pinterest");
        if (string.IsNullOrEmpty(apiKey)) return null;
        var client = new PinterestClient(_httpFactory, apiKey);
        return await client.GetUserProfile(username);
    }

    public async Task<JObject?> GetTelegramUserAsync(string username, string discordUserId)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(discordUserId, "telegram");
        if (string.IsNullOrEmpty(apiKey)) return null;
        var client = new TelegramClient(_httpFactory, apiKey);
        return await client.GetUserInfo(username);
    }

    // ========== INSTAGRAM SCRAPERS ==========

    public async Task<JObject?> GetSocialApisInstagramUserAsync(string username, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username))
            return null;

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-token", apiKey);

        try
        {
            var url = $"https://api.socialapis.io/instagram/profile/details?username={Uri.EscapeDataString(username)}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<JObject?> GetSerpApiInstagramUserAsync(string username, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username))
            return null;

        var client = _httpFactory.CreateClient();

        try
        {
            var url = $"https://serpapi.com/search.json?engine=instagram_profile&profile_id={Uri.EscapeDataString(username)}&api_key={apiKey}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            var profile = data["profile_results"];
            return profile as JObject;
        }
        catch
        {
            return null;
        }
    }

    // Apify Reddit Author Scraper (profile only, no posts/comments)
    public async Task<JObject?> GetRedditAuthorAsync(string username, string apifyToken)
    {
        if (string.IsNullOrEmpty(apifyToken) || string.IsNullOrEmpty(username))
            return null;

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apifyToken}");

        var input = new JObject
        {
            ["username"] = username,
            ["max_posts"] = 0,
            ["max_comments"] = 0
        };
        var content = new StringContent(input.ToString(), System.Text.Encoding.UTF8, "application/json");

        try
        {
            var url = $"https://api.apify.com/v2/acts/agentx~reddit-author-scraper/run-sync-get-dataset-items?token={apifyToken}";
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var items = JArray.Parse(json);
            return items.Count > 0 ? items[0] as JObject : null;
        }
        catch
        {
            return null;
        }
    }

    // ========== STUBS FOR EXTERNAL DISCORD TOOLS ==========
    // (to be replaced with real implementations when API keys and endpoints are available)

    public async Task<JObject?> GetOsintCatAsync(string query, string apiKey) => null;
    public async Task<JObject?> GetLeakInsightAsync(string email, string apiKey) => null;
    public async Task<JObject?> GetIntelFetchAsync(string query, string apiKey) => null;
    public async Task<JObject?> GetIndiciaAsync(string query, string apiKey) => null;
    public async Task<JObject?> GetCrowSintAsync(string query, string apiKey) => null;
    public async Task<JObject?> GetOathNetAsync(string discordId, string apiKey) => null;
}