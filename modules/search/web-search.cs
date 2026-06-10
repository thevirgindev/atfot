using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.search;

[Group("search", "advanced search aggregators and archival tools")]
public class SearchCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ScraperService _scraper;

    public SearchCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        IHttpClientFactory httpFactory,
        ScraperService scraper)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
        _scraper = scraper;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("query", "execute advanced search across multiple engines")]
    public async Task SearchQuery(
        [Summary("target", "domain, company, or entity keyword")] string target,
        [Summary("export", "export format (none, txt, json)")] string export = "none")
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("🔒 You need to redeem a master key first using `/redeem`.", ephemeral: true);
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
            TargetLookup = target,
            ModuleSource = "search_aggregators"
        };

        string encoded = Uri.EscapeDataString(target);
        var links = new List<string>
        {
            $"[Google](https://www.google.com/search?q=site%3A{encoded}+ext%3Apdf+OR+ext%3Adoc+OR+ext%3Aconf)",
            $"[DuckDuckGo](https://duckduckgo.com/?q=site%3A{encoded}+filetype%3Adoc)",
            $"[Bing](https://www.bing.com/search?q=site%3A{encoded})",
            $"[Yandex](https://yandex.com/search/?text=site%3A{encoded})",
            $"[Baidu](https://www.baidu.com/s?wd=site%3A{encoded})",
            $"[Wayback Machine](https://web.archive.org/web/*/{target})",
            $"[Archive.today](https://archive.today/search/?q={encoded})"
        };

        dto.DeepLinks = links;

        var googleKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), "google_custom_search");
        if (!string.IsNullOrEmpty(googleKey))
        {
            dto.ExtractedData["google_cse"] = "API key present, but CX needed for full integration. Using link fallback.";
        }

        var linkLines = string.Join("\n", links);
        var description = $"**Target:** `{target}`\n\n**Search Links:**\n{linkLines}";

        var embed = _embed.CreateMonochromeEmbed("search aggregators", description, "dark");

        if (export.ToLower() == "json")
        {
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "search.json", embed: embed);
        }
        else if (export.ToLower() == "txt")
        {
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "search.txt", embed: embed);
        }
        else
        {
            await FollowupAsync(embed: embed);
        }
    }
}