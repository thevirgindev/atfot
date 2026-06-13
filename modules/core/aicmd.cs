using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;

namespace atfot.modules.core;

public class AiCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keySvc;
    private readonly CooldownService _cd;
    private readonly AiChatService _ai;
    private readonly EmbedBuilderService _emb;
    private readonly SettingsService _settings;

    public AiCmd(KeyRedemptionService keySvc, CooldownService cd, AiChatService ai,
        EmbedBuilderService emb, SettingsService settings)
    {
        _keySvc = keySvc;
        _cd = cd;
        _ai = ai;
        _emb = emb;
        _settings = settings;
    }

    private async Task<bool> isAuthed() => await _keySvc.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("chat", "chat with the AI assistant (free, unlimited)")]
    public async Task Chat([Summary("message", "your message to the AI")] string message)
    {
        if (!await isAuthed())
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait {rem.TotalSeconds:f0}s.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var userSettings = await _settings.GetUserSettingsAsync(Context.User.Id.ToString());
        var sysPrompt = string.IsNullOrEmpty(userSettings.SystemPrompt) ? "you are atfot's ai assistant. help with osint analysis, technical questions, and general knowledge. be concise and direct." : userSettings.SystemPrompt;

        var reply = await _ai.chatAsync(Context.User.Id.ToString(), message, sysPrompt);

        if (string.IsNullOrEmpty(reply))
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription("[ERR] AI service unavailable, try again later.")
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                    .Build();
            });
            return;
        }

        await ModifyOriginalResponseAsync(m =>
        {
            m.Embed = new EmbedBuilder()
                .WithDescription(reply.Length > 4000 ? reply[..4000] + "...(truncated)" : reply)
                .WithColor(new Color(0x55, 0x55, 0x55))
                .WithCurrentTimestamp()
                .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                .Build();
        });
    }

    [SlashCommand("chat-reset", "clear your AI conversation history")]
    public async Task ChatReset()
    {
        if (!await isAuthed())
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        _ai.clearHistory(Context.User.Id.ToString());
        await RespondAsync("[DONE] conversation history cleared.", ephemeral: true);
    }
}
