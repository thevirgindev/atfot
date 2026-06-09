using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.utilities;

[Group("news", "news aggregation and fact checking")]
public class NewsCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public NewsCmd(
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

    [SlashCommand("latest", "get latest news on a topic")]
    public async Task LatestNews(
        [Summary("topic", "news keyword")] string topic,
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
            TargetLookup = topic,
            ModuleSource = "news"
        };

        string encoded = Uri.EscapeDataString(topic);
        var links = new List<string>
        {
            $"[Google News](https://news.google.com/search?q={encoded})",
            $"[Reuters](https://www.reuters.com/site-search/?search={encoded})",
            $"[BBC News](https://www.bbc.com/search?q={encoded})",
            $"[AP News](https://apnews.com/search?q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Topic:** `{topic}`\n\n**News Sources:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("news aggregation", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "news.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "news.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("factcheck", "verify a claim using fact-checking websites")]
    public async Task FactCheck(
        [Summary("claim", "statement to verify")] string claim,
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
            TargetLookup = claim,
            ModuleSource = "factcheck"
        };

        string encoded = Uri.EscapeDataString(claim);
        var links = new List<string>
        {
            $"[Snopes](https://www.snopes.com/search/{encoded})",
            $"[FactCheck.org](https://www.factcheck.org/search/?s={encoded})",
            $"[Full Fact](https://fullfact.org/search/?q={encoded})",
            $"[PolitiFact](https://www.politifact.com/search/?q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Claim:** `{claim}`\n\n**Fact-Checking Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("fact check", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "factcheck.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "factcheck.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



