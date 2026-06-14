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

        var sysPrompt = @"You are ATFOT's elite OSINT & Threat Intelligence Analyst. Your task is to analyze raw JSON/text data and extract high-value insights.
Provide a clean, professional summary with:
1. **Threat Indicators**: List IPs, domains, hashes, or emails found.
2. **Subject Profile**: Provide a brief summary of the target with confidence levels (Low/Medium/High).
3. **Reasoning & Intel**: Cross-reference details and provide reasoning.
4. **Actionable Recommendations**: Suggest next commands to run (e.g. /osint, /threat, /social).
Keep your response concise, factual, under 800 characters, and formatted in clean markdown without emojis.";

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
            var resp = await client.PostAsync("https://gen.pollinations.ai/v1/chat/completions", content);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            return obj["choices"]?[0]?["message"]?["content"]?.ToString();
        }
        catch { return null; }
    }
}