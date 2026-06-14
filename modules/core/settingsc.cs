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
        [Summary("key", "theme, notifications, ai_summary, ai_chat, assp, acsp")] string key,
        [Summary("value", "theme | notifications | ai_summary | ai_chat (see guide)")] string value)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        var validKeys = new[] { "theme", "notifications", "ai_summary", "ai_chat", "assp", "acsp" };
        if (key.ToLower() == "assp" || key.ToLower() == "acsp")
        {
            // special handling for prompts — allow spaces
        }
        else if (!validKeys.Contains(key.ToLower()))
        {
            await RespondAsync($"[ERR] invalid key. valid: {string.Join(", ", validKeys)}", ephemeral: true);
            return;
        }

        // validate theme
        if (key.ToLower() == "theme" && !new[] { "dark", "gray", "white" }.Contains(value.ToLower()))
        {
            await RespondAsync("[ERR] theme must be dark, gray, or white.", ephemeral: true);
            return;
        }
        // validate notifications
        if (key.ToLower() == "notifications" && !new[] { "silent", "public" }.Contains(value.ToLower()))
        {
            await RespondAsync("[ERR] notifications must be silent or public.", ephemeral: true);
            return;
        }
        // validate ai_summary
        if (key.ToLower() == "ai_summary" && !new[] { "on", "off" }.Contains(value.ToLower()))
        {
            await RespondAsync("[ERR] ai_summary must be on or off.", ephemeral: true);
            return;
        }
        // validate ai_chat
        if (key.ToLower() == "ai_chat" && !new[] { "on", "off", "true", "false" }.Contains(value.ToLower()))
        {
            await RespondAsync("[ERR] ai_chat must be on or off.", ephemeral: true);
            return;
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
}
