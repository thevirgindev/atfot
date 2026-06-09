using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;

namespace pewbot.modules.search;

[Group("docs", "search documents and slides")]
public class DocumentCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public DocumentCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed, ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("search", "search for PDFs, Word docs, presentations")]
    public async Task SearchDocuments(
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
            ModuleSource = "document_search"
        };

        string encoded = Uri.EscapeDataString(query);
        var links = new List<string>
        {
            $"[Google (filetype:pdf)](https://www.google.com/search?q={encoded}+filetype%3Apdf)",
            $"[Scribd](https://www.scribd.com/search?query={encoded})",
            $"[SlideShare](https://www.slideshare.net/search/slideshow?searchfor={encoded})",
            $"[DocumentCloud](https://www.documentcloud.org/search?q={encoded})",
            $"[Free Full PDF](http://www.freefullpdf.com/search.html?q={encoded})"
        };
        dto.DeepLinks = links;

        var description = $"**Query:** {query}\n\n**Document Sources:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("document search", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "docs.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "docs.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}



