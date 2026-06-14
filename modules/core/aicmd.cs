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

    [SlashCommand("chat-reset", "clear your AI conversation history")]
    public async Task ChatReset()
    {
        if (!await isAuthed())
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        await _ai.clearHistory(Context.User.Id.ToString());
        await RespondAsync("[DONE] conversation history cleared.", ephemeral: true);
    }
}
