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

[Group("social", "social media footprint analysis")]
public class SocialCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly SocialMediaService _socialMedia;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImageService _imageService;

    private static readonly Dictionary<string, string> _sessionUsername = new();
    private static readonly Dictionary<ulong, string> _userUsername = new();
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

    public SocialCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        SocialMediaService socialMedia,
        IHttpClientFactory httpFactory,
        ImageService imageService)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _socialMedia = socialMedia;
        _httpFactory = httpFactory;
        _imageService = imageService;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());

    [SlashCommand("username", "search for a username across major social platforms")]
    public async Task SocialUsername([Summary("username", "target username (without @)")] string username)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("Redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"Wait a few seconds...", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        _userUsername[Context.User.Id] = username;
        var sessionId = Guid.NewGuid().ToString("N");
        _sessionUsername[sessionId] = username;

        var menu = new SelectMenuBuilder()
            .WithPlaceholder("Select a platform...")
            .WithCustomId($"social_platform:{sessionId}")
            .AddOption("Instagram", "instagram")
            .AddOption("Reddit", "reddit")
            .AddOption("GitHub", "github")
            .AddOption("Twitter", "twitter")
            .AddOption("TikTok", "tiktok")
            .AddOption("LinkedIn", "linkedin")
            .AddOption("Telegram", "telegram")
            .AddOption("Pinterest", "pinterest");

        var component = new ComponentBuilder().WithSelectMenu(menu).Build();

        var loadingEmbed = new EmbedBuilder()
            .WithTitle("Profile Lookup")
            .WithDescription("```diff\nInitializing modules...```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
            .Build();

        var initialResponse = await FollowupAsync(embed: loadingEmbed, components: null);

        var statusMessages = new (string text, int delay)[]
        {
            ("```diff\n Initializing modules...```", 800),
            ("```diff\n+ Initializing modules... [DONE]```",200),
            ("```diff\n Checking on providers health...```", 1200),
            ("```diff\n+ Checking on providers health... [PERFECT]!\n```", 200),
            ("```diff\n Processing request...```", 400),
            ("```diff\n+ Processing request... [DONE]\n```", 200),
            ("```diff\nAll done twin, Please wait a moment...\n```", 1000)
        };

        foreach (var (text, delay) in statusMessages)
        {
            await Task.Delay(delay);
            await initialResponse.ModifyAsync(msg =>
            {
                msg.Embed = new EmbedBuilder()
                    .WithTitle("Profile Lookup")
                    .WithDescription(text)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
                    .Build();
            });
        }

        try
        {
            using var imageStream = await _imageService.profilelookupImgAsync(username);
            await initialResponse.ModifyAsync(msg =>
            {
                msg.Embed = new EmbedBuilder()
                    .WithTitle("")
                    .WithImageUrl("attachment://profile-lookup.jpg")
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
                    .Build();
                msg.Attachments = new List<FileAttachment> { new FileAttachment(imageStream, "profile-lookup.jpg") };
                msg.Components = component;
            });
        }
        catch (Exception)
        {
            var fallbackEmbed = new EmbedBuilder()
                .WithTitle("Profile Lookup")
                .WithDescription($"┌────────────────────────────────────┐\n" +
                                 $"│ Enter the username you want to     │\n" +
                                 $"│ investigate across social media    │\n" +
                                 $"│ platforms. Select a platform from  │\n" +
                                 $"│ the dropdown to gather public      │\n" +
                                 $"│ data, social footprints, and       │\n" +
                                 $"│ associated accounts across the     │\n" +
                                 $"│ selected service.                  │\n" +
                                 $"│                                    │\n" +
                                 $"│ Target: {username}                 │\n" +
                                 $"└────────────────────────────────────┘")
                .WithColor(new Color(0x55, 0x55, 0x55))
                .WithCurrentTimestamp()
                .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
                .Build();
            await initialResponse.ModifyAsync(msg =>
            {
                msg.Embed = fallbackEmbed;
                msg.Components = component;
            });
        }
    }

    [ComponentInteraction("social_platform:*", ignoreGroupNames: true)]
    public async Task HandlePlatformSelection(string sessionId)
    {
        await DeferAsync();
        try
        {
            var smc = Context.Interaction as SocketMessageComponent;
            if (smc == null) return;
            var platform = smc.Data.Values.FirstOrDefault();
            if (string.IsNullOrEmpty(platform)) return;

            var originalMsg = await (Context.Channel as ISocketMessageChannel).GetMessageAsync(smc.Message.Id) as IUserMessage;
            if (originalMsg != null)
            {
                await originalMsg.ModifyAsync(m =>
                {
                    m.Components = new ComponentBuilder().Build();
                    m.Attachments = null;
                });
            }

            if (!_sessionUsername.TryGetValue(sessionId, out var username))
            {
                await FollowupAsync("Session expired.", ephemeral: true);
                return;
            }

            if (platform == "instagram")
            {
                await HandleInstagramTools(sessionId, username, smc.Message.Id);
            }
            else
            {
                await HandleOtherPlatform(platform, sessionId, username, smc.Message.Id);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"Error: {ex.Message}", ephemeral: true);
        }
    }

    private async Task HandleInstagramTools(string sessionId, string username, ulong originalMessageId)
    {
        var userId = Context.User.Id.ToString();

        var channel = Context.Channel as ISocketMessageChannel;
        var originalMsg = await channel.GetMessageAsync(originalMessageId) as IUserMessage;
        if (originalMsg != null)
        {
            var loadingEmbed = new EmbedBuilder()
                .WithTitle("Instagram Scrapers")
                .WithDescription("```\nFetching data... Please wait a few seconds.\n```")
                .WithColor(new Color(0x55, 0x55, 0x55))
                .WithCurrentTimestamp()
                .WithFooter(f => f.Text = "ATFOT osint // made by thevirgindev")
                .Build();
            await originalMsg.ModifyAsync(m =>
            {
                m.Embed = loadingEmbed;
                m.Attachments = null;
            });
        }

        var tools = new List<(string toolId, string toolName, Func<string, string, Task<(string summary, string? rawJson)>> fetch, string apiKeyServiceName)>
        {
            ("socialapi", "instagram socialapi scraper:", FetchSocialApisInstagram, "socialapi"),
            ("serpapi", "instagram serpapi scraper:", FetchSerpApiInstagram, "serpapi")
        };

        var results = new List<(string toolId, string toolName, string result, string? rawJson)>();

        foreach (var (toolId, toolName, fetch, keyServiceName) in tools)
        {
            var apiKey = await _apiKeyService.GetApiKeyAsync(userId, keyServiceName);
            if (string.IsNullOrEmpty(apiKey))
            {
                results.Add((toolId, toolName, "```diff - add an api key first twin```", null));
                continue;
            }

            try
            {
                var (summary, rawJson) = await fetch(username, userId);
                if (summary != null && summary.Length > 4000)
                    summary = summary.Substring(0, 4000) + "...\n(truncated)";
                results.Add((toolId, toolName, summary ?? "No data returned.", rawJson));
            }
            catch (Exception ex)
            {
                results.Add((toolId, toolName, $"Error: {ex.Message}", null));
            }
        }

        var cacheKey = $"{sessionId}_{username}";
        _toolResultsCache[cacheKey] = results;

        await ShowInstagramTool(cacheKey, 0, originalMessageId);
    }

    private async Task ShowInstagramTool(string cacheKey, int index, ulong messageId)
    {
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
            return;

        var tool = tools[index];
        string cleanTitle = tool.toolName.Replace("instagram ", "").Replace(" scraper:", "").Trim();
        var embed = new EmbedBuilder()
            .WithTitle($"Instagram: {cleanTitle}")
            .WithDescription($"```\n{tool.result}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
            .Build();

        var components = new ComponentBuilder()
            .WithButton("◀", $"insta_carousel:{cacheKey}:{index - 1}", ButtonStyle.Secondary, disabled: index == 0)
            .WithButton("▶", $"insta_carousel:{cacheKey}:{index + 1}", ButtonStyle.Secondary, disabled: index == tools.Count - 1)
            .WithButton("TXT", $"insta_export:{cacheKey}:{index}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"insta_export:{cacheKey}:{index}:json", ButtonStyle.Secondary)
            .WithButton("Back to Menu", $"back_to_menu:{cacheKey}", ButtonStyle.Secondary)
            .Build();

        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(messageId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        else
            await FollowupAsync(embed: embed, components: components);
    }

    [ComponentInteraction("insta_carousel:*:*", ignoreGroupNames: true)]
    public async Task HandleInstaCarousel(string cacheKey, string indexStr)
    {
        await DeferAsync();
        if (!int.TryParse(indexStr, out int index)) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await ShowInstagramTool(cacheKey, index, smc.Message.Id);
    }

    [ComponentInteraction("insta_export:*:*:*", ignoreGroupNames: true)]
    public async Task HandleInstaExport(string cacheKey, string indexStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!int.TryParse(indexStr, out int index)) return;
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
        {
            await FollowupAsync("Export data not found.", ephemeral: true);
            return;
        }

        var tool = tools[index];
        if (string.IsNullOrEmpty(tool.rawJson))
        {
            await FollowupAsync("```diff - No raw data to export.```", ephemeral: true);
            return;
        }

        var username = cacheKey.Split('_')[1];
        var dto = new ScanResultDto
        {
            TargetLookup = username,
            ModuleSource = tool.toolId,
            RawApiResponse = tool.rawJson,
            Summary = tool.result
        };

        string filename = $"{tool.toolId}_{username}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format switch
        {
            "json" => _export.BuildJsonStream(dto),
            _ => _export.BuildTextStream(dto)
        };
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported {tool.toolName} data.");
    }

    private async Task HandleOtherPlatform(string platform, string sessionId, string username, ulong originalMessageId)
    {
        var userId = Context.User.Id.ToString();
        var toolList = GetToolsForPlatform(platform);
        if (toolList == null || toolList.Count == 0)
        {
            await FollowupAsync($"```diff - No tools available for {platform}.```", ephemeral: true);
            return;
        }

        var channel = Context.Channel as ISocketMessageChannel;
        if (channel == null) return;
        var originalMsg = await channel.GetMessageAsync(originalMessageId) as IUserMessage;
        if (originalMsg == null) return;

        var statusMessages = new (string text, int delay)[]
        {
            ("```diff\n Checking on providers health...```", 1000),
            ("```diff\n+ Checking on providers health... [PERFECT]!\n```", 200),
            ("```diff\n Processing request...```", 400),
            ("```diff\n+ Processing request... [DONE]\n```", 200),
            ("```diff\nAll done twin, Please wait a moment...\n```", 700)
        };

        foreach (var (text, delay) in statusMessages)
        {
            await Task.Delay(delay);
            await originalMsg.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithTitle($"{platform} Scraper")
                    .WithDescription(text)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = "ATFOT osint // made by thevirgindev")
                    .Build();
                m.Attachments = null;
            });
        }

        var results = new List<(string toolId, string toolName, string result, string? rawJson)>();
        foreach (var tool in toolList)
        {
            if (tool.Fetch == null) continue;
            try
            {
                var (summary, rawJson) = await tool.Fetch(username, userId);
                results.Add((tool.Id!, tool.Name!, summary ?? "No data returned.", rawJson));
            }
            catch (Exception ex)
            {
                results.Add((tool.Id!, tool.Name!, $"Error: {ex.Message}", null));
            }
        }

        if (results.Count == 0)
        {
            await FollowupAsync("```diff - No data could be fetched.```", ephemeral: true);
            return;
        }

        var cacheKey = $"{sessionId}_{platform}_{username}";
        _toolResultsCache[cacheKey] = results;
        await ShowPlatformTool(cacheKey, 0, originalMessageId);
    }

    private async Task ShowPlatformTool(string cacheKey, int index, ulong messageId)
    {
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
            return;

        var tool = tools[index];
        var embed = new EmbedBuilder()
            .WithTitle(tool.toolName)
            .WithDescription($"```\n{tool.result}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
            .Build();

        var components = new ComponentBuilder()
            .WithButton("◀", $"platform_carousel:{cacheKey}:{index - 1}", ButtonStyle.Secondary, disabled: index == 0)
            .WithButton("▶", $"platform_carousel:{cacheKey}:{index + 1}", ButtonStyle.Secondary, disabled: index == tools.Count - 1)
            .WithButton("TXT", $"platform_export:{cacheKey}:{index}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"platform_export:{cacheKey}:{index}:json", ButtonStyle.Secondary)
            .WithButton("Back to Menu", $"back_to_menu:{cacheKey}", ButtonStyle.Secondary)
            .Build();

        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(messageId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        else
            await FollowupAsync(embed: embed, components: components);
    }

    [ComponentInteraction("platform_carousel:*:*", ignoreGroupNames: true)]
    public async Task HandlePlatformCarousel(string cacheKey, string indexStr)
    {
        await DeferAsync();
        if (!int.TryParse(indexStr, out int index)) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await ShowPlatformTool(cacheKey, index, smc.Message.Id);
    }

    [ComponentInteraction("back_to_menu:*", ignoreGroupNames: true)]
    public async Task HandleBackToMenu(string cacheKey)
    {
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;

        var parts = cacheKey.Split('_');
        string sessionId, username;
        if (parts.Length == 2)
        {
            sessionId = parts[0];
            username = parts[1];
        }
        else if (parts.Length >= 3)
        {
            sessionId = parts[0];
            username = parts[2];
        }
        else
        {
            await smc.RespondAsync("Invalid session data.", ephemeral: true);
            return;
        }

        if (!_sessionUsername.ContainsKey(sessionId))
        {
            await smc.RespondAsync("Session expired. Please use /social username again.", ephemeral: true);
            return;
        }

        var menu = new SelectMenuBuilder()
            .WithPlaceholder("Select a platform...")
            .WithCustomId($"social_platform:{sessionId}")
            .AddOption("Instagram", "instagram")
            .AddOption("Reddit", "reddit")
            .AddOption("GitHub", "github")
            .AddOption("Twitter", "twitter")
            .AddOption("TikTok", "tiktok")
            .AddOption("LinkedIn", "linkedin")
            .AddOption("Telegram", "telegram")
            .AddOption("Pinterest", "pinterest");

        var component = new ComponentBuilder().WithSelectMenu(menu).Build();

        try
        {
            using var imageStream = await _imageService.profilelookupImgAsync(username);
            var embed = new EmbedBuilder()
                .WithTitle("")
                .WithImageUrl("attachment://profile-lookup.jpg")
                .WithColor(new Color(0x55, 0x55, 0x55))
                .WithCurrentTimestamp()
                .WithFooter(f => f.Text = "the most powerful osint bot || made by @thevirgindev")
                .Build();

            await smc.UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = component;
                msg.Attachments = new List<FileAttachment> { new FileAttachment(imageStream, "profile-lookup.jpg") };
            });
        }
        catch (Exception ex)
        {
            await smc.RespondAsync("Failed to regenerate profile on the target. Please try again.", ephemeral: true);
        }
    }

    [ComponentInteraction("platform_export:*:*:*", ignoreGroupNames: true)]
    public async Task HandlePlatformExport(string cacheKey, string indexStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!int.TryParse(indexStr, out int index)) return;
        if (!_toolResultsCache.TryGetValue(cacheKey, out var tools) || index < 0 || index >= tools.Count)
        {
            await FollowupAsync("Export data not found.", ephemeral: true);
            return;
        }

        var tool = tools[index];
        if (string.IsNullOrEmpty(tool.rawJson))
        {
            await FollowupAsync("No raw data to export.", ephemeral: true);
            return;
        }

        var parts = cacheKey.Split('_');
        var username = parts[2];
        var dto = new ScanResultDto
        {
            TargetLookup = username,
            ModuleSource = tool.toolId,
            RawApiResponse = tool.rawJson,
            Summary = tool.result
        };

        string filename = $"{tool.toolId}_{username}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format switch
        {
            "json" => _export.BuildJsonStream(dto),
            _ => _export.BuildTextStream(dto)
        };
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported {tool.toolName} data.");
    }

    private List<ToolInfo>? GetToolsForPlatform(string platform)
    {
        return platform switch
        {
            "reddit" => new() { new ToolInfo { Id = "reddit_apify", Name = "reddit apify scraper", Fetch = FetchRedditAuthor } },
            "github" => new() { new ToolInfo { Id = "github_api", Name = "github public api", Fetch = FetchGitHubApi } },
            "twitter" => new() { new ToolInfo { Id = "twitter_api", Name = "twitter api v2", Fetch = FetchTwitterApi } },
            "tiktok" => new() { new ToolInfo { Id = "tiktok_api", Name = "tiktok api", Fetch = FetchTikTokApi } },
            "linkedin" => new() { new ToolInfo { Id = "linkedin_api", Name = "linkedin api", Fetch = FetchLinkedInApi } },
            "telegram" => new() { new ToolInfo { Id = "telegram_api", Name = "telegram bot api", Fetch = FetchTelegramApi } },
            "pinterest" => new() { new ToolInfo { Id = "pinterest_api", Name = "pinterest api", Fetch = FetchPinterestApi } },
            _ => null
        };
    }

    [ComponentInteraction("export:*:*:*:*", ignoreGroupNames: true)]
    public async Task HandleExport(string sessionId, string platform, string toolId, string format)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var smc = Context.Interaction as SocketMessageComponent;
            if (smc == null) return;
            if (!_sessionUsername.TryGetValue(sessionId, out var username))
            {
                await FollowupAsync("Session expired.", ephemeral: true);
                return;
            }

            var userId = Context.User.Id.ToString();
            var toolList = GetToolsForPlatform(platform);
            var tool = toolList?.FirstOrDefault(t => t.Id == toolId);
            if (tool == null || tool.Fetch == null)
            {
                await FollowupAsync("Tool not found.", ephemeral: true);
                return;
            }

            var result = await tool.Fetch(username, userId);
            var summary = result.Item1;
            var rawJson = result.Item2;
            var dto = new ScanResultDto
            {
                TargetLookup = username,
                ModuleSource = $"{platform}_{toolId}",
                RawApiResponse = rawJson ?? "",
                Summary = summary ?? "No data"
            };
            string filename = $"{platform}_{toolId}_{username}_{DateTime.Now:yyyyMMddHHmmss}";
            using var stream = format switch
            {
                "json" => _export.BuildJsonStream(dto),
                _ => _export.BuildTextStream(dto)
            };
            await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported {platform} data.");
        }
        catch (Exception ex)
        {
            await FollowupAsync($"Export error: {ex.Message}", ephemeral: true);
        }
    }

    // ---------- Tool implementations ----------

    private async Task<(string summary, string? rawJson)> FetchSocialApisInstagram(string username, string discordUserId)
    {
        var token = await _apiKeyService.GetApiKeyAsync(discordUserId, "socialapi");
        if (string.IsNullOrEmpty(token))
            return ("add an api key first", null);
        var data = await _socialMedia.GetSocialApisInstagramUserAsync(username, token);
        if (data == null) return ("No data returned from SocialApis.io.", null);
        if (data["error"] != null) return ($"SocialApis error: {data["error"]}", null);

        var prettyJson = JsonConvert.SerializeObject(data, Formatting.Indented);
        
        var fullN = data["data"]?["full_name"]?.Value<string>() ?? data["full_name"]?.Value<string>() ?? "N/A";
        var accId = data["data"]?["id"]?.Value<string>() ?? data["id"]?.Value<string>() ?? "N/A";
        var followers = data["data"]?["followers_count"]?.Value<int>() ?? data["followers_count"]?.Value<int>() ?? 0;
        var following = data["data"]?["following_count"]?.Value<int>() ?? data["following_count"]?.Value<int>() ?? 0;
        var posts = data["data"]?["media_count"]?.Value<int>() ?? data["media_count"]?.Value<int>() ?? 0;
        var isPriv = data["data"]?["is_private"]?.Value<bool>() ?? data["is_private"]?.Value<bool>() ?? false;
        var isVerif = data["data"]?["is_verified"]?.Value<bool>() ?? data["is_verified"]?.Value<bool>() ?? false;
        var accT = data["data"]?["is_business_account"]?.Value<bool>() ?? data["is_business_account"]?.Value<bool>() ?? false;
        var extUrl = data["data"]?["external_url"]?.Value<string>() ?? data["external_url"]?.Value<string>() ?? "";
        if (string.IsNullOrEmpty(extUrl)) extUrl = "None";
        
        var summary = $"=================================\n" +
                      $" Profile Summary ==> {username}\n" +
                      $"=================================\n" +
                      $"Full Name      : {fullN}\n" +
                      $"Account ID     : {accId}\n" +
                      $"Followers      : {followers}\n" +
                      $"Following      : {following}\n" +
                      $"Posts          : {posts}\n" +
                      $"Private        : {(isPriv ? "Yes" : "No")}\n" +
                      $"Verified       : {(isVerif ? "Yes" : "No")}\n" +
                      $"Account Type   : {(accT ? "Business" : "Personal")}\n" +
                      $"External URL   : {extUrl}\n" +
                      $"=================================";
        return (summary, prettyJson);
    }

    private async Task<(string summary, string? rawJson)> FetchSerpApiInstagram(string username, string discordUserId)
    {
        var token = await _apiKeyService.GetApiKeyAsync(discordUserId, "serpapi");
        if (string.IsNullOrEmpty(token))
            return ("add an api key first", null);
        var data = await _socialMedia.GetSerpApiInstagramUserAsync(username, token);
        if (data == null) return ("No data returned from SerpApi.", null);
        if (data["error"] != null) return ($"SerpApi error: {data["error"]}", null);

        var prettyJson = JsonConvert.SerializeObject(data, Formatting.Indented);
        
        var fullName = data["full_name"]?.Value<string>() ?? "N/A";
        var accountId = data["id"]?.Value<string>() ?? "N/A";
        var followers = data["followers"]?.Value<int>() ?? 0;
        var following = data["following"]?.Value<int>() ?? 0;
        var posts = data["posts_count"]?.Value<int>() ?? 0;
        var isPrivate = data["is_private"]?.Value<bool>() ?? false;
        var isVerified = data["is_verified"]?.Value<bool>() ?? false;
        var businessAccount = data["is_business_account"]?.Value<bool>() ?? false;
        var externalUrl = data["external_url"]?.Value<string>() ?? "";
        if (string.IsNullOrEmpty(externalUrl)) externalUrl = "None";
        var created = data["created_at"]?.Value<string>() ?? "N/A";
        var createdDate = DateTime.TryParse(created, out var dt) ? dt.ToString("yyyy-MM-dd") : created;
        var createdDateRelative = DateTime.TryParse(created, out var dt2) ? $"{(DateTime.UtcNow - dt2).TotalDays:F0} days ago" : created;
        var profilePic = data["profile_pic_url"]?.Value<string>() ?? "N/A";
        
        var summary = $"=================================\n" +
                      $" Profile Summary ==> {username}\n" +
                      $"=================================\n" +
                      $"Full Name      : {fullName}\n" +
                      $"Account ID     : {accountId}\n" +
                      $"Followers      : {followers}\n" +
                      $"Following      : {following}\n" +
                      $"Posts          : {posts}\n" +
                      $"Private        : {(isPrivate ? "Yes" : "No")}\n" +
                      $"Verified       : {(isVerified ? "Yes" : "No")}\n" +
                      $"Account Type   : {(businessAccount ? "Business" : "Personal")}\n" +
                      $"External URL   : {externalUrl}\n" +
                      $"Created At     : {createdDate}\n" +
                      $"Created Rel.   : {createdDateRelative}\n" +
                      $"Profile Pic    : {profilePic}\n" +
                      $"=================================";
        return (summary, prettyJson);
    }

    private async Task<(string summary, string? rawJson)> FetchTwitterApi(string username, string discordUserId)
    {
        var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "twitter");
        if (string.IsNullOrEmpty(key)) return ("No Twitter API key set. Use /admin setkey twitter <token>", null);
        var data = await _socialMedia.GetTwitterUserAsync(username, discordUserId);
        if (data == null) return ("No data from Twitter API.", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (json, json);
    }

    private async Task<(string summary, string? rawJson)> FetchTikTokApi(string username, string discordUserId)
    {
        var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "tiktok");
        if (string.IsNullOrEmpty(key)) return ("No TikTok API key set.", null);
        var data = await _socialMedia.GetTikTokUserAsync(username, discordUserId);
        if (data == null) return ("No data from TikTok API.", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (json, json);
    }

    private async Task<(string summary, string? rawJson)> FetchLinkedInApi(string username, string discordUserId)
    {
        var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "linkedin");
        if (string.IsNullOrEmpty(key)) return ("No LinkedIn API key set.", null);
        var data = await _socialMedia.GetLinkedInUserAsync(username, discordUserId);
        if (data == null) return ("No data from LinkedIn API.", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (json, json);
    }

    private async Task<(string summary, string? rawJson)> FetchTelegramApi(string username, string discordUserId)
    {
        var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "telegram");
        if (string.IsNullOrEmpty(key)) return ("No Telegram API key set.", null);
        var data = await _socialMedia.GetTelegramUserAsync(username, discordUserId);
        if (data == null) return ("No data from Telegram API.", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (json, json);
    }

    private async Task<(string summary, string? rawJson)> FetchPinterestApi(string username, string discordUserId)
    {
        var key = await _apiKeyService.GetApiKeyAsync(discordUserId, "pinterest");
        if (string.IsNullOrEmpty(key)) return ("No Pinterest API key set.", null);
        var data = await _socialMedia.GetPinterestUserAsync(username, discordUserId);
        if (data == null) return ("No data from Pinterest API.", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (json, json);
    }

    private async Task<(string summary, string? rawJson)> FetchRedditAuthor(string username, string discordUserId)
    {
        var token = await _apiKeyService.GetApiKeyAsync(discordUserId, "apify");
        if (string.IsNullOrEmpty(token))
            return ("add an api key first (use /admin setkey apify <apify_token>)", null);
        
        var data = await _socialMedia.GetRedditAuthorAsync(username, token);
        if (data == null) return ("No data returned from Apify Reddit scraper.", null);
        
        var prettyJson = JsonConvert.SerializeObject(data, Formatting.Indented);
        prettyJson = DecodeUnicodeEscapes(prettyJson);
        
        var displayName = data["name"]?.Value<string>() ?? "N/A";
        var bio = data["about"]?.Value<string>() ?? "No bio";
        var createdRaw = data["created"]?.Value<string>() ?? "";
        var accountAge = "Unknown";
        if (DateTime.TryParse(createdRaw, out var createdDate))
            accountAge = $"{(DateTime.UtcNow - createdDate).Days} days";
        
        var postKarma = data["post_karma"]?.Value<int>() ?? 0;
        var commentKarma = data["comment_karma"]?.Value<int>() ?? 0;
        var totalKarma = postKarma + commentKarma;
        
        var isVerified = data["verified"]?.Value<bool>() ?? false;
        var isPremium = data["is_premium"]?.Value<bool>() ?? false;
        var isEmployee = data["is_employee"]?.Value<bool>() ?? false;
        var isModerator = data["is_moderator"]?.Value<bool>() ?? false;
        var isSuspended = data["is_suspended"]?.Value<bool>() ?? false;
        
        var moderatedList = data["moderated"]?.Select(s => s.ToString()).ToList() ?? new List<string>();
        var moderatedStr = moderatedList.Count > 0 ? string.Join(", ", moderatedList.Take(5)) : "None";
        if (moderatedList.Count > 5) moderatedStr += "...";
        
        var trophiesCount = data["trophies"]?.Count() ?? 0;
        var multiredditsCount = data["multireddits"]?.Count() ?? 0;
        
        var summary = $"=================================\n" +
                    $" Reddit Profile: {username}\n" +
                    $"=================================\n" +
                    $"Display Name : {displayName}\n" +
                    $"Bio          : {bio}\n" +
                    $"Account Age  : {accountAge}\n" +
                    $"Post Karma   : {postKarma}\n" +
                    $"Comment Karma: {commentKarma}\n" +
                    $"Total Karma  : {totalKarma}\n" +
                    $"Verified     : {(isVerified ? "Yes" : "No")}\n" +
                    $"Premium      : {(isPremium ? "Yes" : "No")}\n" +
                    $"Employee     : {(isEmployee ? "Yes" : "No")}\n" +
                    $"Moderator    : {(isModerator ? "Yes" : "No")}\n" +
                    $"Suspended    : {(isSuspended ? "Yes" : "No")}\n" +
                    $"Moderates    : {moderatedStr}\n" +
                    $"Trophies     : {trophiesCount}\n" +
                    $"Multireddits : {multiredditsCount}\n" +
                    $"=================================";
        return (summary, prettyJson);
    }

    private async Task<(string summary, string? rawJson)> FetchGitHubApi(string username, string _)
    {
        var data = await _socialMedia.GetGitHubUserAsync(username);
        if (data == null) return ("User not found.", null);
        var name = data["name"]?.Value<string>() ?? username;
        var repos = data["public_repos"]?.Value<int>() ?? 0;
        var followers = data["followers"]?.Value<int>() ?? 0;
        var following = data["following"]?.Value<int>() ?? 0;
        var bio = data["bio"]?.Value<string>() ?? "None";
        var summary = $"Name: {name}\nBio: {bio}\nPublic Repos: {repos}\nFollowers: {followers}\nFollowing: {following}";
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        json = DecodeUnicodeEscapes(json);
        return (summary, json);
    }

    private class ToolInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Func<string, string, Task<(string, string?)>>? Fetch { get; set; }
    }
}
