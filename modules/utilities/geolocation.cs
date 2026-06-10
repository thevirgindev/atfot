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
        [Summary("location", "address, city, or lat,lon coordinates")] string location,
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
            TargetLookup = location,
            ModuleSource = "geolocation"
        };

        string encoded = Uri.EscapeDataString(location);
        var links = new List<string>
        {
            $"[Google Maps](https://www.google.com/maps/search/{encoded})",
            $"[Wikimapia](https://wikimapia.org/#search={encoded})",
            $"[SunCalc](https://www.suncalc.org/#/{encoded})",
            $"[What3Words](https://what3words.com/) (enter manually)",
            $"[GPS Visualizer](https://www.gpsvisualizer.com/maps?q={encoded})"
        };
        dto.DeepLinks = links;

        try
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ATFOT/1.0");
            var response = await client.GetAsync($"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=1");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                dto.RawApiResponse = json;
                var array = JArray.Parse(json);
                if (array.Count > 0)
                {
                    var lat = array[0]["lat"]?.ToString();
                    var lon = array[0]["lon"]?.ToString();
                    dto.ExtractedData["nominatim_lat"] = lat ?? "unknown";
                    dto.ExtractedData["nominatim_lon"] = lon ?? "unknown";
                    dto.Summary = $"Coordinates: {lat}, {lon}";
                }
            }
        }
        catch (Exception ex)
        {
            dto.ExtractedData["geocode_error"] = ex.Message;
        }

        var description = $"**Location:** `{location}`\n\n**Map & Analysis Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("geospatial analysis", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "geo.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "geo.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}