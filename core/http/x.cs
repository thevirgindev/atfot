using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class XClient : ApiClientBase
{
    public XClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var headers = new Dictionary<string, string>
        {
            { "Authorization", $"Bearer {_apiKey}" }
        };

        var url = $"https://api.twitter.com/2/users/by/username/{username}?user.fields=created_at,description,public_metrics,verified";
        return await GetJsonAsync(url, headers);
    }
}



