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
        var prompt = string.IsNullOrEmpty(s.ChatSystemPrompt) ? "(default)" : s.ChatSystemPrompt.Length > 40 ? s.ChatSystemPrompt[..40] + "..." : s.ChatSystemPrompt;
        var emb = _emb.CreateMonochromeEmbed("user settings",
            $"```\n" +
            $"╔══════════════════════════════════════════╗\n" +
            $"║ Theme             : {s.Theme,-26} ║\n" +
            $"║ Notifications     : {s.Notifications,-26} ║\n" +
            $"║ AI Summary        : {(s.AiSummaryEnabled ? "on" : "off"),-26} ║\n" +
            $"║ AI Chat Prompt    : {prompt,-26} ║\n" +
            $"║ Loading Style     : {s.LoadingStyle,-26} ║\n" +
            $"║ Auto Collapse     : {(s.AutoCollapse ? "on" : "off"),-26} ║\n" +
            $"║ Updated           : {s.UpdatedAt,-26} ║\n" +
            $"╚══════════════════════════════════════════╝\n```", s.Theme);
        await RespondAsync(embed: emb);
    }

    [SlashCommand("set", "update a setting")]
    public async Task Set(
        [Summary("key", "theme, notifications, ai_summary, ai_chat_system_prompt, loading_style, auto_collapse")] string key,
        [Summary("value", "new value")] string value)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait {rem.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        var validKeys = new[] { "theme", "notifications", "ai_summary", "ai_chat_system_prompt", "loading_style", "auto_collapse" };
        if (key.ToLower() == "ai_chat_system_prompt")
        {
            // special handling for prompt — allow spaces, joined from value param
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
        // validate loading_style
        if (key.ToLower() == "loading_style" && !new[] { "minimal", "verbose" }.Contains(value.ToLower()))
        {
            await RespondAsync("[ERR] loading_style must be minimal or verbose.", ephemeral: true);
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