using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.http;
using atfot.core.services;

namespace atfot.modules.osint;

[Group("threat", "threat intelligence lookups")]
public class ThreatIntelModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    // Export cache: key = "threat_{msgId}_{cmdName}" -> (summary, rawJson, targetLookup)
    private static readonly Dictionary<string, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

    public ThreatIntelModule(KeyRedemptionService keyService, ApiKeyService apiKeyService,
        ExportService export, IHttpClientFactory httpFactory)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _export = export;
        _httpFactory = httpFactory;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    private async Task ShowResult(ulong msgId, string cmdName, string title, string content, string? rawJson = null, bool showButtons = true)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription($"```\n{content}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();
        var channel = Context.Channel;
        if (channel == null) return;
        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
        {
            if (showButtons)
            {
                var cacheKey = $"threat_{msgId}_{cmdName}";
                _exportCache[cacheKey] = (content, rawJson, title);

                var hasJson = !string.IsNullOrEmpty(rawJson);
                var components = new ComponentBuilder()
                    .WithButton("TXT", $"threat_export:{cacheKey}:txt", ButtonStyle.Secondary)
                    .WithButton("JSON", $"threat_export:{cacheKey}:json", ButtonStyle.Secondary, disabled: !hasJson)
                    .Build();

                await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
            }
            else
            {
                await msg.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
            }
        }
    }

    [ComponentInteraction("threat_export:*:*", ignoreGroupNames: true)]
    public async Task HandleThreatExport(string cacheKey, string format)
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

        var dto = new atfot.models.ScanResultDto
        {
            TargetLookup = data.targetLookup,
            ModuleSource = "threatintel",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"threat_{data.targetLookup.Replace(" ", "_").Replace("/", "_")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported threat intelligence data.");
    }

    // ========== CVE LOOKUP (NVD API, no key) ==========
    [SlashCommand("cve", "look up a CVE vulnerability from NVD")]
    public async Task CveLookup([Summary("cve", "e.g., CVE-2024-1234")] string cveId)
    {
        if (!await EnsureAuthorized()) 
        { 
            await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); 
            return; 
        }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"[INFO] Looking up {cveId}...").Build());

        try
        {
            using var client = _httpFactory.CreateClient();
            var url = $"https://services.nvd.nist.gov/rest/json/cves/2.0?cveId={cveId}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
        await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"[ERR] NVD API error: HTTP {response.StatusCode}").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
                return;
            }
            var json = await response.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            var vulns = obj["vulnerabilities"] as JArray;
            if (vulns == null || vulns.Count == 0)
            {
                await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"[ERR] CVE {cveId} not found.").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
                return;
            }
            var cve = vulns[0]["cve"];
            var description = cve?["descriptions"]?[0]?["value"]?.ToString() ?? "No description.";
            var cvss = cve?["metrics"]?["cvssMetricV31"]?[0]?["cvssData"]?["baseScore"]?.ToString() ?? "N/A";
            var severity = cve?["metrics"]?["cvssMetricV31"]?[0]?["cvssData"]?["baseSeverity"]?.ToString() ?? "N/A";
            var published = cve?["published"]?.ToString() ?? "Unknown";
            var summary = $"**CVE:** {cveId}\n**Published:** {published}\n**CVSS:** {cvss} ({severity})\n**Description:** {description}";
            await ShowResult(loading.Id, "cve", $"CVE – {cveId}", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"[ERR] {ex.Message}").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
        }
    }

    // ========== C2 INFRASTRUCTURE LOOKUP (Abuse.ch Feodo Tracker) ==========
    [SlashCommand("c2", "check IP against known C2 feeds (Abuse.ch Feodo Tracker)")]
    public async Task C2Lookup([Summary("ip", "IP address")] string ip)
    {
        if (!await EnsureAuthorized()) 
        { 
            await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); 
            return; 
        }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"[INFO] Checking C2 feeds for IP {ip}...").Build());

        using var client = _httpFactory.CreateClient();
        try
        {
            var resp = await client.GetAsync("https://feodotracker.abuse.ch/downloads/ipblocklist.json");
            if (!resp.IsSuccessStatusCode)
            {
                await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"[ERR] Feodo Tracker API error: HTTP {resp.StatusCode}").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
                return;
            }
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            var entries = data["ipblocklist"] as JArray;
            if (entries == null)
            {
                await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("[ERR] invalid response from Feodo Tracker.").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
                return;
            }
            var found = entries.FirstOrDefault(e => e["ip"]?.ToString() == ip);
            string resultText;
            if (found != null)
            {
                var firstSeen = found["first_seen"]?.ToString() ?? "unknown";
                var malware = found["malware"]?.ToString() ?? "unknown";
                resultText = $"[ALERT] IP {ip} is listed as C2 server.\nFirst seen: {firstSeen}\nMalware: {malware}";
            }
            else
            {
                resultText = $"[OK] IP {ip} not found in Feodo Tracker C2 list.";
            }
            await ShowResult(loading.Id, "c2", $"C2 Intelligence – {ip}", resultText, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"[ERR] {ex.Message}").WithColor(new Color(0x55, 0x55, 0x55)).WithCurrentTimestamp().WithFooter(f => f.Text = EmbedBuilderService.FooterText).Build());
        }
    }

    // ========== MALWARE HASH CHECK (MalwareBazaar only — VT removed) ==========
    [SlashCommand("malware", "check file hash (SHA256) against MalwareBazaar")]
    public async Task MalwareHash([Summary("hash", "SHA256 hash of the file")] string hash)
    {
        if (!await EnsureAuthorized()) 
        { 
            await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); 
            return; 
        }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"[INFO] Checking hash {hash}...").Build());

        var results = new List<string>();

        // MalwareBazaar (free, no key)
        try
        {
            var client = _httpFactory.CreateClient();
            var content = new StringContent($"{{\"query\":\"get_info\",\"hash\":\"{hash}\"}}", System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://mb-api.abuse.ch/api/v1/", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                if (obj["query_status"]?.ToString() == "ok")
                {
                    var firstSeen = obj["data"]?[0]?["first_seen"]?.ToString() ?? "unknown";
                    var signature = obj["data"]?[0]?["signature"]?.ToString() ?? "none";
                    results.Add($"[MB] MalwareBazaar: first seen {firstSeen}, signature {signature}");

                    // Also show file type, size, tags
                    var fileType = obj["data"]?[0]?["file_type"]?.ToString() ?? "unknown";
                    var fileSize = obj["data"]?[0]?["file_size"]?.ToString() ?? "unknown";
                    var tags = obj["data"]?[0]?["tags"] as JArray;
                    var tagStr = tags != null ? string.Join(", ", tags) : "none";
                    results.Add($"     Type: {fileType}, Size: {fileSize} bytes");
                    results.Add($"     Tags: {tagStr}");

                    // Also check /osint sf for VirusTotal data via SpiderFoot
results.Add($"\n[info] for VirusTotal detections, run `/osint sf {hash}`");

                    await ShowResult(loading.Id, "malware", $"Malware Hash – {hash}", string.Join("\n", results), json);
                }
                else
                {
                    results.Add("[MB] MalwareBazaar: hash not found");
                    await ShowResult(loading.Id, "malware", $"Malware Hash – {hash}", string.Join("\n", results), null);
                }
            }
            else
            {
                results.Add($"[MB] MalwareBazaar: HTTP {response.StatusCode}");
                await ShowResult(loading.Id, "malware", $"Malware Hash – {hash}", string.Join("\n", results), null);
            }
        }
        catch (Exception ex)
        {
            results.Add($"[MB] MalwareBazaar error: {ex.Message}");
            await ShowResult(loading.Id, "malware", $"Malware Hash – {hash}", string.Join("\n", results), null);
        }
    }
}