using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.utilities;

[Group("data", "global statistics and economic data")]
public class DataCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public DataCmd(
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

    [SlashCommand("worldbank", "query World Bank open data")]
    public async Task WorldBankData(
        [Summary("indicator", "e.g., NY.GDP.MKTP.CD, SP.POP.TOTL")] string indicator,
        [Summary("country", "country code (e.g., US, CN, GB)")] string country = "all",
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
            TargetLookup = $"{indicator} for {country}",
            ModuleSource = "worldbank"
        };

        string encoded = Uri.EscapeDataString(indicator);
        string url = country == "all" 
            ? $"https://api.worldbank.org/v2/country/all/indicator/{encoded}?format=json"
            : $"https://api.worldbank.org/v2/country/{country}/indicator/{encoded}?format=json";

        var links = new List<string>
        {
            $"[World Bank Data Explorer](https://data.worldbank.org/indicator/{encoded})",
            $"[CIA World Factbook](https://www.cia.gov/the-world-factbook/)",
            $"[IMF Data](https://www.imf.org/en/Data)"
        };
        dto.DeepLinks = links;

        // Simple API call to World Bank (no key required)
        try
        {
            var client = new System.Net.Http.HttpClient();
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                dto.RawApiResponse = json;
                dto.ExtractedData["api_status"] = "success";
            }
            else
            {
                dto.ExtractedData["api_error"] = response.StatusCode.ToString();
            }
        }
        catch (Exception ex)
        {
            dto.ExtractedData["exception"] = ex.Message;
        }

        var description = $"**Indicator:** `{indicator}`\n**Country:** `{country}`\n\n**Data Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("world bank data", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "worldbank.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "worldbank.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}


