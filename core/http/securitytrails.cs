using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

public class SecuritytrailsClient : ApiClientBase
{
    public SecuritytrailsClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetSubdomains(string domain)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.securitytrails.com/v1/domain/{domain}/subdomains";
        var headers = new Dictionary<string, string> { { "APIKEY", _apiKey } };
        return await GetJsonAsync(url, headers);
    }

    public async Task<JObject?> GetDnsHistory(string domain)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.securitytrails.com/v1/history/{domain}/dns/a";
        var headers = new Dictionary<string, string> { { "APIKEY", _apiKey } };
        return await GetJsonAsync(url, headers);
    }
}




