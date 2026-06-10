using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class SocialApisClient : ApiClientBase
{
    public SocialApisClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<JObject?> GetInstagramProfileDetails(string username)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;
        var headers = new Dictionary<string, string>
        {
            { "x-api-token", _apiKey }
        };
        var url = $"https://api.socialapis.io/instagram/profile/details?username={username}";
        return await GetJsonAsync(url, headers);
    }
}
