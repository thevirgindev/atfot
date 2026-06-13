using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.http;
using atfot.core.services;
using atfot.models;
using atfot.utils;

namespace atfot.modules.osint;

[Group("osint", "extended OSINT utilities")]
public partial class OsintToolsCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    // Cache for export buttons: "osint_{loadingMsgId}_{cmdName}" -> (summary, rawJson, targetLookup)
    private static readonly Dictionary<string, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

    public OsintToolsCmd(KeyRedemptionService keyService, ApiKeyService apiKeyService, CooldownService cooldown,
        EmbedBuilderService embed, ExportService export, IHttpClientFactory httpFactory, BotConfig botConfig)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
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
                var cacheKey = $"osint_{msgId}_{cmdName}";
                _exportCache[cacheKey] = (content, rawJson, title);

                var hasJson = !string.IsNullOrEmpty(rawJson);
                var components = new ComponentBuilder()
                    .WithButton("TXT", $"osint_export:{cacheKey}:txt", ButtonStyle.Secondary)
                    .WithButton("JSON", $"osint_export:{cacheKey}:json", ButtonStyle.Secondary, disabled: !hasJson)
                    .Build();

                await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
            }
            else
            {
                await msg.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
            }
        }
    }

    private async Task ShowError(ulong msgId, string message)
    {
        var embed = new EmbedBuilder()
            .WithDescription($"[err] {message}")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();
        var channel = Context.Channel;
        if (channel == null) return;
        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
    }

    private async Task<string> RunCli(string command, string args, int timeoutSec = 30)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = new Process { StartInfo = psi };

            try
            {
                proc.Start();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                return $"[ERR] command '{command}' not found.\nthis tool is only available when running the bot inside the Docker container.\nplease use the official ATFOT Docker image.";
            }

            var outputTask = proc.StandardOutput!.ReadToEndAsync();
            var errorTask = proc.StandardError!.ReadToEndAsync();
            if (await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeoutSec * 1000)) != proc.WaitForExitAsync())
            {
                proc.Kill();
                return $"[WARN] timeout after {timeoutSec} seconds.";
            }
            await outputTask;
            await errorTask;
            var output = outputTask.Result;
            var error = errorTask.Result;
            return string.IsNullOrEmpty(error) ? output : output + "\n--- STDERR ---\n" + error;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return $"[ERR] command '{command}' not found.\nthis tool is only available when running the bot inside the Docker container.\nplease use the official ATFOT Docker image.";
        }
        catch (Exception ex) when (ex.Message.Contains("No such file") || ex.Message.Contains("not found"))
        {
            return $"[ERR] command '{command}' not found.\nthis tool is only available when running the bot inside the Docker container.\nplease use the official ATFOT Docker image.";
        }
    }

    private async Task<T?> ExecuteWithKeyRotation<T>(string discordId, string service, Func<string, Task<T>> apiCall, Func<T, bool>? isRateLimit = null, Func<T, bool>? isQuotaError = null)
    {
        var keyResult = await _apiKeyService.GetNextAvailableKeyAsync(discordId, service);
        if (keyResult == null) return default;
        try
        {
            var result = await apiCall(keyResult.Value.apiKey);
            if (isRateLimit != null && isRateLimit(result))
            {
                await _apiKeyService.MarkKeyRateLimitedAsync(keyResult.Value.keyId, discordId);
                return await ExecuteWithKeyRotation(discordId, service, apiCall, isRateLimit, isQuotaError);
            }
            if (isQuotaError != null && isQuotaError(result))
            {
                await _apiKeyService.MarkKeyQuotaExhaustedAsync(keyResult.Value.keyId, discordId);
                return await ExecuteWithKeyRotation(discordId, service, apiCall, isRateLimit, isQuotaError);
            }
            await _apiKeyService.IncrementUsageAsync(keyResult.Value.keyId, discordId);
            return result;
        }
        catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limit"))
        {
            await _apiKeyService.MarkKeyRateLimitedAsync(keyResult.Value.keyId, discordId);
            return await ExecuteWithKeyRotation(discordId, service, apiCall, isRateLimit, isQuotaError);
        }
    }

    [ComponentInteraction("osint_export:*:*", ignoreGroupNames: true)]
    public async Task HandleOsintExport(string cacheKey, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!_exportCache.TryGetValue(cacheKey, out var data))
        {
            await FollowupAsync("export data expired or not found. Run the command again.", ephemeral: true);
            return;
        }

        if (format == "json" && string.IsNullOrEmpty(data.rawJson))
        {
            await FollowupAsync("no raw JSON data to export.", ephemeral: true);
            return;
        }

        var dto = new ScanResultDto
        {
            TargetLookup = data.targetLookup,
            ModuleSource = "osinttool",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"osint_{data.targetLookup.Replace(" ", "_").Replace("/", "_")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"exported OSINT data.");
    }

    // ==================== KEPT API COMMANDS ====================

    // PeopleDataLabs
    [SlashCommand("pdl", "enrich person by email (requires PeopleDataLabs key)")]
    public async Task Pdl([Summary("email")] string email)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"enriching {email}..."));
        var result = await ExecuteWithKeyRotation<JObject?>(
            Context.User.Id.ToString(), "peopledatalabs",
            async (key) => {
                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
                var content = new StringContent($"{{\"email\":\"{email}\"}}", System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.peopledatalabs.com/v2/enrich/person", content);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            });
        if (result == null)
        {
            await ShowError(loading.Id, "no working keys or no data.");
            return;
        }
        var data = result["data"];
        if (data == null) throw new Exception("No data found");
        var name = data["full_name"]?.Value<string>() ?? "Unknown";
        var location = data["location"]?["name"]?.Value<string>() ?? "Unknown";
        var job = data["job_title"]?.Value<string>() ?? "Unknown";
        var company = data["company"]?["name"]?.Value<string>() ?? "Unknown";
        var summary = $"[info] {email}\nname: {name}\nlocation: {location}\njob: {job}\ncompany: {company}";
        await ShowResult(loading.Id, "pdl", "PeopleDataLabs", summary, result.ToString());
    }

    // ipgeolocation.io
    [SlashCommand("ipgeo", "geolocation by IP (requires ipgeolocation.io key)")]
    public async Task IpGeo([Summary("ip")] string ip)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"geolocating {ip}..."));
        var result = await ExecuteWithKeyRotation<JObject?>(
            Context.User.Id.ToString(), "ipgeolocation",
            async (key) => {
                var client = _httpFactory.CreateClient();
                var response = await client.GetAsync($"https://api.ipgeolocation.io/ipgeo?ip={ip}&apiKey={key}");
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            });
        if (result == null)
        {
            await ShowError(loading.Id, "no working keys or no data.");
            return;
        }
        var city = result["city"]?.Value<string>() ?? "Unknown";
        var country = result["country_name"]?.Value<string>() ?? "Unknown";
        var isp = result["isp"]?.Value<string>() ?? "Unknown";
        var lat = result["latitude"]?.Value<double>() ?? 0;
        var lon = result["longitude"]?.Value<double>() ?? 0;
        var summary = $"[info] ip: {ip}\n{city}, {country}\nisp: {isp}\ncoords: {lat},{lon}";
        await ShowResult(loading.Id, "ipgeo", "ipgeolocation.io", summary, result.ToString());
    }

    // ip-api.com (free)
    [SlashCommand("ipapi", "geolocation by IP (free)")]
    public async Task IpApiFree([Summary("ip")] string ip)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"checking ip-api.com for {ip}..."));
        var client = _httpFactory.CreateClient();
        var url = $"http://ip-api.com/json/{ip}";
        try
        {
            var resp = await client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            if (data["status"]?.Value<string>() == "fail")
            {
                await ShowError(loading.Id, $"{data["message"]}");
                return;
            }
            var city = data["city"]?.Value<string>() ?? "Unknown";
            var country = data["country"]?.Value<string>() ?? "Unknown";
            var isp = data["isp"]?.Value<string>() ?? "Unknown";
            var lat = data["lat"]?.Value<double>() ?? 0;
            var lon = data["lon"]?.Value<double>() ?? 0;
            var summary = $"[info] ip: {ip}\n{city}, {country}\nisp: {isp}\ncoords: {lat},{lon}";
            await ShowResult(loading.Id, "ipapi", "ip-api.com", summary, json);
        }
        catch (Exception ex) { await ShowError(loading.Id, $"error: {ex.Message}"); }
    }

    // OnionEngine
    [SlashCommand("onion", "search dark web (requires OnionEngine key)")]
    public async Task Onion([Summary("keyword")] string keyword)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"searching OnionEngine for {keyword}..."));
        var result = await ExecuteWithKeyRotation<JObject?>(
            Context.User.Id.ToString(), "onionengine",
            async (key) => {
                var client = _httpFactory.CreateClient();
                var response = await client.GetAsync($"https://onionengine.com/api/search?q={Uri.EscapeDataString(keyword)}&apiKey={key}");
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            });
        if (result == null)
        {
            await ShowError(loading.Id, "no working keys or no results.");
            return;
        }
        var results = result["results"] as JArray;
        if (results == null || results.Count == 0)
        {
            await ShowResult(loading.Id, "onion", "OnionEngine", $"[warn] no results for {keyword}.", result.ToString(), showButtons: false);
            return;
        }
        var list = string.Join("\n", results.Take(10).Select(r => $"- {r["title"]} ({r["url"]})"));
        var summary = $"[done] found {results.Count} results:\n{list}";
        await ShowResult(loading.Id, "onion", "OnionEngine", summary, result.ToString());
    }

    // ==================== SPIDERFOOT COMMAND ====================

    [SlashCommand("sf", "run SpiderFoot OSINT aggregation via Docker CLI")]
    public async Task SpiderFootSf([Summary("target", "IP, domain, email, or hash to scan")] string target)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running SpiderFoot on `{target}`... (may take 60-180 seconds)"));

        var modules = "sfp_dnsresolve,sfp_shodan,sfp_virustotal,sfp_alienvault,sfp_hunter,sfp_abuseipdb,sfp_greynoise,sfp_urlscan,sfp_pwned,sfp_securitytrails,sfp_censys";
        var rawOutput = await RunCli("sf", $" -s {target} -o json -m {modules}", 180);

        // Check for errors
        if (rawOutput.StartsWith("[ERR]") || rawOutput.StartsWith("[WARN]"))
        {
            await ShowError(loading.Id, rawOutput);
            return;
        }

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            await ShowError(loading.Id, $"SpiderFoot returned empty output for `{target}`. the modules may have found nothing.");
            return;
        }

        JToken? parsed = null;
        if (rawOutput.TrimStart().StartsWith("["))
        {
            try { parsed = JArray.Parse(rawOutput); }
            catch { }
        }
        else if (rawOutput.TrimStart().StartsWith("{"))
        {
            try { parsed = JObject.Parse(rawOutput); }
            catch { }
        }

        if (parsed == null)
        {
            var truncated = rawOutput.Length > 4000 ? rawOutput[..4000] + "\n... (truncated)" : rawOutput;
            await ShowResult(loading.Id, "sf", $"SpiderFoot — {target}", truncated, rawOutput);
            return;
        }

        var findings = new List<(string Type, string Module, string Summary)>();
        var openPorts = new List<string>();
        var subdomains = new List<string>();
        var maliciousFlags = new List<string>();
        var emails = new List<string>();
        var ips = new List<string>();
        var domains = new List<string>();
        var urls = new List<string>();
        var otherFindings = new List<string>();

        JArray results = parsed as JArray ?? new JArray(parsed);

        foreach (var item in results)
        {
            var type = item["type"]?.ToString() ?? "UNKNOWN";
            var module = item["module"]?.ToString() ?? "unknown";
            var data = item["data"];

            switch (type.ToUpperInvariant())
            {
                case "PORT":
                    var port = data?["port"]?.ToString() ?? "?";
                    var proto = data?["protocol"]?.ToString() ?? "tcp";
                    var state = data?["state"]?.ToString() ?? "open";
                    if (state == "open")
                        openPorts.Add($"{port}/{proto}");
                    findings.Add(("PORT", module, $"{port}/{proto} ({state})"));
                    break;

                case "SUBDOMAIN":
                    var sub = data?["host"]?.ToString() ?? data?["subdomain"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(sub)) subdomains.Add(sub);
                    findings.Add(("SUBDOMAIN", module, sub));
                    break;

                case "MALICIOUS":
                case "THREAT":
                    var desc = data?["description"]?.ToString() ?? data?["summary"]?.ToString() ?? module;
                    maliciousFlags.Add(desc);
                    findings.Add(("MALICIOUS", module, desc));
                    break;

                case "EMAIL_ADDRESS":
                    var email = data?["email"]?.ToString() ?? data?["address"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(email)) emails.Add(email);
                    findings.Add(("EMAIL", module, email));
                    break;

                case "IP_ADDRESS":
                    var ip = data?["ip"]?.ToString() ?? data?["address"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(ip)) ips.Add(ip);
                    findings.Add(("IP", module, ip));
                    break;

                case "INTERNET_NAME":
                    var d = data?["host"]?.ToString() ?? data?["domain"]?.ToString() ?? data?["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(d) && !subdomains.Contains(d)) domains.Add(d);
                    findings.Add(("DOMAIN", module, d));
                    break;

                case "URL":
                    var u = data?["url"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(u)) urls.Add(u);
                    findings.Add(("URL", module, u));
                    break;

                default:
                    var summary = data?.ToString()?.Length > 100 ? data.ToString()[..100] + "..." : data?.ToString() ?? "";
                    otherFindings.Add($"[{type}] {module}: {summary}");
                    findings.Add((type, module, summary));
                    break;
            }
        }

        var lines = new List<string>();
        lines.Add($"[done] spiderfoot scan for `{target}`");
        lines.Add($"findings: {findings.Count}");

        if (openPorts.Count > 0)
            lines.Add($"\n**open ports:** {string.Join(", ", openPorts.Take(20))}{(openPorts.Count > 20 ? $" (+{openPorts.Count - 20} more)" : "")}");

        if (subdomains.Count > 0)
            lines.Add($"\n**subdomains:** {subdomains.Count} found — {string.Join(", ", subdomains.Take(10))}{(subdomains.Count > 10 ? $" (+{subdomains.Count - 10} more)" : "")}");

        if (maliciousFlags.Count > 0)
            lines.Add($"\n**malicious/threat detections:** {maliciousFlags.Count}\n{string.Join("\n", maliciousFlags.Take(10).Select(m => $"  - {m}"))}{(maliciousFlags.Count > 10 ? $"\n  ... and {maliciousFlags.Count - 10} more" : "")}");

        if (emails.Count > 0)
            lines.Add($"\n**emails:** {string.Join(", ", emails.Take(10))}{(emails.Count > 10 ? $" (+{emails.Count - 10} more)" : "")}");

        if (ips.Count > 0)
            lines.Add($"\n**ips:** {string.Join(", ", ips.Take(10))}{(ips.Count > 10 ? $" (+{ips.Count - 10} more)" : "")}");

        if (domains.Count > 0)
            lines.Add($"\n**domains:** {string.Join(", ", domains.Take(10))}{(domains.Count > 10 ? $" (+{domains.Count - 10} more)" : "")}");

        if (urls.Count > 0)
            lines.Add($"\n**urls:** {string.Join(", ", urls.Take(5))}{(urls.Count > 5 ? $" (+{urls.Count - 5} more)" : "")}");

        if (otherFindings.Count > 0)
            lines.Add($"\n**other findings:** {otherFindings.Count} types — {string.Join(", ", otherFindings.Take(5))}{(otherFindings.Count > 5 ? $" (+{otherFindings.Count - 5} more)" : "")}");

        var summaryText = string.Join("\n", lines);
        if (summaryText.Length > 4000)
            summaryText = summaryText[..3900] + "\n... (truncated — download full json)";

        await ShowResult(loading.Id, "sf", $"SpiderFoot — {target}", summaryText, rawOutput);
    }

    // ==================== CLI COMMANDS ====================

    [SlashCommand("sherlock", "search username across 400+ sites (CLI)")]
    public async Task Sherlock([Summary("username")] string username)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running Sherlock on {username}..."));
        var output = await RunCli("sherlock", username, 45);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "sherlock", "Sherlock", output, null);
    }

    [SlashCommand("harvester", "gather emails/subdomains (CLI)")]
    public async Task Harvester([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running theHarvester on {domain}..."));
        var output = await RunCli("theHarvester", $"-d {domain} -b all", 90);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "harvester", "theHarvester", output, null);
    }

    [SlashCommand("recon", "web reconnaissance (CLI)")]
    public async Task Recon([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running Recon-ng on {domain}..."));
        var output = await RunCli("recon-ng", $"-w {domain}", 90);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "recon", "Recon-ng", output, null);
    }

    [SlashCommand("subfinder", "enumerate subdomains (CLI)")]
    public async Task Subfinder([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running Subfinder on {domain}..."));
        var output = await RunCli("subfinder", $"-d {domain} -silent", 45);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "subfinder", "Subfinder", output, null);
    }

    [SlashCommand("amass", "attack surface mapping (CLI)")]
    public async Task Amass([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running AMASS on {domain}..."));
        var output = await RunCli("amass", $"-d {domain} -o /dev/stdout", 120);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "amass", "AMASS", output, null);
    }

    [SlashCommand("torbot", "crawl onion sites (CLI)")]
    public async Task TorBot([Summary("onion")] string onionUrl)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running TorBot on {onionUrl}..."));
        var output = await RunCli("torbot", $"-u http://{onionUrl}", 90);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "torbot", "TorBot", output, null);
    }

    [SlashCommand("odcrawler", "username disclosure (CLI)")]
    public async Task OdCrawler([Summary("username")] string username)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"running maigret on {username}..."));
        var output = await RunCli("maigret", username, 60);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "odcrawler", "OD Crawler (via maigret)", output, null);
    }

    [SlashCommand("whocord", "comprehensive OSINT (CLI)")]
    public async Task WhoCord([Summary("type", "username, email, or discord")] string type, [Summary("target")] string target)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        await DeferAsync();
        string arg = type.ToLower() switch
        {
            "username" => $"-u {target}",
            "email" => $"-e {target}",
            "discord" => $"-d {target}",
            _ => throw new ArgumentException("Invalid type. Use username, email, or discord.")
        };
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running WhoCord on {target}...").Build());
        var output = await RunCli("whocord", arg, 60);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "whocord", "WhoCord", output, null);
    }

    [SlashCommand("sublist3r", "enumerate subdomains using OSINT (CLI)")]
    public async Task Sublist3r([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running Sublist3r on {domain}...").Build());
        var output = await RunCli("sublist3r", $"-d {domain} -t 10", 120);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "sublist3r", "Sublist3r", output, null);
    }

    [SlashCommand("whatweb", "fingerprint websites (CLI)")]
    public async Task WhatWeb([Summary("target", "URL or IP")] string target)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running WhatWeb on {target}...").Build());
        var output = await RunCli("whatweb", target, 60);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "whatweb", "WhatWeb", output, null);
    }

    [SlashCommand("dnsrecon", "DNS enumeration (CLI)")]
    public async Task DnsRecon([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running DNSRecon on {domain}...").Build());
        var output = await RunCli("dnsrecon", $"-d {domain} -t axfr,zonewalk,bing", 90);
        if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
        await ShowResult(loading.Id, "dnsrecon", "DNSRecon", output, null);
    }
}