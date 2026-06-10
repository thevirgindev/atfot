using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class LinkedinClient : ApiClientBase
{
    public LinkedinClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserProfile(string username)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "linkedin-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };

        var url = $"https://linkedin-api.p.rapidapi.com/search-profile?keywords={username}";
        return await GetJsonAsync(url, headers);
    }
}





