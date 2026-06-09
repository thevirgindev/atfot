using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

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