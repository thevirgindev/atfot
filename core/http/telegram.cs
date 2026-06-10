using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class TelegramClient : ApiClientBase
{
    public TelegramClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetUserInfo(string username)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var headers = new Dictionary<string, string>
        {
            { "x-rapidapi-host", "telegram-api.p.rapidapi.com" },
            { "x-rapidapi-key", _apiKey }
        };

        var url = $"https://telegram-api.p.rapidapi.com/search?q={username}&type=user";
        return await GetJsonAsync(url, headers);
    }
}





