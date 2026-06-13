using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace atfot.core.services;

public class AiChatService
{
    private readonly IHttpClientFactory _http;
    private readonly Dictionary<string, List<(string role, string content)>> _history = new();
    private const int maxMsgs = 20;

    public AiChatService(IHttpClientFactory http)
    {
        _http = http;
    }

    public async Task<string?> chatAsync(string userId, string userMsg, string? systemPrompt = null)
    {
        if (!_history.ContainsKey(userId))
            _history[userId] = new();
        var msgs = _history[userId];
        msgs.Add(("user", userMsg));
        if (msgs.Count > maxMsgs)
            msgs = msgs.Skip(msgs.Count - maxMsgs).ToList();
        _history[userId] = msgs;

        var sys = systemPrompt ?? "you are atfot's ai assistant. help with osint analysis, technical questions, and general knowledge. be concise and direct.";

        var allMsgs = new List<object> { new { role = "system", content = sys } };
        foreach (var m in msgs)
            allMsgs.Add(new { role = m.role, content = m.content });

        var payload = new { model = "openai", messages = allMsgs, max_tokens = 1000, temperature = 0.7 };
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");

        try
        {
            var resp = await client.PostAsync("https://text.pollinations.ai/openai/chat/completions", content);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            var reply = obj["choices"]?[0]?["message"]?["content"]?.ToString();
            if (reply != null)
            {
                msgs.Add(("assistant", reply));
                if (msgs.Count > maxMsgs)
                    _history[userId] = msgs.Skip(msgs.Count - maxMsgs).ToList();
            }
            return reply;
        }
        catch
        {
            return null;
        }
    }

    public void clearHistory(string userId)
    {
        _history.Remove(userId);
    }
}