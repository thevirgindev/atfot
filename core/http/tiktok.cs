using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

public class TiktokClient : ApiClientBase
{
    public TiktokClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "tiktok-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };

        var url = $"https://tiktok-api.p.rapidapi.com/user/detail?username={username}";
        return await GetJsonAsync(url, headers);
    }
}





