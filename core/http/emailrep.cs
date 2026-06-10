using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class EmailrepClient : ApiClientBase
{
    public EmailrepClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetEmailReputation(string email)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;
        var url = $"https://emailrep.io/{email}?key={_apiKey}";
        return await GetJsonAsync(url);
    }
}




