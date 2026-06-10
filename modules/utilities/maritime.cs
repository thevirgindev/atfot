using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.utilities;

[Group("maritime", "vessel tracking and marine intelligence")]
public class MaritimeCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public MaritimeCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("vessel", "track vessel by name or MMSI")]
    public async Task VesselLookup(
        [Summary("name", "vessel name or MMSI number")] string name,
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
            TargetLookup = name,
            ModuleSource = "maritime"
        };

        string encoded = Uri.EscapeDataString(name);
        var links = new List<string>
        {
            $"[VesselFinder](https://www.vesselfinder.com/?q={encoded})",
            $"[MarineTraffic](https://www.marinetraffic.com/en/ais/home/center:0:0/zoom:4/name:{encoded})",
            $"[FleetMon](https://www.fleetmon.com/vessels/search/{encoded}/)"
        };
        dto.DeepLinks = links;

        var description = $"**Vessel:** {name}\n\n**Tracking Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("maritime intelligence", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "maritime.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "maritime.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



