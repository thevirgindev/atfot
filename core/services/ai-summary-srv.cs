using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.services;

public class AiSummaryService
{
    private readonly IHttpClientFactory _http;
    private readonly ApiKeyService _apiKeys;

    public AiSummaryService(IHttpClientFactory http, ApiKeyService apiKeys)
    {
        _http = http;
        _apiKeys = apiKeys;
    }

    public async Task<string?> generateAsync(string discordId, string rawJson, string contextInfo)
    {
        if (string.IsNullOrEmpty(rawJson)) return null;
        var truncated = rawJson.Length > 3000 ? rawJson[..3000] + "\n... (truncated)" : rawJson;

        var sysPrompt = @"you are an osint analyst for atfot. from the data below, extract:
- threat indicators (ips, domains, hashes, emails)
- subject profile with low/medium/high confidence
- cross-reference reasoning
- actionable follow-up commands (like /osint, /threat, etc.)
keep under 800 chars. plain text. no emojis.";

        var payload = new
        {
            model = "openai",
            messages = new[]
            {
                new { role = "system", content = sysPrompt },
                new { role = "user", content = $"{contextInfo}\n\nData:\n{truncated}" }
            },
            max_tokens = 300,
            temperature = 0.5
        };

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        var apiKey = await _apiKeys.GetApiKeyAsync(discordId, "pollinations");
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");
        try
        {
            var resp = await client.PostAsync("https://gen.pollinations.ai/openai/chat/completions", content);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            return obj["choices"]?[0]?["message"]?["content"]?.ToString();
        }
        catch { return null; }
    }
}