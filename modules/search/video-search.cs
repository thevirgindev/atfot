using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.search;

[Group("video", "YouTube and online video OSINT")]
public class VideoCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public VideoCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("search", "search videos and subtitles")]
    public async Task VideoSearch(
        [Summary("query", "search term")] string query,
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
            TargetLookup = query,
            ModuleSource = "video_search"
        };

        string encoded = Uri.EscapeDataString(query);
        var links = new List<string>
        {
            $"[YouTube Search](https://www.youtube.com/results?search_query={encoded})",
            $"[Filmot (Subtitle Search)](https://filmot.com/search?q={encoded})",
            $"[YouTube Geofind](https://mattw.io/youtube-geofind/?q={encoded})",
            $"[YouTube Metadata](https://mattw.io/youtube-metadata/) (manual video ID)"
        };
        dto.DeepLinks = links;

        var description = $"**Query:** {query}\n\n**Video Tools:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("video OSINT", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "video.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "video.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



