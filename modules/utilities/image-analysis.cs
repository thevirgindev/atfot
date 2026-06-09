using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.models;
using MetadataExtractor;
using System.Net.Http;
using System.IO;

namespace pewbot.modules.utilities;

[Group("image", "reverse image search and metadata analysis")]
public class ImageCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    public ImageCmd(
        KeyRedemptionService keyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        IHttpClientFactory httpFactory)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("reverse", "reverse image search using public engines")]
    public async Task ReverseImage(
        [Summary("url", "direct image URL")] string url,
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
            TargetLookup = url,
            ModuleSource = "image_analysis"
        };

        string encoded = Uri.EscapeDataString(url);
        var links = new List<string>
        {
            $"[TinEye](https://tineye.com/search?url={encoded})",
            $"[Google Images](https://lens.google.com/uploadbyurl?url={encoded})",
            $"[Yandex Images](https://yandex.com/images/search?url={encoded}&rpt=imageview)",
            $"[PimEyes](https://pimeyes.com/en) (manual upload)",
            $"[FaceCheck.ID](https://facecheck.id) (manual upload)"
        };
        dto.DeepLinks = links;

        var description = $"**Image URL:** {url}\n\n**Reverse Search Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("reverse image search", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "image_reverse.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "image_reverse.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("metadata", "extract EXIF metadata from an image URL")]
    public async Task ExtractMetadata(
        [Summary("url", "direct image URL")] string url,
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
            TargetLookup = url,
            ModuleSource = "exif_analysis"
        };

        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                await FollowupAsync("❌ Failed to download image.");
                return;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var ms = new MemoryStream(bytes);
            var directories = ImageMetadataReader.ReadMetadata(ms);
            var exifData = new List<string>();
            foreach (var dir in directories)
            {
                foreach (var tag in dir.Tags)
                {
                    exifData.Add($"{dir.Name} - {tag.Name}: {tag.Description}");
                    dto.ExtractedData[$"{dir.Name}_{tag.Name}"] = tag.Description ?? "null";
                }
            }
            dto.Summary = string.Join("\n", exifData);
            dto.DeepLinks.Add($"[FotoForensics](https://fotoforensics.com/analysis?url={Uri.EscapeDataString(url)})");
        }
        catch (Exception ex)
        {
            dto.ExtractedData["error"] = ex.Message;
        }

        var embed = _embed.CreateMonochromeEmbed("exif metadata", dto.Summary ?? "No metadata found.", "dark");
        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "exif.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "exif.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}


