using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

// ========== BASE CLASS ==========
public abstract class ApiClientBase
{
    protected readonly IHttpClientFactory _httpFactory;
    protected readonly string _apiKey;

    protected ApiClientBase(IHttpClientFactory httpFactory, string apiKey)
    {
        _httpFactory = httpFactory;
        _apiKey = apiKey;
    }

    protected async Task<JObject?> GetJsonAsync(string url, Dictionary<string, string>? headers = null)
    {
        var client = _httpFactory.CreateClient();
        if (headers != null)
        {
            foreach (var h in headers)
                client.DefaultRequestHeaders.Add(h.Key, h.Value);
        }
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return null;
        var content = await response.Content.ReadAsStringAsync();
        return JObject.Parse(content);
    }

    protected async Task<JObject?> PostJsonAsync(string url, HttpContent content, Dictionary<string, string>? headers = null)
    {
        var client = _httpFactory.CreateClient();
        if (headers != null)
        {
            foreach (var h in headers)
                client.DefaultRequestHeaders.Add(h.Key, h.Value);
        }
        var response = await client.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
            return null;
        var responseContent = await response.Content.ReadAsStringAsync();
        return JObject.Parse(responseContent);
    }
}

// ========== LINKEDIN (RAPIDAPI) ==========
public class LinkedinClient : ApiClientBase
{
    public LinkedinClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;
        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "linkedin-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };
        var url = $"https://linkedin-api.p.rapidapi.com/search-profile?keywords={username}";
        return await GetJsonAsync(url, headers);
    }
}

// ========== PINTEREST (RAPIDAPI) ==========
public class PinterestClient : ApiClientBase
{
    public PinterestClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;
        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "pinterest-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };
        var url = $"https://pinterest-api.p.rapidapi.com/user/{username}/";
        return await GetJsonAsync(url, headers);
    }
}

// ========== TIKTOK (RAPIDAPI) ==========
public class TiktokClient : ApiClientBase
{
    public TiktokClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;
        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "tiktok-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };
        var url = $"https://tiktok-api.p.rapidapi.com/user/detail?username={username}";
        return await GetJsonAsync(url, headers);
    }
}

// ========== X (TWITTER API V2) ==========
public class XClient : ApiClientBase
{
    public XClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;
        var headers = new Dictionary<string, string> { { "Authorization", $"Bearer {_apiKey}" } };
        var url = $"https://api.twitter.com/2/users/by/username/{username}?user.fields=created_at,description,public_metrics,verified";
        return await GetJsonAsync(url, headers);
    }
}