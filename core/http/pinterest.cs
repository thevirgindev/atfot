using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace pewbot.core.http;

public class PinterestClient : ApiClientBase
{
    public PinterestClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "pinterest-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };

        var url = $"https://pinterest-api.p.rapidapi.com/user/{username}/";
        return await GetJsonAsync(url, headers);
    }
}





