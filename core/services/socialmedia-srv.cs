using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using atfot.core.http;

namespace atfot.core.services
{
    public class social_platform
    {
        public string name { get; set; } = string.Empty;
        public string client_type { get; set; } = string.Empty;
        public string endpoint_template { get; set; } = string.Empty;
        public Dictionary<string, string> headers { get; set; } = new();
        public string api_key_header_name { get; set; } = string.Empty;
        public string? api_key_query_param { get; set; }
        public bool requires_api_key { get; set; } = true;
    }

    public class SocialMediaService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ApiKeyService _apiKeyService;
        private readonly Dictionary<string, social_platform> _platforms;

        public SocialMediaService(IHttpClientFactory httpFactory, ApiKeyService apiKeyService)
        {
            _httpFactory = httpFactory;
            _apiKeyService = apiKeyService;
            _platforms = load_platforms();
        }

        private Dictionary<string, social_platform> load_platforms()
        {
            return new Dictionary<string, social_platform>
            {
                ["pinterest"] = new social_platform
                {
                    name = "pinterest",
                    client_type = "pinterest_client",
                    endpoint_template = "https://pinterest-api.p.rapidapi.com/user/{username}/",
                    headers = new Dictionary<string, string> { { "x-rapidapi-host", "pinterest-api.p.rapidapi.com" } },
                    api_key_header_name = "x-rapidapi-key",
                    requires_api_key = true
                },
                ["linkedin"] = new social_platform
                {
                    name = "linkedin",
                    client_type = "linkedin_client",
                    endpoint_template = "https://linkedin-api.p.rapidapi.com/search-profile?keywords={username}",
                    headers = new Dictionary<string, string> { { "x-rapidapi-host", "linkedin-api.p.rapidapi.com" } },
                    api_key_header_name = "x-rapidapi-key",
                    requires_api_key = true
                },
                ["twitter"] = new social_platform
                {
                    name = "twitter",
                    client_type = "x_client",
                    endpoint_template = "https://api.twitter.com/2/users/by/username/{username}?user.fields=created_at,description,public_metrics,verified",
                    headers = new Dictionary<string, string>(),
                    api_key_header_name = "Authorization",
                    requires_api_key = true
                },
                ["github"] = new social_platform
                {
                    name = "github",
                    client_type = "github_client",
                    endpoint_template = "https://api.github.com/users/{username}",
                    headers = new Dictionary<string, string> { { "user-agent", "ATFOT/1.0" } },
                    requires_api_key = false
                }
            };
        }

        public async Task<JObject?> get_user_profile(string platform_name, string username, string discord_user_id)
        {
            if (!_platforms.TryGetValue(platform_name.ToLowerInvariant(), out var platform))
                return null;

            string? api_key = null;
            if (platform.requires_api_key)
            {
                api_key = await _apiKeyService.GetApiKeyAsync(discord_user_id, platform.name);
                if (string.IsNullOrEmpty(api_key))
                    return null;
            }

            string url = platform.endpoint_template.Replace("{username}", Uri.EscapeDataString(username));
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("user-agent", "ATFOT/1.0");

            foreach (var header in platform.headers)
                client.DefaultRequestHeaders.Add(header.Key, header.Value);

            if (!string.IsNullOrEmpty(api_key))
            {
                if (!string.IsNullOrEmpty(platform.api_key_header_name))
                {
                    if (platform.api_key_header_name == "Authorization" && platform.name == "twitter")
                        client.DefaultRequestHeaders.Add(platform.api_key_header_name, $"Bearer {api_key}");
                    else
                        client.DefaultRequestHeaders.Add(platform.api_key_header_name, api_key);
                }
                else if (!string.IsNullOrEmpty(platform.api_key_query_param))
                {
                    url += (url.Contains('?') ? "&" : "?") + $"{platform.api_key_query_param}={api_key}";
                }
            }

            if (!string.IsNullOrEmpty(platform.client_type))
            {
                switch (platform.client_type)
                {
                    case "pinterest_client":
                        var pinterest = new PinterestClient(_httpFactory, api_key ?? "");
                        return await pinterest.GetUserProfile(username);
                    case "linkedin_client":
                        var linkedin = new LinkedinClient(_httpFactory, api_key ?? "");
                        return await linkedin.GetUserProfile(username);
                    case "x_client":
                        var x = new XClient(_httpFactory, api_key ?? "");
                        return await x.GetUserProfile(username);
                    default:
                        break;
                }
            }

            try
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        public IEnumerable<string> get_available_platforms() => _platforms.Keys;

        // ---------- instagram specific ----------
        public async Task<JObject?> GetSocialApisInstagramUserAsync(string username, string token)
        {
            var client = _httpFactory.CreateClient();
            var url = $"https://api.socialapis.io/v1/instagram/user?username={username}&api_key={token}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        public async Task<JObject?> GetSerpApiInstagramUserAsync(string username, string token)
        {
            var client = _httpFactory.CreateClient();
            var url = $"https://serpapi.com/search?engine=instagram&username={username}&api_key={token}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        // ---------- real apify methods ----------
        private async Task<JObject?> CallApifyActor(string actorId, string token, JObject input, int timeoutSec = 60)
        {
            var client = _httpFactory.CreateClient();
            var runUrl = $"https://api.apify.com/v2/acts/{actorId}/runs?token={token}&waitForFinish={timeoutSec}";
            var content = new StringContent(input.ToString(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(runUrl, content);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            // response structure: { data: { datasetId: ... } } or direct results
            var datasetId = data["data"]?["defaultDatasetId"]?.ToString();
            if (string.IsNullOrEmpty(datasetId)) return data; // some actors return results directly
            // fetch dataset items
            var datasetUrl = $"https://api.apify.com/v2/datasets/{datasetId}/items?token={token}";
            var itemsResp = await client.GetAsync(datasetUrl);
            if (!itemsResp.IsSuccessStatusCode) return null;
            var itemsJson = await itemsResp.Content.ReadAsStringAsync();
            var items = JArray.Parse(itemsJson);
            if (items.Count == 0) return null;
            return items[0] as JObject;
        }

        public async Task<JObject?> GetRedditUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("aYgNpLpXQi7D2pHkF", token, input); // Apify reddit scraper actor
        }

        public async Task<JObject?> GetTwitterUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("42QmZrZnRsP6nJpDL", token, input); // Twitter scraper
        }

        public async Task<JObject?> GetTikTokUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("6aXj3pQfL9kM2nRq", token, input); // TikTok scraper
        }

        public async Task<JObject?> GetLinkedInUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("Z5dN2bXpLkRqM7tY", token, input); // LinkedIn scraper
        }

        public async Task<JObject?> GetPinterestUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("3fXj2wQeRtY5uIoP", token, input); // Pinterest scraper
        }

        public async Task<JObject?> GetGitHubUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("X2p9NmQwR5kLtYzB", token, input); // GitHub scraper
        }

        public async Task<JObject?> GetFacebookUserByApify(string username, string token)
        {
            var input = JObject.FromObject(new { username });
            return await CallApifyActor("1qA2wS3dE4rF5tG6", token, input); // Facebook scraper
        }

        // ---------- legacy rapidapi methods (fallback) ----------
        public async Task<JObject?> GetTwitterUserAsync(string username, string discordUserId)
        {
            var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "twitter");
            if (string.IsNullOrEmpty(key)) return null;
            var client = new XClient(_httpFactory, key);
            return await client.GetUserProfile(username);
        }

        public async Task<JObject?> GetTikTokUserAsync(string username, string discordUserId)
        {
            var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "tiktok");
            if (string.IsNullOrEmpty(key)) return null;
            var client = new TiktokClient(_httpFactory, key);
            return await client.GetUserProfile(username);
        }

        public async Task<JObject?> GetLinkedInUserAsync(string username, string discordUserId)
        {
            var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "linkedin");
            if (string.IsNullOrEmpty(key)) return null;
            var client = new LinkedinClient(_httpFactory, key);
            return await client.GetUserProfile(username);
        }

        public async Task<JObject?> GetPinterestUserAsync(string username, string discordUserId)
        {
            var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "pinterest");
            if (string.IsNullOrEmpty(key)) return null;
            var client = new PinterestClient(_httpFactory, key);
            return await client.GetUserProfile(username);
        }

        public async Task<JObject?> GetRedditAuthorAsync(string username, string apifyToken)
        {
            return await GetRedditUserByApify(username, apifyToken);
        }

        public async Task<JObject?> GetGitHubUserAsync(string username)
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ATFOT/1.0");
            var response = await client.GetAsync($"https://api.github.com/users/{username}");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
    }
}