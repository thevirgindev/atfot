using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.search;

[Group("code", "source code search and repository discovery")]
public class CodeCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly ScraperService _scraper;

    public CodeCmd(
        KeyRedemptionService keyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        ScraperService scraper)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _scraper = scraper;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("search", "search public code repositories for keywords")]
    public async Task CodeSearch(
        [Summary("query", "search term, function name, or API key pattern")] string query,
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
            ModuleSource = "code_search"
        };

        string encoded = Uri.EscapeDataString(query);
        var links = new List<string>
        {
            $"[GitHub Code](https://github.com/search?type=code&q={encoded})",
            $"[grep.app](https://grep.app/search?q={encoded})",
            $"[SearchCode](https://searchcode.com/?q={encoded})",
            $"[SourceGraph](https://sourcegraph.com/search?q={encoded})"
        };
        dto.DeepLinks = links;

        // Optional: scrape GitHub search (GitHub may block, but we try with user-agent)
        try
        {
            var html = await _scraper.FetchHtmlAsync($"https://github.com/search?q={encoded}&type=code");
            dto.RawApiResponse = html.Substring(0, Math.Min(2000, html.Length));
            dto.ExtractedData["github_scrape"] = "Partial HTML captured";
        }
        catch (Exception ex)
        {
            dto.ExtractedData["github_error"] = ex.Message;
        }

        var description = $"**Query:** `{query}`\n\n**Code Search Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("code search", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "code.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "code.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}


