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
    private readonly TorProxyService _tor;

    public InfrastructureCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        IHttpClientFactory httpFactory,
        TorProxyService tor)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
        _tor = tor;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("investigate", "audit routing topologies, reputation, and onion services")]
    public async Task Investigate(
        [Summary("node", "IP address, domain name, or .onion address")] string node,
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
            TargetLookup = node,
            ModuleSource = "infrastructure_telemetry"
        };

        string encoded = Uri.EscapeDataString(node);
        bool isOnion = node.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

        var links = new List<string>
        {
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
            dto.ExtractedData["tor_warning"] = "Onion services may require Tor Browser to access directly.";
        }

        dto.DeepLinks = links;

        var shodanKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), "shodan");
        if (!string.IsNullOrEmpty(shodanKey))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var response = await client.GetAsync($"https://api.shodan.io/shodan/host/{node}?key={shodanKey}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    dto.RawApiResponse = json;
                    var obj = JObject.Parse(json);
                    dto.ExtractedData["shodan_isp"] = obj["isp"]?.ToString() ?? "unknown";
                    dto.ExtractedData["shodan_country"] = obj["country_name"]?.ToString() ?? "unknown";
                    dto.ExtractedData["shodan_ports"] = string.Join(",", obj["ports"] ?? new JArray());
                    dto.Summary = $"Shodan: ISP {dto.ExtractedData["shodan_isp"]}, located in {dto.ExtractedData["shodan_country"]}. Open ports: {dto.ExtractedData["shodan_ports"]}";
                }
                else
                {
                    dto.ExtractedData["shodan_error"] = $"HTTP {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                dto.ExtractedData["shodan_exception"] = ex.Message;
            }
        }
        else
        {
            dto.ExtractedData["shodan"] = "No API key. Use /setapikey shodan <key> to enable live data.";
        }

        var description = $"**Target:** `{node}`\n\n**Investigation Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("infrastructure diagnostics", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "infra.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "infra.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("dns", "perform DNS history and subdomain enumeration")]
    public async Task DnsInvestigation(
        [Summary("domain", "domain name")] string domain,
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
            TargetLookup = domain,
            ModuleSource = "dns_telemetry"
        };

        string encoded = Uri.EscapeDataString(domain);
        var links = new List<string>
        {
            $"[DNSDumpster](https://dnsdumpster.com/?target={encoded})",
            $"[SecurityTrails DNS History](https://securitytrails.com/domain/{encoded}/dns)",
            $"[ViewDNS.info](https://viewdns.info/dnsrecord/?domain={encoded})",
            $"[Subdomain Finder (CRT.sh)](https://crt.sh/?q=%25.{encoded})"
        };
        dto.DeepLinks = links;

        var secTrailsKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), "securitytrails");
        if (!string.IsNullOrEmpty(secTrailsKey))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Add("APIKEY", secTrailsKey);
                var response = await client.GetAsync($"https://api.securitytrails.com/v1/domain/{domain}/subdomains");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    dto.RawApiResponse = json;
                    var obj = JObject.Parse(json);
                    var subdomains = obj["subdomains"] as JArray;
                    if (subdomains != null)
                    {
                        var subList = string.Join(", ", subdomains.Take(20));
                        dto.ExtractedData["subdomains"] = subList;
                        dto.Summary = $"Found {subdomains.Count} subdomains (first 20: {subList})";
                    }
                }
                else
                {
                    dto.ExtractedData["securitytrails_error"] = $"HTTP {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                dto.ExtractedData["securitytrails_exception"] = ex.Message;
            }
        }
        else
        {
            dto.ExtractedData["securitytrails"] = "No API key. Use /setapikey securitytrails <key> for subdomain enumeration.";
        }

        var description = $"**Domain:** `{domain}`\n\n**DNS & Subdomain Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("dns analysis", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "dns.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "dns.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}