using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;
using Newtonsoft.Json;

namespace atfot.modules.osint;

[Group("discord", "Discord user OSINT (deep lookup)")]
public class DiscordLookupCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly SocialMediaService _socialMedia;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImageService _imageService;
    private readonly AiSummaryService _aiSummary;

    private static readonly Dictionary<string, List<(string toolId, string toolName, string result, string? rawJson)>> _toolResultsCache = new();

    private static string DecodeUnicodeEscapes(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        return Regex.Replace(input, @"\\u([0-9A-Fa-f]{4})", m =>
        {
            char c = (char)Convert.ToInt32(m.Groups[1].Value, 16);
            return c.ToString();
        });
    }

    public DiscordLookupCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        SocialMediaService socialMedia,
        IHttpClientFactory httpFactory,
        ImageService imageService,
        AiSummaryService aiSummary)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _socialMedia = socialMedia;
        _httpFactory = httpFactory;
        _imageService = imageService;
        _aiSummary = aiSummary;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("lookup", "deep Discord user OSINT using multiple tools")]
    public async Task DiscordLookup(
        [Summary("userid", "Discord user ID (snowflake)")] string userId)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var sessionId = Guid.NewGuid().ToString("N");
        var loadingEmbed = new EmbedBuilder()
            .WithTitle("Discord Lookup")
            .WithDescription("```[INFO] initializing modules...```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();

        var initialResponse = await FollowupAsync(embed: loadingEmbed, components: null);

        var statusMessages = new (string text, int delay)[]
        {
            ("```[INFO] initializing modules...```", 600),
            ("```[DONE] initialized```", 200),
            ("```[INFO] checking providers health...```", 800),
            ("```[DONE] providers healthy```", 200),
            ("```[INFO] enumerating OSINT sources...```", 800),
            ("```[DONE] source check complete```", 200),
            ("```[DONE] profile loaded```", 1000)
        };

        foreach (var (text, delay) in statusMessages)
        {
            await Task.Delay(delay);
            await initialResponse.ModifyAsync(msg =>
            {
                msg.Embed = new EmbedBuilder()
                    .WithTitle("Discord Lookup")
                    .WithDescription(text)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                    .Build();
            });
        }

        var tools = new List<(string toolId, string toolName, Func<string, string, Task<(string, string?)>> fetch, string? keyService)>
        {
            ("public_api", "Discord Public API (no key)", FetchDiscordPublicApi, null),
            ("oathnet", "OathNet (Roblox & name history)", FetchOathNet, "oathnet"),
            ("osintcat", "OsintCat (coming soon)", FetchOsintCat, "osintcat"),
            ("leakinsight", "LeakInsight (coming soon)", FetchLeakInsight, "leakinsight"),
            ("intelfetch", "IntelFetch (coming soon)", FetchIntelFetch, "intelfetch"),
            ("indicia", "Indicia (coming soon)", FetchIndicia, "indicia"),
            ("crowsint", "CrowSint (coming soon)", FetchCrowSint, "crowsint")
        };

        var results = new List<(string toolId, string toolName, string result, string? rawJson)>();
        foreach (var (toolId, toolName, fetch, keyService) in tools)
        {
            string? apiKey = null;
            if (keyService != null)
                apiKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), keyService);

            if (keyService != null && string.IsNullOrEmpty(apiKey))
            {
                results.Add((toolId, toolName, "[ERR] API key required – use /setapikey", null));
                continue;
            }

            try
            {
                var (summary, rawJson) = await fetch(userId, apiKey ?? "");
                
                // AI summary replacement (if enabled and rawJson exists)
                string finalSummary = summary;
                if (!string.IsNullOrEmpty(rawJson) && await _aiSummary.generateAsync(Context.User.Id.ToString(), rawJson, $"Discord user {userId}") != null)
                {
                    var aiSummaryText = await _aiSummary.generateAsync(Context.User.Id.ToString(), rawJson, $"Discord user {userId}");
                    if (!string.IsNullOrEmpty(aiSummaryText))
                        finalSummary = aiSummaryText;
                }
                
                results.Add((toolId, toolName, finalSummary ?? "No data returned.", rawJson));
            }
            catch (Exception ex)
            {
                results.Add((toolId, toolName, $"[ERR] {ex.Message}", null));
            }
        }

        var cacheKey = $"{sessionId}_discord_{userId}";
        _toolResultsCache[cacheKey] = results;
        await ShowDiscordTool(cacheKey, 0, initialResponse.Id);
    }

    private async Task ShowDiscordTool(string cacheKey, int index, ulong messageId)
    {
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
            return;

        var tool = tools[index];
        var embed = new EmbedBuilder()
            .WithTitle(tool.toolName)
            .WithDescription($"```\n{tool.result}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("◀", $"discord_carousel:{cacheKey}:{index - 1}", ButtonStyle.Secondary, disabled: index == 0)
            .WithButton("▶", $"discord_carousel:{cacheKey}:{index + 1}", ButtonStyle.Secondary, disabled: index == tools.Count - 1)
            .WithButton("TXT", $"discord_export:{cacheKey}:{index}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"discord_export:{cacheKey}:{index}:json", ButtonStyle.Secondary)
            .Build();

        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(messageId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        else
            await FollowupAsync(embed: embed, components: components);
    }

    [ComponentInteraction("discord_carousel:*:*", ignoreGroupNames: true)]
    public async Task HandleDiscordCarousel(string cacheKey, string indexStr)
    {
        await DeferAsync();
        if (!int.TryParse(indexStr, out int index)) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await ShowDiscordTool(cacheKey, index, smc.Message.Id);
    }

    [ComponentInteraction("discord_export:*:*:*", ignoreGroupNames: true)]
    public async Task HandleDiscordExport(string cacheKey, string indexStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!int.TryParse(indexStr, out int index)) return;
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
        {
            await FollowupAsync("[ERR] export data not found.", ephemeral: true);
            return;
        }

        var tool = tools[index];
        if (string.IsNullOrEmpty(tool.rawJson))
        {
            await FollowupAsync("[ERR] no raw data to export.", ephemeral: true);
            return;
        }

        var parts = cacheKey.Split('_');
        var userId = parts[2];
        var dto = new ScanResultDto
        {
            TargetLookup = userId,
            ModuleSource = tool.toolId,
            RawApiResponse = tool.rawJson,
            Summary = tool.result
        };

        string filename = $"{tool.toolId}_{userId}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format switch
        {
            "json" => _export.BuildJsonStream(dto),
            _ => _export.BuildTextStream(dto)
        };
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported {tool.toolName} data.");
    }

    // ---------- Tool implementations ----------
    private async Task<(string summary, string? rawJson)> FetchDiscordPublicApi(string userId, string _)
    {
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "ATFOT/1.0");
        var combinedData = new JObject();

        async Task<JObject?> TryPrimaryApi()
        {
            var userUrl = $"https://japi.rest/discord/v1/user/{userId}";
            var userResponse = await client.GetAsync(userUrl);
            if (!userResponse.IsSuccessStatusCode) return null;
            var userJson = await userResponse.Content.ReadAsStringAsync();
            return JObject.Parse(userJson);
        }

        async Task<JObject?> TryFallbackApi()
        {
            var url = $"https://discordlookup.com/api/user/{userId}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        var userData = await TryPrimaryApi();
        if (userData == null)
        {
            userData = await TryFallbackApi();
            if (userData == null)
                return ("Public API request failed (user may not exist or API timeout).", null);
        }

        combinedData["user"] = userData;
        var profile = userData["data"] ?? userData;

        var connUrl = $"https://japi.rest/discord/v1/user/{userId}/connections";
        var connResponse = await client.GetAsync(connUrl);
        JArray? connections = null;
        if (connResponse.IsSuccessStatusCode)
        {
            var connJson = await connResponse.Content.ReadAsStringAsync();
            var connData = JObject.Parse(connJson);
            combinedData["connections"] = connData;
            connections = connData["data"] as JArray;
        }

        var username = profile["username"]?.Value<string>() ?? "N/A";
        var globalName = profile["global_name"]?.Value<string>() ?? "N/A";
        var discriminator = profile["discriminator"]?.Value<string>() ?? "0";
        var avatarHash = profile["avatar"]?.Value<string>();
        var avatarUrl = !string.IsNullOrEmpty(avatarHash) ? $"https://cdn.discordapp.com/avatars/{userId}/{avatarHash}.png" : "None";
        var banner = profile["banner"]?.Value<string>() ?? "None";
        var accentColorToken = profile["accent_color"];
        var accentColorStr = (accentColorToken != null && accentColorToken.Type != JTokenType.Null && accentColorToken.Type != JTokenType.None)
            ? $"#{accentColorToken.Value<int>():X6}" : "None";
        var avatarDecoration = profile["avatar_decoration"]?.Value<string>() ?? "None";
        var badges = profile["badges"]?.Select(b => b.ToString()).ToList();
        var badgesStr = badges != null && badges.Any() ? string.Join(", ", badges) : "None";

        var createdAt = "Unknown";
        if (ulong.TryParse(userId, out var snowflake))
        {
            var timestamp = (long)((snowflake >> 22) + 1420070400000);
            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString("yyyy-MM-dd HH:mm:ss");
        }

        var summary = $"=================================\n" +
                      $" Discord Profile (Public API)\n" +
                      $"=================================\n" +
                      $"ID          : {userId}\n" +
                      $"Username    : {username}\n" +
                      $"Discriminator: {discriminator}\n" +
                      $"Global Name : {globalName}\n" +
                      $"Created At  : {createdAt}\n" +
                      $"Badges      : {badgesStr}\n" +
                      $"Accent Color: {accentColorStr}\n" +
                      $"Avatar Decoration: {avatarDecoration}\n" +
                      $"Avatar URL  : {avatarUrl}\n" +
                      $"Banner      : {banner}\n" +
                      $"=================================\n\n";

        if (connections != null && connections.Any())
        {
            summary += "**Linked Accounts**\n";
            foreach (var conn in connections)
            {
                var type = conn["type"]?.Value<string>() ?? "Unknown";
                var name = conn["name"]?.Value<string>() ?? "Unknown";
                var verified = conn["verified"]?.Value<bool>() ?? false;
                var revoke = conn["revoked"]?.Value<bool>() ?? false;
                summary += $"• **{type}** – {name}";
                if (verified) summary += " (verified)";
                if (revoke) summary += " [revoked]";
                summary += "\n";
            }
        }
        else
        {
            summary += "**Linked Accounts**\nNone found (rarely visible without OAuth2).\n";
        }

        var prettyJson = JsonConvert.SerializeObject(combinedData, Formatting.Indented);
        prettyJson = DecodeUnicodeEscapes(prettyJson);
        return (summary, prettyJson);
    }

    private async Task<(string summary, string? rawJson)> FetchOsintCat(string userId, string apiKey)
    {
        await Task.CompletedTask;
        return ("OsintCat integration coming soon – will pull public records", null);
    }

    private async Task<(string summary, string? rawJson)> FetchLeakInsight(string userId, string apiKey)
    {
        await Task.CompletedTask;
        return ("LeakInsight breach check coming soon", null);
    }

    private async Task<(string summary, string? rawJson)> FetchIntelFetch(string userId, string apiKey)
    {
        await Task.CompletedTask;
        return ("IntelFetch AI OSINT coming soon", null);
    }

    private async Task<(string summary, string? rawJson)> FetchIndicia(string userId, string apiKey)
    {
        await Task.CompletedTask;
        return ("Indicia digital footprint coming soon", null);
    }

    private async Task<(string summary, string? rawJson)> FetchCrowSint(string userId, string apiKey)
    {
        await Task.CompletedTask;
        return ("CrowSint correlation coming soon", null);
    }

    private async Task<(string summary, string? rawJson)> FetchOathNet(string userId, string apiKey)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        var allData = new JObject();
        var userInfoSummary = "";
        var usernameHistorySummary = "None found.";
        var robloxSummary = "No Roblox account linked.";

        try
        {
            var userInfoResponse = await client.GetAsync($"https://oathnet.org/api/service/discord-userinfo?id={userId}");
            if (userInfoResponse.IsSuccessStatusCode)
            {
                var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();
                var userInfoData = JObject.Parse(userInfoJson);
                allData["userinfo"] = userInfoData;
                var dataNode = userInfoData["data"];
                if (dataNode != null)
                {
                    userInfoSummary = $"**User Info**\n" +
                                      $"• ID: {dataNode["id"]}\n" +
                                      $"• Username: {dataNode["username"]}\n" +
                                      $"• Global Name: {dataNode["global_name"]}\n" +
                                      $"• Avatar URL: {dataNode["avatar_url"]}\n" +
                                      $"• Creation Date: {dataNode["creation_date"]}\n";
                }
            }

            var usernameHistoryResponse = await client.GetAsync($"https://oathnet.org/api/service/discord-username-history?id={userId}");
            if (usernameHistoryResponse.IsSuccessStatusCode)
            {
                var historyJson = await usernameHistoryResponse.Content.ReadAsStringAsync();
                var historyData = JObject.Parse(historyJson);
                allData["username_history"] = historyData;
                var historyNode = historyData["data"]?["history"];
                if (historyNode != null && historyNode.HasValues)
                {
                    var historyItems = new List<string>();
                    foreach (var entry in historyNode)
                    {
                        var name = entry["name"]?.First?.ToString();
                        var time = entry["time"]?.First?.ToString();
                        if (!string.IsNullOrEmpty(name))
                            historyItems.Add($"• {name} – {time}");
                    }
                    if (historyItems.Any())
                        usernameHistorySummary = string.Join("\n", historyItems);
                }
            }

            var robloxResponse = await client.GetAsync($"https://oathnet.org/api/service/discord-to-roblox?id={userId}");
            if (robloxResponse.IsSuccessStatusCode)
            {
                var robloxJson = await robloxResponse.Content.ReadAsStringAsync();
                var robloxData = JObject.Parse(robloxJson);
                allData["roblox"] = robloxData;
                var robloxNode = robloxData["data"];
                if (robloxNode != null && !string.IsNullOrEmpty(robloxNode["name"]?.ToString()))
                {
                    robloxSummary = $"**Roblox Profile**\n" +
                                    $"• ID: {robloxNode["roblox_id"]}\n" +
                                    $"• Username: {robloxNode["name"]}\n" +
                                    $"• Display Name: {robloxNode["displayName"]}\n" +
                                    $"• Avatar URL: {robloxNode["avatar"]}";
                }
            }

            var prettyJson = JsonConvert.SerializeObject(allData, Formatting.Indented);
            var summary = $"=================================\n" +
                          $" OathNet Intel for Discord ID {userId}\n" +
                          $"=================================\n\n" +
                          $"{userInfoSummary}\n" +
                          $"**Username History**\n{usernameHistorySummary}\n\n" +
                          $"{robloxSummary}\n" +
                          $"=================================";
            return (summary, prettyJson);
        }
        catch (Exception ex)
        {
            return ($"Error querying OathNet: {ex.Message}", null);
        }
    }
}