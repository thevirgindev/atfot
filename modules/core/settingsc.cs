using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;

namespace atfot.modules.core;

[Group("settings", "user preferences")]
public class SettingsCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly SettingsService _settings;
    private readonly EmbedBuilderService _emb;
    private readonly CooldownService _cd;
    private readonly KeyRedemptionService _keySvc;

    public SettingsCmd(SettingsService settings, EmbedBuilderService emb, CooldownService cd, KeyRedemptionService keySvc)
    {
        _settings = settings;
        _emb = emb;
        _cd = cd;
        _keySvc = keySvc;
    }

    private async Task<bool> isAuthed() => await _keySvc.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("show", "view your current settings")]
    public async Task Show()
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait {rem.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        var s = await _settings.GetUserSettingsAsync(Context.User.Id.ToString());
        var assp = string.IsNullOrEmpty(s.AiSummarySystemPrompt) ? "(default)" : s.AiSummarySystemPrompt.Length > 30 ? s.AiSummarySystemPrompt[..30] + "..." : s.AiSummarySystemPrompt;
        var acsp = string.IsNullOrEmpty(s.AiChatSystemPrompt) ? "(default)" : s.AiChatSystemPrompt.Length > 30 ? s.AiChatSystemPrompt[..30] + "..." : s.AiChatSystemPrompt;
        var emb = _emb.CreateMonochromeEmbed("user settings",
            $"```\n" +
            $"╔══════════════════════════════════════════╗\n" +
            $"║ Theme             : {s.Theme,-26} ║\n" +
            $"║ Notifications     : {s.Notifications,-26} ║\n" +
            $"║ AI Summary        : {(s.AiSummaryEnabled ? "on" : "off"),-26} ║\n" +
            $"║ AI Chat           : {(s.AiChatEnabled ? "on" : "off"),-26} ║\n" +
            $"║ AI Summary Prompt : {assp,-26} ║\n" +
            $"║ AI Chat Prompt    : {acsp,-26} ║\n" +
            $"║ Updated           : {s.UpdatedAt,-26} ║\n" +
            $"╚══════════════════════════════════════════╝\n```", s.Theme);
        await RespondAsync(embed: emb);
    }

    [SlashCommand("set", "update theme | notifications | ai_summary | ai_chat")]
    public async Task Set(
        [Summary("key")] string key,
        [Summary("value")] string value)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        var validKeys = new[] { "theme", "notifications", "ai_summary", "ai_chat" };
        if (!validKeys.Contains(key.ToLower()))
        {
            await RespondAsync($"[ERR] Invalid key. Valid: {string.Join(", ", validKeys)}. Use `/setassp` or `/setacsp` for system prompts.", ephemeral: true);
            return;
        }

        key = key.ToLower();
        value = value.ToLower();

        // validate theme
        if (key == "theme")
        {
            var allowed = new[] { "dark", "gray", "white", "random" };
            if (!allowed.Contains(value))
            {
                await RespondAsync($"[ERR] `theme` must be one of: {string.Join(", ", allowed)}", ephemeral: true);
                return;
            }
        }
        // validate notifications
        else if (key == "notifications")
        {
            var allowed = new[] { "silent", "public" };
            if (!allowed.Contains(value))
            {
                await RespondAsync($"[ERR] `notifications` must be one of: {string.Join(", ", allowed)}", ephemeral: true);
                return;
            }
        }
        // validate ai_summary
        else if (key == "ai_summary")
        {
            var allowed = new[] { "on", "off" };
            if (!allowed.Contains(value))
            {
                await RespondAsync($"[ERR] `ai_summary` must be `on` or `off`", ephemeral: true);
                return;
            }
        }
        // validate ai_chat
        else if (key == "ai_chat")
        {
            var allowed = new[] { "on", "off", "true", "false" };
            if (!allowed.Contains(value))
            {
                await RespondAsync($"[ERR] `ai_chat` must be `on`, `off`, `true`, or `false`", ephemeral: true);
                return;
            }
        }

        try
        {
            var success = await _settings.UpdateSettingAsync(Context.User.Id.ToString(), key, value);
            if (success)
                await RespondAsync($"[DONE] **{key}** updated to `{value}`", ephemeral: true);
            else
                await RespondAsync($"[ERR] failed to update {key}.", ephemeral: true);
        }
        catch (ArgumentException ex)
        {
            await RespondAsync($"[ERR] {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("setassp", "set AI summary system prompt from a .txt or .md file")]
    public async Task SetAssp([Summary("file", "attach a .txt or .md file with your prompt")] IAttachment file)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        if (!file.Filename.EndsWith(".txt") && !file.Filename.EndsWith(".md"))
        {
            await RespondAsync("[ERR] File must be a .txt or .md file.", ephemeral: true);
            return;
        }

        if (file.Size > 100_000)
        {
            await RespondAsync("[ERR] File too large. Max 100KB.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            using var http = new System.Net.Http.HttpClient();
            var content = await http.GetStringAsync(file.Url);
            if (string.IsNullOrWhiteSpace(content))
            {
                await FollowupAsync("[ERR] File is empty.", ephemeral: true);
                return;
            }
            var success = await _settings.UpdateSettingAsync(Context.User.Id.ToString(), "assp", content);
            if (success)
                await FollowupAsync($"[DONE] AI Summary system prompt updated ({content.Length} chars from `{file.Filename}`).", ephemeral: true);
            else
                await FollowupAsync("[ERR] Failed to save prompt.", ephemeral: true);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"[ERR] Failed to read file: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("setacsp", "set AI chat system prompt from a .txt or .md file")]
    public async Task SetAcsp([Summary("file", "attach a .txt or .md file with your prompt")] IAttachment file)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        if (!file.Filename.EndsWith(".txt") && !file.Filename.EndsWith(".md"))
        {
            await RespondAsync("[ERR] File must be a .txt or .md file.", ephemeral: true);
            return;
        }

        if (file.Size > 100_000)
        {
            await RespondAsync("[ERR] File too large. Max 100KB.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            using var http = new System.Net.Http.HttpClient();
            var content = await http.GetStringAsync(file.Url);
            if (string.IsNullOrWhiteSpace(content))
            {
                await FollowupAsync("[ERR] File is empty.", ephemeral: true);
                return;
            }
            var success = await _settings.UpdateSettingAsync(Context.User.Id.ToString(), "acsp", content);
            if (success)
                await FollowupAsync($"[DONE] AI Chat system prompt updated ({content.Length} chars from `{file.Filename}`).", ephemeral: true);
            else
                await FollowupAsync("[ERR] Failed to save prompt.", ephemeral: true);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"[ERR] Failed to read file: {ex.Message}", ephemeral: true);
        }
    }
}