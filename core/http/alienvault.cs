using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

public class AlienvaultClient : ApiClientBase
{
    public AlienvaultClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetPulseByIndicator(string indicator)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://otx.alienvault.com/api/v1/indicators/any/{indicator}/general";
        var headers = new Dictionary<string, string> { { "X-OTX-API-KEY", _apiKey } };
        return await GetJsonAsync(url, headers);
    }
}




