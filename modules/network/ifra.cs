using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.network;

[Group("infra", "domain, IP, network mapping, and dark web investigation")]
public class InfrastructureCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Dictionary<string, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

    public InfrastructureCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        IHttpClientFactory httpFactory)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("investigate", "audit routing topologies and reputation (link-based)")]
    public async Task Investigate(
        [Summary("node", "IP address, domain name, or .onion address")] string node)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] you need to redeem a master key first using `/redeem`.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync("[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        string encoded = Uri.EscapeDataString(node);
        bool isOnion = node.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

        var links = new List<string>
        {
            $"[SpiderFoot](https://spiderfoot.net/?target={encoded}) — run `/osint sf {node}` for aggregated intelligence",
            $"[Shodan](https://www.shodan.io/host/{encoded})",
            $"[VirusTotal](https://www.virustotal.com/gui/search/{encoded})",
            $"[Censys](https://search.censys.io/hosts/{encoded})",
            $"[DNSDumpster](https://dnsdumpster.com/?target={encoded})",
            $"[SecurityTrails](https://securitytrails.com/domain/{encoded}/dns)"
        };

        if (isOnion)
        {
            links.Add($"[Ahmia](https://ahmia.fi/search/?q={encoded})");
            links.Add($"[DarkSearch.io](https://darksearch.io/search?q={encoded})");
        }

        string dataSummary = $"**Target:** `{node}`\n\n";

        if (isOnion)
dataSummary += "[warn] onion services may require Tor Browser to access directly.\n\n";

        // Add ip-api.com geo lookup (free)
        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync($"http://ip-api.com/json/{node}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                if (obj["status"]?.ToString() == "success")
                {
                    var city = obj["city"]?.ToString() ?? "?";
                    var country = obj["country"]?.ToString() ?? "?";
                    var isp = obj["isp"]?.ToString() ?? "?";
dataSummary += $"**geo data (ip-api.com)**\ncity: {city}, country: {country}\nisp: {isp}\n\n";
                }
            }
        }
        catch { }

        dataSummary += $"**Investigation Links:**\n{string.Join("\n", links)}";

        var embed = _embed.CreateMonochromeEmbed("infrastructure diagnostics", dataSummary, "dark");
        var cacheKey = $"infra_inv_{Guid.NewGuid():N}";
        _exportCache[cacheKey] = (dataSummary, null, node);

        var components = new ComponentBuilder()
            .WithButton("TXT", $"infra_inv_export:{cacheKey}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"infra_inv_export:{cacheKey}:json", ButtonStyle.Secondary, disabled: true);

        var msg = await FollowupAsync(embed: embed, components: components.Build());
    }

    [SlashCommand("dns", "perform DNS history and subdomain enumeration (link-based)")]
    public async Task DnsInvestigation(
        [Summary("domain", "domain name")] string domain)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] you need to redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var _))
        {
            await RespondAsync("[WARN] wait a bit.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        string encoded = Uri.EscapeDataString(domain);

        string dataSummary = $"**Domain:** `{domain}`\n\n";

        // Attempt basic DNS resolution via ip-api.com (free)
        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync($"http://ip-api.com/json/{domain}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                if (obj["status"]?.ToString() == "success")
                {
                    var ip = obj["query"]?.ToString() ?? "?";
                    var country = obj["country"]?.ToString() ?? "?";
                    var isp = obj["isp"]?.ToString() ?? "?";
dataSummary += $"**resolved ip:** {ip}\ncountry: {country}\nisp: {isp}\n\n";
                }
            }
        }
        catch { }

        dataSummary += $"**DNS & Subdomain Links:**\n" +
            $"[DNSDumpster](https://dnsdumpster.com/?target={encoded})\n" +
            $"[SecurityTrails DNS History](https://securitytrails.com/domain/{encoded}/dns)\n" +
            $"[ViewDNS.info](https://viewdns.info/dnsrecord/?domain={encoded})\n" +
            $"[Subdomain Finder (CRT.sh)](https://crt.sh/?q=%25.{encoded})\n";

dataSummary += $"\n[info] for deeper analysis, run `/osint sf {domain}` (SpiderFoot) or `/osint subfinder {domain}` / `/osint amass {domain}` (CLI)";

        var embed = _embed.CreateMonochromeEmbed("dns analysis", dataSummary, "gray");
        var cacheKey = $"infra_dns_{Guid.NewGuid():N}";
        _exportCache[cacheKey] = (dataSummary, null, domain);

        var components = new ComponentBuilder()
            .WithButton("TXT", $"infra_dns_export:{cacheKey}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"infra_dns_export:{cacheKey}:json", ButtonStyle.Secondary, disabled: true);

        var msg = await FollowupAsync(embed: embed, components: components.Build());
    }

    [ComponentInteraction("infra_inv_export:*:*", ignoreGroupNames: true)]
    public async Task HandleInvestigateExport(string cacheKey, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!_exportCache.TryGetValue(cacheKey, out var data))
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
            ModuleSource = "infrastructure_telemetry",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"infra_investigate_{data.targetLookup.Replace(" ", "_")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported infrastructure data.");
    }

    [ComponentInteraction("infra_dns_export:*:*", ignoreGroupNames: true)]
    public async Task HandleDnsExport(string cacheKey, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!_exportCache.TryGetValue(cacheKey, out var data))
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
            ModuleSource = "dns_telemetry",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"infra_dns_{data.targetLookup.Replace(" ", "_")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported DNS data.");
    }
}
