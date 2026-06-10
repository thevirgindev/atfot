using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class VirustotalClient : ApiClientBase
{
    public VirustotalClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUrlReport(string url)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var encoded = Uri.EscapeDataString(url);
        var requestUrl = $"https://www.virustotal.com/api/v3/urls/{encoded}";
        var headers = new Dictionary<string, string> { { "x-apikey", _apiKey } };
        return await GetJsonAsync(requestUrl, headers);
    }

    public async Task<JObject?> GetIpReport(string ip)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://www.virustotal.com/api/v3/ip_addresses/{ip}";
        var headers = new Dictionary<string, string> { { "x-apikey", _apiKey } };
        return await GetJsonAsync(url, headers);
    }
}




