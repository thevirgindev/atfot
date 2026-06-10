using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.osint;

[Group("identity", "username enumeration and digital footprint mapping")]
public class IdentityCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly ScraperService _scraper;

    public IdentityCmd(
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

    [SlashCommand("profile", "map handle footprints across global OSINT platforms")]
    public async Task ProfileScan(
        [Summary("username", "target handle to track")] string username,
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
            TargetLookup = username,
            ModuleSource = "identity_footprints"
        };

        string clean = Uri.EscapeDataString(username);
        var links = new List<string>
        {
            $"[Namechk](https://namechk.com/check/{clean})",
            $"[Social‑Searcher](https://www.social-searcher.com/search/?q={clean})",
            $"[IntelX](https://intelx.io/?s={clean})",
            $"[WhatsMyName](https://whatsmyname.app/?q={clean})",
            $"[Sherlock](https://github.com/sherlock-project/sherlock) (CLI tool)",
            $"[Maigret](https://github.com/soxoj/maigret) (CLI tool)"
        };
        dto.DeepLinks = links;
        dto.ExtractedData["note"] = "Namechk and Social‑Searcher provide live checks; click links for instant results.";

        var description = $"**Username:** `{username}`\n\n**Investigation Links:**\n" +
                          string.Join("\n", links) +
                          "\n\n*Note: Some tools require manual interaction. Use `/setapikey` to add API keys for automated results.*";
        var embed = _embed.CreateMonochromeEmbed("identity enumeration", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "identity.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "identity.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}