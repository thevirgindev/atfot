using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.search;

[Group("similar", "find similar websites")]
public class SimilarCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public SimilarCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("find", "find websites similar to a given URL")]
    public async Task FindSimilar(
        [Summary("url", "website URL (e.g., example.com)")] string url,
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
            TargetLookup = url,
            ModuleSource = "similar_sites"
        };

        string encoded = Uri.EscapeDataString(url);
        var links = new List<string>
        {
            $"[SimilarSites](https://www.similarsites.com/search?q={encoded})",
            $"[SitesLike](https://www.siteslike.com/site/{encoded})",
            $"[SiteSimilar](https://www.sitesimilar.com/similar-to/{encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**URL:** {url}\n\n**Similar Sites Tools:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("similar websites", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "similar.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "similar.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



