using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class ShodanClient : ApiClientBase
{
    public ShodanClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetHostInfo(string ip)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.shodan.io/shodan/host/{ip}?key={_apiKey}";
        return await GetJsonAsync(url);
    }

    public async Task<JObject?> Search(string query, int limit = 10)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.shodan.io/shodan/host/search?key={_apiKey}&query={Uri.EscapeDataString(query)}&limit={limit}";
        return await GetJsonAsync(url);
    }
}




