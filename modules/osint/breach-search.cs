using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.osint;

[Group("breach", "data breach and credential leak search")]
public class BreachCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public BreachCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("email", "check if email appears in data breaches")]
    public async Task BreachEmail(
        [Summary("email", "email address")] string email,
        [Summary("export", "export format (none, txt, json)")] string export = "none")
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("🔒 Redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var dto = new ScanResultDto
        {
            TargetLookup = email,
            ModuleSource = "breach_check"
        };

        string encoded = Uri.EscapeDataString(email);
        var links = new List<string>
        {
            $"[HaveIBeenPwned](https://haveibeenpwned.com/account/{encoded})",
            $"[LeakCheck](https://leakcheck.io/?q={encoded})",
            $"[DeHashed](https://dehashed.com/search?query={encoded})",
            $"[IntelX](https://intelx.io/?s={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Email:** {email}\n\n**Breach Check Links:**\n{string.Join("\n", links)}\n*Note: Some services require API keys for automated results. Use `/admin setkey` for LeakCheck, DeHashed, etc.*";
        var embed = _embed.CreateMonochromeEmbed("breach intelligence", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "breach.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "breach.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}


