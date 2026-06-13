using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.utilities;

[Group("geo", "geospatial research and mapping tools")]
public class GeolocationCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Dictionary<ulong, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

    public GeolocationCmd(
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

    [SlashCommand("locate", "convert address or coordinates to map links and SunCalc data")]
    public async Task Locate(
        [Summary("location", "address, city, or lat,lon coordinates")] string location)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] you need to redeem a master key first using `/redeem`.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WARN] please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        string encoded = Uri.EscapeDataString(location);
        var links = new List<string>
        {
            $"[Google Maps](https://www.google.com/maps/search/{encoded})",
            $"[Wikimapia](https://wikimapia.org/#search={encoded})",
            $"[SunCalc](https://www.suncalc.org/#/{encoded})",
            $"[What3Words](https://what3words.com/) (enter manually)",
            $"[GPS Visualizer](https://www.gpsvisualizer.com/maps?q={encoded})"
        };

        string dataSummary = "";
        string? rawJson = null;

        try
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ATFOT/1.0");
            var response = await client.GetAsync($"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=1");
            if (response.IsSuccessStatusCode)
            {
                rawJson = await response.Content.ReadAsStringAsync();
                var array = JArray.Parse(rawJson);
                if (array.Count > 0)
                {
                    var lat = array[0]["lat"]?.ToString() ?? "?";
                    var lon = array[0]["lon"]?.ToString() ?? "?";
                    var displayName = array[0]["display_name"]?.ToString() ?? location;
                    var city = array[0]["address"]?["city"]?.ToString()
                        ?? array[0]["address"]?["town"]?.ToString()
                        ?? array[0]["address"]?["village"]?.ToString()
                        ?? "?";
                    var country = array[0]["address"]?["country"]?.ToString() ?? "?";
                    var category = array[0]["category"]?.ToString() ?? "?";
                    var type = array[0]["type"]?.ToString() ?? "?";

                    dataSummary =
                        $"**Location:** {displayName}\n" +
                        $"**Coordinates:** {lat}, {lon}\n" +
                        $"**City:** {city}\n" +
                        $"**Country:** {country}\n" +
                        $"**Category:** {category} ({type})\n\n" +
                        $"**Maps & Analysis:**\n{string.Join("\n", links)}";
                }
                else
                {
                    dataSummary = $"**Location:** `{location}`\nNo results from Nominatim.\n\n**Maps & Analysis:**\n{string.Join("\n", links)}";
                }
            }
            else
            {
                dataSummary = $"**Location:** `{location}`\nNominatim returned HTTP {response.StatusCode}.\n\n**Maps & Analysis:**\n{string.Join("\n", links)}";
            }
        }
        catch (Exception ex)
        {
            dataSummary = $"**Location:** `{location}`\nGeocode error: {ex.Message}\n\n**Maps & Analysis:**\n{string.Join("\n", links)}";
        }

        var embed = _embed.CreateMonochromeEmbed("geospatial analysis", dataSummary, "dark");

        var msg = await FollowupAsync(embed: embed);

        // Cache for export buttons
        _exportCache[msg.Id] = (dataSummary, rawJson, location);

        // Attach export buttons
        var hasJson = !string.IsNullOrEmpty(rawJson);
        var components = new ComponentBuilder()
            .WithButton("TXT", $"geo_export:{msg.Id}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"geo_export:{msg.Id}:json", ButtonStyle.Secondary, disabled: !hasJson);
        await msg.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("geo_export:*:*", ignoreGroupNames: true)]
    public async Task HandleGeoExport(string msgIdStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!ulong.TryParse(msgIdStr, out var msgId) || !_exportCache.TryGetValue(msgId, out var data))
        {
            await FollowupAsync("Export data expired or not found. Run the command again.", ephemeral: true);
            return;
        }

        if (format == "json" && string.IsNullOrEmpty(data.rawJson))
        {
            await FollowupAsync("No raw JSON data to export.", ephemeral: true);
            return;
        }

        var dto = new ScanResultDto
        {
            TargetLookup = data.targetLookup,
            ModuleSource = "geolocation",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"geo_{data.targetLookup.Replace(" ", "_").Replace(",", "")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported geolocation data.");
    }
}
