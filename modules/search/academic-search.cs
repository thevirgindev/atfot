using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.search;

[Group("academic", "academic research and scholarly search")]
public class AcademicCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public AcademicCmd(
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

    [SlashCommand("search", "search academic papers and grey literature")]
    public async Task AcademicSearch(
        [Summary("query", "research topic or paper title")] string query,
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
            TargetLookup = query,
            ModuleSource = "academic"
        };

        string encoded = Uri.EscapeDataString(query);
        var links = new List<string>
        {
            $"[Google Scholar](https://scholar.google.com/scholar?q={encoded})",
            $"[JSTOR](https://www.jstor.org/action/doBasicSearch?Query={encoded})",
            $"[PubMed](https://pubmed.ncbi.nlm.nih.gov/?term={encoded})",
            $"[arXiv](https://arxiv.org/search/?query={encoded})",
            $"[OA.mg](https://oa.mg/search?q={encoded})",
            $"[Core](https://core.ac.uk/search?q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Query:** `{query}`\n\n**Academic Databases:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("academic research", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "academic.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "academic.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



