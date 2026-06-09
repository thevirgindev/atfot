using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.utilities;

[Group("keyword", "keyword discovery and trend analysis")]
public class KeywordCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public KeywordCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("trends", "get Google Trends data and keyword suggestions")]
    public async Task KeywordTrends(
        [Summary("topic", "search topic")] string topic,
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
            TargetLookup = topic,
            ModuleSource = "keyword_trends"
        };

        string encoded = Uri.EscapeDataString(topic);
        var links = new List<string>
        {
            $"[Google Trends](https://trends.google.com/trends/explore?q={encoded})",
            $"[KeywordTool.io](https://keywordtool.io/search/{encoded})",
            $"[Ubersuggest](https://app.neilpatel.com/en/ubersuggest/{encoded})",
            $"[WordTracker](https://www.wordtracker.com/search?q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Topic:** {topic}\n\n**Trend & Keyword Tools:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("keyword research", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "keyword.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "keyword.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



