using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.utilities;

[Group("privacy", "privacy tools and encryption resources")]
public class PrivacyCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public PrivacyCmd(
        KeyRedemptionService keyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("tools", "list privacy-enhancing tools and services")]
    public async Task PrivacyTools(
        [Summary("export", "export format (none, txt, json)")] string export = "none")
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("🔒 You need to redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var dto = new ScanResultDto
        {
            TargetLookup = "privacy_tools",
            ModuleSource = "privacy"
        };

        var links = new List<string>
        {
            "[ProtonMail](https://proton.me/mail)",
            "[Signal](https://signal.org/)",
            "[Tor Browser](https://www.torproject.org/)",
            "[Bitwarden](https://bitwarden.com/)",
            "[Tails OS](https://tails.boum.org/)",
            "[uBlock Origin](https://ublockorigin.com/)",
            "[Privacy Badger](https://privacybadger.org/)"
        };
        dto.DeepLinks = links;

        var description = $"**Privacy & Security Tools:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("privacy resources", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "privacy.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "privacy.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



