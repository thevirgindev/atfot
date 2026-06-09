using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.utilities;

[Group("monitor", "web change detection and alerting")]
public class MonitoringCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public MonitoringCmd(
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

    [SlashCommand("alert", "set up change detection alerts (via external services)")]
    public async Task ChangeDetection(
        [Summary("url", "website URL to monitor")] string url,
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
            TargetLookup = url,
            ModuleSource = "web_monitoring"
        };

        string encoded = Uri.EscapeDataString(url);
        var links = new List<string>
        {
            $"[ChangeDetection.io](https://changedetection.io/?url={encoded})",
            $"[Visualping](https://visualping.io/?url={encoded})",
            $"[Google Alerts](https://www.google.com/alerts?q={encoded})",
            $"[Versionista](https://versionista.com/?url={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**URL:** {url}\n\n**Monitoring Services:**\n{string.Join("\n", links)}\n\n*Note: These services require manual sign-up.*";
        var embed = _embed.CreateMonochromeEmbed("change detection", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "monitor.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "monitor.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



