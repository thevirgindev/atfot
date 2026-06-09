using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.network;

[Group("threatactor", "threat actor profiling and APT groups")]
public class ThreatActorCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public ThreatActorCommands(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("lookup", "search for threat actor information")]
    public async Task ThreatActorLookup(
        [Summary("name", "APT group or threat actor name (e.g., APT28, Lazarus)")] string name,
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
            ModuleSource = "threat_actor"
        };

        string encoded = Uri.EscapeDataString(name);
        var links = new List<string>
        {
            $"[Malpedia](https://malpedia.caad.fkie.fraunhofer.de/actors/search?q={encoded})",
            $"[APT Groups Sheet](https://docs.google.com/spreadsheets/d/1H9_xaxQHpWaa4O_Son4Gx0YOIzlcBWMsdvePFX68EKU/edit#gid=0)",
            $"[Mitre ATT&CK Groups](https://attack.mitre.org/groups/?name={encoded})",
            $"[BreachHQ Threat Actors](https://breach-hq.com/threat-actors?search={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Threat Actor:** {name}\n\n**Profiles & Databases:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("threat actor intelligence", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "threatactor.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "threatactor.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("map", "live cyber threat map")]
    public async Task ThreatMap(
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
            TargetLookup = "live_threat_maps",
            ModuleSource = "threat_map"
        };

        var links = new List<string>
        {
            "[Kaspersky Cybermap](https://cybermap.kaspersky.com/)",
            "[Check Point ThreatMap](https://threatmap.checkpoint.com/)",
            "[Fortinet Threat Map](https://threatmap.fortinet.com/)",
            "[SonicWall Live Map](https://live.sonicwall.com/live)"
        };
        dto.DeepLinks = links;

        var description = $"**Live Cyber Threat Maps:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("threat maps", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "threatmap.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "threatmap.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



