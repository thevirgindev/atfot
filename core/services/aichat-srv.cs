using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using atfot.core.storage;

namespace atfot.core.services;

public class AiChatService
{
    private readonly IHttpClientFactory _http;
    private readonly ApiKeyService _apiKeys;
    private readonly DatabaseService _db;
    private const int maxMsgs = 20;

    public AiChatService(IHttpClientFactory http, ApiKeyService apiKeys, DatabaseService db)
    {
        _http = http;
        _apiKeys = apiKeys;
        _db = db;
    }

    public async Task<string?> chatAsync(string userId, string userMsg, string? systemPrompt = null)
    {
        var memoryText = await _db.GetUserMemoryAsync(userId);
        var dbHistory = await _db.GetChatHistoryAsync(userId, maxMsgs);
        var msgs = dbHistory.Select(h => (h.role, h.content)).ToList();
        await _db.SaveChatMessageAsync(userId, "user", userMsg);
        msgs.Add(("user", userMsg));

        var sys = systemPrompt ?? "You are ATFOT's Advanced AI OSINT Assistant. You help users with technical inquiries, digital footprinting, OSINT methodology, and general queries. Be precise, technical, objective, and structured in your explanations.";
        sys += "\n\nIf you learn new permanent facts about the user (name, preferences, targets), output them inside <save_memory>bullet points</save_memory> at the end. Do not mention this tag to the user.";
        if (!string.IsNullOrEmpty(memoryText))
            sys += $"\n\nKnown facts about this user:\n[MEMORY]\n{memoryText}\n[END MEMORY]";

        var allMsgs = new List<object> { new { role = "system", content = sys } };
        foreach (var m in msgs)
            allMsgs.Add(new { role = m.role, content = m.content });

        var payload = new { model = "openai", messages = allMsgs, max_tokens = 1000, temperature = 0.7 };
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);

        var apiKey = await _apiKeys.GetApiKeyAsync(userId, "pollinations");
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");

        try
        {
            var resp = await client.PostAsync("https://gen.pollinations.ai/v1/chat/completions", content);
            var json = await resp.Content.ReadAsStringAsync();
            
            if (!resp.IsSuccessStatusCode)
            {
                Serilog.Log.Warning("Pollinations API returned {StatusCode}: {Body}", resp.StatusCode, json);
                return $"[ERR] AI service returned {resp.StatusCode}. Try again in a moment.";
            }
            
            var obj = JObject.Parse(json);
            var reply = obj["choices"]?[0]?["message"]?["content"]?.ToString();
            
            if (string.IsNullOrEmpty(reply))
            {
                Serilog.Log.Warning("Pollinations API returned empty reply. Raw: {Json}", json);
                return "[ERR] AI returned an empty response. Try again.";
            }

            // extract and persist memory tag, then strip it from reply
            var memoryMatch = Regex.Match(reply, @"<save_memory>(.*?)</save_memory>", RegexOptions.Singleline);
            if (memoryMatch.Success)
            {
                var newMemory = memoryMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(newMemory))
                    await _db.SaveUserMemoryAsync(userId, newMemory);
                reply = Regex.Replace(reply, @"<save_memory>.*?</save_memory>", "", RegexOptions.Singleline).Trim();
            }
            await _db.SaveChatMessageAsync(userId, "assistant", reply);
            return reply;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AI chat request failed for user {UserId}", userId);
            return "[ERR] AI chat failed. Check logs or try again later.";
        }
    }

    public async Task clearHistory(string userId)
    {
        await _db.ClearChatHistoryAsync(userId);
        await _db.SaveUserMemoryAsync(userId, "");
    }
}