using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.http;

public class OpenaiClient : ApiClientBase
{
    public OpenaiClient(IHttpClientFactory httpFactory, string apiKey) : base(httpFactory, apiKey) { }

    public async Task<string?> GenerateSummary(string text)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return null;

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = "You are an OSINT assistant. Summarize the following intelligence data concisely." },
                new { role = "user", content = text }
            },
            max_tokens = 300,
            temperature = 0.5
        };

        var json = JObject.FromObject(payload).ToString();
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
        if (!response.IsSuccessStatusCode)
            return null;
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JObject.Parse(responseJson);
        return result["choices"]?[0]?["message"]?["content"]?.ToString();
    }
}




