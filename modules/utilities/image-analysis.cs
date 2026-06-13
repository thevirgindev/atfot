using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;
using MetadataExtractor;
using System.Net.Http;
using System.IO;

namespace atfot.modules.utilities;

[Group("image", "reverse image search and metadata analysis")]
public class ImageCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Dictionary<ulong, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

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

    [SlashCommand("metadata", "extract EXIF metadata from an image URL")]
    public async Task ExtractMetadata(
        [Summary("url", "direct image URL")] string url)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] you need to redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WARN] please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        string dataSummary;
        string? rawJson = null;

        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                await FollowupAsync("[ERR] failed to download image.");
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
                }
            }

            dataSummary = string.Join("\n", exifData);
            if (string.IsNullOrEmpty(dataSummary))
                dataSummary = "No metadata found.";

            // Store raw EXIF as JSON-like string for export
            rawJson = string.Join("\n", exifData);
        }
        catch (Exception ex)
        {
            dataSummary = $"Error: {ex.Message}";
        }

        var fotoLink = $"\n\n[FotoForensics](https://fotoforensics.com/analysis?url={Uri.EscapeDataString(url)})";
        var embed = _embed.CreateMonochromeEmbed("exif metadata", dataSummary + fotoLink, "dark");

        var msg = await FollowupAsync(embed: embed);

        // Cache for export buttons
        _exportCache[msg.Id] = (dataSummary, rawJson, url);

        // Attach export buttons
        var hasJson = !string.IsNullOrEmpty(rawJson);
        var components = new ComponentBuilder()
            .WithButton("TXT", $"exif_export:{msg.Id}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"exif_export:{msg.Id}:json", ButtonStyle.Secondary, disabled: !hasJson);
        await msg.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("exif_export:*:*", ignoreGroupNames: true)]
    public async Task HandleExifExport(string msgIdStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!ulong.TryParse(msgIdStr, out var msgId) || !_exportCache.TryGetValue(msgId, out var data))
        {
            await FollowupAsync("Export data expired or not found. Run the command again.", ephemeral: true);
            return;
        }

        if (format == "json" && string.IsNullOrEmpty(data.rawJson))
        {
            await FollowupAsync("No raw data to export.", ephemeral: true);
            return;
        }

        var dto = new ScanResultDto
        {
            TargetLookup = data.targetLookup,
            ModuleSource = "exif_analysis",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"exif_{data.targetLookup.Replace(" ", "_").Replace("/", "_").Replace(":", "")[..Math.Min(40, data.targetLookup.Length)]}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported EXIF metadata.");
    }
}
