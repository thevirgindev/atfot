using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.utilities;

[Group("vehicle", "vehicle history and license plate lookup")]
public class VehicleCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public VehicleCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("plate", "look up license plate (US/Europe)")]
    public async Task PlateLookup(
        [Summary("plate", "license plate number")] string plate,
        [Summary("country", "country code (us, uk, de, etc.)")] string country = "us",
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
            TargetLookup = $"{plate} ({country})",
            ModuleSource = "vehicle"
        };

        string encoded = Uri.EscapeDataString(plate);
        var links = new List<string>
        {
            $"[FaxVIN](https://www.faxvin.com/plate-check/{encoded})",
            $"[EpicVIN](https://epicvin.com/check-license-plate/{encoded})",
            $"[VehicleHistory](https://www.vehiclehistory.com/plate-search/{encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Plate:** `{plate}`\n**Country:** {country}\n\n**Lookup Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("vehicle lookup", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "vehicle.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "vehicle.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



