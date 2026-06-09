using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

public class HunterClient : ApiClientBase
{
    public HunterClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> EmailSearch(string domain)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.hunter.io/v2/domain-search?domain={domain}&api_key={_apiKey}";
        return await GetJsonAsync(url);
    }

    public async Task<JObject?> EmailVerifier(string email)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://api.hunter.io/v2/email-verifier?email={email}&api_key={_apiKey}";
        return await GetJsonAsync(url);
    }
}




