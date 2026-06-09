using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.osint;

[Group("company", "business and corporate intelligence")]
public class CompanyCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public CompanyCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("lookup", "search for company information")]
    public async Task CompanyLookup(
        [Summary("name", "company name")] string name,
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
            ModuleSource = "company_intel"
        };

        string encoded = Uri.EscapeDataString(name);
        var links = new List<string>
        {
            $"[Crunchbase](https://www.crunchbase.com/search/organization.companies?q={encoded})",
            $"[OpenCorporates](https://opencorporates.com/companies?q={encoded})",
            $"[Bloomberg](https://www.bloomberg.com/search?query={encoded})",
            $"[Glassdoor](https://www.glassdoor.com/Search/results.htm?keyword={encoded})",
            $"[EDGAR (SEC)](https://www.sec.gov/edgar/search/#/q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Company:** {name}\n\n**Intelligence Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("company research", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "company.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "company.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



