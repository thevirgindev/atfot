using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.search;

[Group("pastebin", "pastebin search and discovery")]
public class PastebinCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly ScraperService _scraper;

    public PastebinCmd(
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

    [SlashCommand("search", "search for keywords across major pastebin sites")]
    public async Task SearchPastebin(
        [Summary("keyword", "search term")] string keyword,
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
            TargetLookup = keyword,
            ModuleSource = "pastebin_search"
        };

        string encoded = Uri.EscapeDataString(keyword);
        var links = new List<string>
        {
            $"[Pastebin.com](https://pastebin.com/search?q={encoded})",
            $"[Pastebin.pl](https://pastebin.pl/search/{encoded})",
            $"[Paste.ee](https://paste.ee/search?q={encoded})",
            $"[ControlC](https://controlc.com/index.php?act=search&s={encoded})",
            $"[Rentry](https://rentry.co/search?q={encoded})",
            $"[JustPaste.it](https://justpaste.it/search?q={encoded})"
        };
        dto.DeepLinks = links;

        // Attempt to scrape Pastebin search results (Pastebin blocks bots, but we try with user-agent)
        try
        {
            var html = await _scraper.FetchHtmlAsync($"https://pastebin.com/search?q={encoded}");
            dto.RawApiResponse = html.Substring(0, Math.Min(2000, html.Length));
            dto.ExtractedData["scraped"] = "Partial HTML captured (first 2000 chars)";
        }
        catch (Exception ex)
        {
            dto.ExtractedData["scrape_error"] = ex.Message;
        }

        var description = $"**Keyword:** `{keyword}`\n\n**Pastebin Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("pastebin discovery", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "pastebin.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "pastebin.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



