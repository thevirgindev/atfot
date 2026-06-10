using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;
using atfot.utils;

namespace atfot.modules.osint;

[Group("osint", "extended OSINT utilities")]
public class OsintToolsCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    public OsintToolsCmd(KeyRedemptionService keyService, ApiKeyService apiKeyService, CooldownService cooldown,
        EmbedBuilderService embed, ExportService export, IHttpClientFactory httpFactory)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    private async Task ShowResult(ulong msgId, string title, string content, string? rawJson = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription($"```\n{content}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "ATFOT || made by @thevirgindev")
            .Build();

        var channel = Context.Channel;
        if (channel == null) return;  // Fix: guard against null channel

        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
        {
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
            if (!string.IsNullOrEmpty(rawJson))
            {
                var dto = new ScanResultDto { TargetLookup = title, ModuleSource = "osinttool", RawApiResponse = rawJson, Summary = content };
                using var stream = _export.BuildJsonStream(dto);
                await channel.SendFileAsync(stream, "result.json", "Exported data.");
            }
        }
    }

    private async Task<string> RunCli(string command, string args, int timeoutSec = 30)
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
        proc.Start();

        // Fix: use null-forgiving operator because streams are guaranteed non-null after Start() with redirection enabled
        var outputTask = proc.StandardOutput!.ReadToEndAsync();
        var errorTask = proc.StandardError!.ReadToEndAsync();

        if (await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeoutSec * 1000)) != proc.WaitForExitAsync())
        {
            proc.Kill();
            return $"Timeout after {timeoutSec} seconds.";
        }
        await outputTask;
        await errorTask;
        var output = outputTask.Result;
        var error = errorTask.Result;
        return string.IsNullOrEmpty(error) ? output : error;
    }

    // ========== 1. HaveIBeenPwned (no key) ==========
    [SlashCommand("hibp", "check if an email appears in data breaches (no key)")]
    public async Task Hibp([Summary("email")] string email)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription("🔍 Checking HaveIBeenPwned...").Build());
        var client = _httpFactory.CreateClient();
        var url = $"https://haveibeenpwned.com/api/v3/breachedaccount/{Uri.EscapeDataString(email)}";
        client.DefaultRequestHeaders.Add("hibp-api-key", "");
        try
        {
            var resp = await client.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"✅ No breaches found for {email}").Build());
                return;
            }
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var breaches = JArray.Parse(json);
            var list = string.Join("\n", breaches.Select(b => 
            {
                var name = b["Name"]?.Value<string>() ?? "Unknown";
                var date = b["BreachDate"]?.Value<string>() ?? "Unknown";
                return $"- {name} ({date})";
            }));
            var summary = $"📧 {email} found in {breaches.Count} breaches:\n{list}";
            await ShowResult(loading.Id, "HaveIBeenPwned", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 2. AbuseIPDB (requires key) ==========
    [SlashCommand("abuseip", "report or check IP reputation (requires AbuseIPDB key)")]
    public async Task AbuseIp([Summary("ip")] string ip)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Checking AbuseIPDB for {ip}...").Build());
        var apiKey = await _apiKeyService.GetDefaultApiKeyAsync(Context.User.Id.ToString(), "abuseipdb");
        if (string.IsNullOrEmpty(apiKey))
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("❌ No AbuseIPDB key set. Use `/setapikey abuseipdb <key>`").Build());
            return;
        }
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Key", apiKey);
        var url = $"https://api.abuseipdb.com/api/v2/check?ipAddress={ip}";
        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json)["data"];
            if (data == null) throw new Exception("Invalid response");
            var score = data["abuseConfidenceScore"]?.Value<int>() ?? 0;
            var totalReports = data["totalReports"]?.Value<int>() ?? 0;
            var country = data["countryName"]?.Value<string>() ?? "Unknown";
            var summary = $"🌐 IP: {ip}\n⚠️ Abuse score: {score}%\n📊 Total reports: {totalReports}\n📍 Country: {country}";
            await ShowResult(loading.Id, "AbuseIPDB", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 3. Hunter.io (requires key) ==========
    [SlashCommand("hunter", "domain email search (requires Hunter key)")]
    public async Task Hunter([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Searching Hunter for {domain}...").Build());
        var apiKey = await _apiKeyService.GetDefaultApiKeyAsync(Context.User.Id.ToString(), "hunter");
        if (string.IsNullOrEmpty(apiKey))
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("❌ No Hunter key set. Use `/setapikey hunter <key>`").Build());
            return;
        }
        var client = _httpFactory.CreateClient();
        var url = $"https://api.hunter.io/v2/domain-search?domain={domain}&api_key={apiKey}";
        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json)["data"];
            var emails = data?["emails"] as JArray;
            if (emails == null || emails.Count == 0)
            {
                await ShowResult(loading.Id, "Hunter.io", $"No emails found for {domain}.", json);
                return;
            }
            var list = string.Join("\n", emails.Take(10).Select(e => $"- {e["value"]} ({e["type"]})"));
            var summary = $"📧 Found {emails.Count} emails for {domain}:\n{list}";
            await ShowResult(loading.Id, "Hunter.io", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 4. PeopleDataLabs (requires key) ==========
    [SlashCommand("pdl", "enrich person by email (requires PeopleDataLabs key)")]
    public async Task Pdl([Summary("email")] string email)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Enriching {email}...").Build());
        var apiKey = await _apiKeyService.GetDefaultApiKeyAsync(Context.User.Id.ToString(), "peopledatalabs");
        if (string.IsNullOrEmpty(apiKey))
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("❌ No PeopleDataLabs key set. Use `/setapikey peopledatalabs <key>`").Build());
            return;
        }
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        var url = "https://api.peopledatalabs.com/v2/enrich/person";
        var content = new StringContent($"{{\"email\":\"{email}\"}}", System.Text.Encoding.UTF8, "application/json");
        try
        {
            var resp = await client.PostAsync(url, content);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json)["data"];
            if (data == null) throw new Exception("No data found");
            var name = data["full_name"]?.Value<string>() ?? "Unknown";
            var location = data["location"]?["name"]?.Value<string>() ?? "Unknown";
            var job = data["job_title"]?.Value<string>() ?? "Unknown";
            var company = data["company"]?["name"]?.Value<string>() ?? "Unknown";
            var summary = $"👤 {email}\n📛 Name: {name}\n📍 Location: {location}\n💼 Job: {job}\n🏢 Company: {company}";
            await ShowResult(loading.Id, "PeopleDataLabs", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 5. ipgeolocation.io (requires key) ==========
    [SlashCommand("ipgeo", "geolocation by IP (requires ipgeolocation.io key)")]
    public async Task IpGeo([Summary("ip")] string ip)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Geolocating {ip}...").Build());
        var apiKey = await _apiKeyService.GetDefaultApiKeyAsync(Context.User.Id.ToString(), "ipgeolocation");
        if (string.IsNullOrEmpty(apiKey))
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("❌ No ipgeolocation key set. Use `/setapikey ipgeolocation <key>`").Build());
            return;
        }
        var client = _httpFactory.CreateClient();
        var url = $"https://api.ipgeolocation.io/ipgeo?ip={ip}&apiKey={apiKey}";
        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            var city = data["city"]?.Value<string>() ?? "Unknown";
            var country = data["country_name"]?.Value<string>() ?? "Unknown";
            var isp = data["isp"]?.Value<string>() ?? "Unknown";
            var lat = data["latitude"]?.Value<double>() ?? 0;
            var lon = data["longitude"]?.Value<double>() ?? 0;
            var summary = $"🌐 IP: {ip}\n📍 {city}, {country}\n📡 ISP: {isp}\n🌍 {lat},{lon}";
            await ShowResult(loading.Id, "ipgeolocation.io", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 6. ip-api.com (no key) ==========
    [SlashCommand("ipapi", "geolocation by IP (free, no key)")]
    public async Task IpApiFree([Summary("ip")] string ip)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Checking ip-api.com for {ip}...").Build());
        var client = _httpFactory.CreateClient();
        var url = $"http://ip-api.com/json/{ip}";
        try
        {
            var resp = await client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            if (data["status"]?.Value<string>() == "fail")
            {
                await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"❌ {data["message"]}").Build());
                return;
            }
            var city = data["city"]?.Value<string>() ?? "Unknown";
            var country = data["country"]?.Value<string>() ?? "Unknown";
            var isp = data["isp"]?.Value<string>() ?? "Unknown";
            var lat = data["lat"]?.Value<double>() ?? 0;
            var lon = data["lon"]?.Value<double>() ?? 0;
            var summary = $"🌐 IP: {ip}\n📍 {city}, {country}\n📡 ISP: {isp}\n🌍 {lat},{lon}";
            await ShowResult(loading.Id, "ip-api.com", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 7. OnionEngine (requires key) ==========
    [SlashCommand("onion", "search dark web (requires OnionEngine key)")]
    public async Task Onion([Summary("keyword")] string keyword)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Searching OnionEngine for {keyword}...").Build());
        var apiKey = await _apiKeyService.GetDefaultApiKeyAsync(Context.User.Id.ToString(), "onionengine");
        if (string.IsNullOrEmpty(apiKey))
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription("❌ No OnionEngine key set. Use `/setapikey onionengine <key>`").Build());
            return;
        }
        var client = _httpFactory.CreateClient();
        var url = $"https://onionengine.com/api/search?q={Uri.EscapeDataString(keyword)}&apiKey={apiKey}";
        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            var results = data["results"] as JArray;
            if (results == null || results.Count == 0)
            {
                await ShowResult(loading.Id, "OnionEngine", $"No results for {keyword}.", json);
                return;
            }
            var list = string.Join("\n", results.Take(10).Select(r => $"- {r["title"]} ({r["url"]})"));
            var summary = $"🌐 Found {results.Count} results:\n{list}";
            await ShowResult(loading.Id, "OnionEngine", summary, json);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 8. Sherlock (CLI) ==========
    [SlashCommand("sherlock", "search username across 400+ sites (CLI tool)")]
    public async Task Sherlock([Summary("username")] string username)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running Sherlock on {username} (may take 30s)...").Build());
        try
        {
            var output = await RunCli("sherlock", username, 45);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "Sherlock", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 9. theHarvester (CLI) ==========
    [SlashCommand("harvester", "gather emails/subdomains (CLI tool)")]
    public async Task Harvester([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running theHarvester on {domain} (may take 60s)...").Build());
        try
        {
            var output = await RunCli("theHarvester", $"-d {domain} -b all", 90);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "theHarvester", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 10. SpiderFoot (CLI) ==========
    [SlashCommand("spiderfoot", "automated OSINT scanning (CLI tool)")]
    public async Task SpiderFoot([Summary("target")] string target)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running SpiderFoot on {target} (may take 120s)...").Build());
        try
        {
            var output = await RunCli("sf", $"-s {target} -o json", 150);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "SpiderFoot", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 11. Recon-ng (CLI) ==========
    [SlashCommand("recon", "web reconnaissance (CLI tool)")]
    public async Task Recon([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running Recon-ng on {domain} (may take 60s)...").Build());
        try
        {
            var output = await RunCli("recon-ng", $"-w {domain}", 90);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "Recon-ng", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 12. Subfinder (CLI) ==========
    [SlashCommand("subfinder", "enumerate subdomains (CLI tool)")]
    public async Task Subfinder([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running Subfinder on {domain}...").Build());
        try
        {
            var output = await RunCli("subfinder", $"-d {domain} -silent", 45);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "Subfinder", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 13. AMASS (CLI) ==========
    [SlashCommand("amass", "attack surface mapping (CLI tool)")]
    public async Task Amass([Summary("domain")] string domain)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running AMASS on {domain} (may take 90s)...").Build());
        try
        {
            var output = await RunCli("amass", $"-d {domain} -o /dev/stdout", 120);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "AMASS", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 14. TorBot (CLI) ==========
    [SlashCommand("torbot", "crawl onion sites (CLI tool)")]
    public async Task TorBot([Summary("onion")] string onionUrl)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running TorBot on {onionUrl} (may take 60s)...").Build());
        try
        {
            var output = await RunCli("torbot", $"-u http://{onionUrl}", 90);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "TorBot", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 15. OD Crawler (CLI) ==========
    [SlashCommand("odcrawler", "username disclosure (CLI tool)")]
    public async Task OdCrawler([Summary("username")] string username)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running OD Crawler on {username}...").Build());
        try
        {
            var output = await RunCli("od-crawler", username, 60);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "OD Crawler", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }

    // ========== 16. WhoCord (CLI) ==========
    [SlashCommand("whocord", "comprehensive username/email/Discord OSINT (CLI tool)")]
    public async Task WhoCord(
        [Summary("type", "username, email, or discord")] string type,
        [Summary("target")] string target)
    {
        if (!await EnsureAuthorized()) { await RespondAsync("Redeem master key first.", ephemeral: true); return; }
        await DeferAsync();
        string arg;
        if (type.ToLower() == "username") arg = $"-u {target}";
        else if (type.ToLower() == "email") arg = $"-e {target}";
        else if (type.ToLower() == "discord") arg = $"-d {target}";
        else { await FollowupAsync("Invalid type. Use username, email, or discord.", ephemeral: true); return; }
        var loading = await FollowupAsync(embed: new EmbedBuilder().WithDescription($"🔍 Running WhoCord on {target}...").Build());
        try
        {
            var output = await RunCli("whocord", arg, 60);
            if (output.Length > 4000) output = output[..4000] + "\n... (truncated)";
            await ShowResult(loading.Id, "WhoCord", output);
        }
        catch (Exception ex)
        {
            await loading.ModifyAsync(m => m.Embed = new EmbedBuilder().WithDescription($"Error: {ex.Message}").Build());
        }
    }
}