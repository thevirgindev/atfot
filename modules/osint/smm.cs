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
    private readonly KeyRedemptionService _keySvc;
    private readonly ApiKeyService _apiKeySvc;
    private readonly CooldownService _cd;
    private readonly EmbedBuilderService _emb;
    private readonly ExportService _export;
    private readonly SocialMediaService _sm;
    private readonly IHttpClientFactory _http;
    private readonly ImageService _img;

    private static readonly Dictionary<string, string> _sessionUser = new();
    private static readonly Dictionary<ulong, string> _userTarget = new();
    private static readonly Dictionary<string, List<(string id, string name, string result, string? raw)>> _cache = new();

    private static string decodeUnicode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, @"\\u([0-9A-Fa-f]{4})", m =>
        {
            char c = (char)Convert.ToInt32(m.Groups[1].Value, 16);
            return c.ToString();
        });
    }

    public SocialCmd(KeyRedemptionService keySvc, ApiKeyService apiKeySvc, CooldownService cd,
        EmbedBuilderService emb, ExportService export, SocialMediaService sm,
        IHttpClientFactory http, ImageService img)
    {
        _keySvc = keySvc;
        _apiKeySvc = apiKeySvc;
        _cd = cd;
        _emb = emb;
        _export = export;
        _sm = sm;
        _http = http;
        _img = img;
    }

    private async Task<bool> isAuthed() => await _keySvc.IsAuthorizedAsync(Context.User.Id.ToString());

    // builds the platform dropdown — all platforms in one menu
    private static SelectMenuBuilder buildMenu(string sessionId)
    {
        return new SelectMenuBuilder()
            .WithPlaceholder("select a platform...")
            .WithCustomId($"sp:{sessionId}")
            .AddOption("instagram", "instagram")
            .AddOption("reddit", "reddit")
            .AddOption("github", "github")
            .AddOption("twitter", "twitter")
            .AddOption("tiktok", "tiktok")
            .AddOption("linkedin", "linkedin")
            .AddOption("pinterest", "pinterest")
            .AddOption("facebook", "facebook");
    }

    private const string profileDesc =
        "Enter the username you want to\n" +
        "investigate across social media\n" +
        "platforms. Select a platform from\n" +
        "the dropdown to gather public\n" +
        "data, social footprints, and\n" +
        "associated accounts across the\n" +
        "selected service.";

    private async Task<(Embed emb, Stream? imgStream, SelectMenuBuilder menu)> buildProfile(string username, string sessionId)
    {
        var menu = buildMenu(sessionId);
        Stream? imgStream = null;
        try { imgStream = await _img.profileLookupImg(); } catch { }
        EmbedBuilder eb = new EmbedBuilder()
            .WithTitle("Profile Lookup")
            .WithDescription(profileDesc)
            .AddField("user", $"`{username}`", inline: true)
            .AddField("platforms", "`ig, reddit, gh, twitter, tiktok, linkedin, pinterest, fb`", inline: true)
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText);
        if (imgStream != null)
            eb.WithImageUrl("attachment://atfot.jpg");
        return (eb.Build(), imgStream, menu);
    }

    // helper to apply profile embed to a message
    private async Task sendProfile(IUserMessage msg, string username, string sessionId)
    {
        var (emb, img, menu) = await buildProfile(username, sessionId);
        var comp = new ComponentBuilder().WithSelectMenu(menu).Build();
        if (img != null)
        {
            var att = new List<FileAttachment> { new FileAttachment(img, "atfot.jpg") };
            await msg.ModifyAsync(m => { m.Embed = emb; m.Attachments = att; m.Components = comp; });
        }
        else
        {
            await msg.ModifyAsync(m => { m.Embed = emb; m.Components = comp; });
        }
    }

    [SlashCommand("username", "search for a username across major social platforms")]
    public async Task SocialUsername([Summary("username", "target username (without @)")] string username)
    {
        if (!await isAuthed()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait {rem.TotalSeconds:f0}s.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());

        await DeferAsync();
        _userTarget[Context.User.Id] = username;
        var sid = Guid.NewGuid().ToString("N");
        _sessionUser[sid] = username;

        var menu = buildMenu(sid);
        var comp = new ComponentBuilder().WithSelectMenu(menu).Build();

        var loadEmb = new EmbedBuilder()
            .WithDescription("```[INFO] initializing modules...```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();

        var resp = await FollowupAsync(embed: loadEmb, components: null);

        var status = new (string text, int delay)[]
        {
            ("```[INFO] initializing modules...```", 800),
            ("```[DONE] initialized```", 200),
            ("```[INFO] checking providers health...```", 1200),
            ("```[DONE] providers healthy```", 200),
            ("```[INFO] processing request...```", 400),
            ("```[DONE] processing request```", 200),
            ("```[DONE] profile loaded```", 1000)
        };

        foreach (var (text, delay) in status)
        {
            await Task.Delay(delay);
            await resp.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription(text)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                    .Build();
            });
        }

        try
        {
            var (emb, img, menu2) = await buildProfile(username, sid);
            var comp2 = new ComponentBuilder().WithSelectMenu(menu2).Build();
            if (img != null)
            {
                var att2 = new List<FileAttachment> { new FileAttachment(img, "atfot.jpg") };
                await resp.ModifyAsync(m => { m.Embed = emb; m.Attachments = att2; m.Components = comp2; });
            }
            else
            {
                await resp.ModifyAsync(m => { m.Embed = emb; m.Components = comp2; });
            }
        }
        catch
        {
            await resp.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithTitle("Profile Lookup")
                    .WithDescription(profileDesc)
                    .AddField("user", $"`{username}`", inline: true)
                    .AddField("platforms", "`ig, reddit, gh, twitter, tiktok, linkedin, pinterest, fb`", inline: true)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                    .Build();
                m.Components = comp;
            });
        }
    }

    // unified platform selection — all platforms go through here
    [ComponentInteraction("sp:*", ignoreGroupNames: true)]
    public async Task onPlatformSelect(string sessionId)
    {
        await DeferAsync();
        try
        {
            var smc = Context.Interaction as SocketMessageComponent;
            if (smc == null) return;
            var platform = smc.Data.Values.FirstOrDefault();
            if (string.IsNullOrEmpty(platform)) return;

            var origMsg = await (Context.Channel as ISocketMessageChannel).GetMessageAsync(smc.Message.Id) as IUserMessage;
            if (origMsg != null)
                await origMsg.ModifyAsync(m => { m.Components = new ComponentBuilder().Build(); m.Attachments = null; });

            if (!_sessionUser.TryGetValue(sessionId, out var username))
            {
                await FollowupAsync("[ERR] session expired, run /social username again.", ephemeral: true);
                return;
            }

            var userId = Context.User.Id.ToString();
            var tools = getTools(platform);
            if (tools == null || tools.Count == 0)
            {
                await FollowupAsync($"[ERR] no tools for {platform}.", ephemeral: true);
                return;
            }

            var channel = Context.Channel as ISocketMessageChannel;
            var oMsg = await channel.GetMessageAsync(smc.Message.Id) as IUserMessage;
            if (oMsg == null) return;

            var sts = new (string text, int delay)[]
            {
                ("```[INFO] running social media search...```", 1000),
                ("```[DONE] providers healthy```", 200),
                ("```[INFO] processing request...```", 400),
                ("```[DONE] processing request```", 200),
                ("```[DONE] search complete```", 700)
            };

            foreach (var (text, delay) in sts)
            {
                await Task.Delay(delay);
                await oMsg.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithTitle($"{platform} scraper")
                        .WithDescription(text)
                        .WithColor(new Color(0x55, 0x55, 0x55))
                        .WithCurrentTimestamp()
                        .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                        .Build();
                    m.Attachments = null;
                });
            }

            var results = new List<(string id, string name, string result, string? raw)>();
            foreach (var tool in tools)
            {
                if (tool.Fetch == null) continue;
                try
                {
                    var (summary, raw) = await tool.Fetch(username, userId);
                    results.Add((tool.Id!, tool.Name!, summary ?? "[ERR] no data returned.", raw));
                }
                catch (Exception ex)
                {
                    results.Add((tool.Id!, tool.Name!, $"[ERR] {ex.Message}", null));
                }
            }

            if (results.Count == 0)
            {
                await FollowupAsync("[ERR] no data could be fetched.", ephemeral: true);
                return;
            }

            var ck = $"{sessionId}_{platform}_{username}";
            _cache[ck] = results;
            await showTool(ck, 0, oMsg.Id);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"[ERR] {ex.Message}", ephemeral: true);
        }
    }

    private async Task showTool(string ck, int idx, ulong msgId)
    {
        if (!_cache.TryGetValue(ck, out var tools) || idx < 0 || idx >= tools.Count) return;
        var t = tools[idx];

        var emb = new EmbedBuilder()
            .WithTitle(t.name)
            .WithDescription($"```\n{t.result}\n```")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();

        var comps = new ComponentBuilder()
            .WithButton("◀", $"sc:{ck}:{idx - 1}", ButtonStyle.Secondary, disabled: idx == 0)
            .WithButton("▶", $"sc:{ck}:{idx + 1}", ButtonStyle.Secondary, disabled: idx == tools.Count - 1)
            .WithButton("txt", $"se:{ck}:{idx}:txt", ButtonStyle.Secondary)
            .WithButton("json", $"se:{ck}:{idx}:json", ButtonStyle.Secondary)
            .WithButton("back", $"sb:{ck}", ButtonStyle.Secondary)
            .Build();

        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = emb; m.Components = comps; });
        else
            await FollowupAsync(embed: emb, components: comps);
    }

    [ComponentInteraction("sc:*:*", ignoreGroupNames: true)]
    public async Task onCarousel(string ck, string idxStr)
    {
        await DeferAsync();
        if (!int.TryParse(idxStr, out int idx)) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await showTool(ck, idx, smc.Message.Id);
    }

    [ComponentInteraction("se:*:*:*", ignoreGroupNames: true)]
    public async Task onExport(string ck, string idxStr, string fmt)
    {
        await DeferAsync(ephemeral: true);
        if (!int.TryParse(idxStr, out int idx)) return;
        if (!_cache.TryGetValue(ck, out var tools) || idx < 0 || idx >= tools.Count)
        {
            await FollowupAsync("[ERR] export data not found.", ephemeral: true);
            return;
        }

        var t = tools[idx];
        if (string.IsNullOrEmpty(t.raw))
        {
            await FollowupAsync("[ERR] no raw data to export.", ephemeral: true);
            return;
        }

        var parts = ck.Split('_');
        var username = parts.Length >= 3 ? parts[2] : parts.Last();
        var dto = new ScanResultDto { TargetLookup = username, ModuleSource = t.id, RawApiResponse = t.raw, Summary = t.result };
        string fn = $"{t.id}_{username}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = fmt == "json" ? _export.BuildJsonStream(dto) : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{fn}.{fmt}", $"exported {t.name} data.");
    }

    // back to menu — regenerates image + text + platform dropdown
    [ComponentInteraction("sb:*", ignoreGroupNames: true)]
    public async Task onBackToMenu(string ck)
    {
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;

        var parts = ck.Split('_');
        string sessionId, username;
        if (parts.Length >= 3) { sessionId = parts[0]; username = parts[2]; }
        else if (parts.Length == 2) { sessionId = parts[0]; username = parts[1]; }
        else { await smc.RespondAsync("[ERR] invalid session.", ephemeral: true); return; }

        if (!_sessionUser.ContainsKey(sessionId))
        {
            await smc.RespondAsync("[ERR] session expired, run /social username again.", ephemeral: true);
            return;
        }

        try
        {
            var (emb, img, menu) = await buildProfile(username, sessionId);
            var comp4 = new ComponentBuilder().WithSelectMenu(menu).Build();
            if (img != null)
            {
                var att4 = new List<FileAttachment> { new FileAttachment(img, "atfot.jpg") };
                await smc.UpdateAsync(msg => { msg.Embed = emb; msg.Components = comp4; msg.Attachments = att4; });
            }
            else
            {
                await smc.UpdateAsync(msg => { msg.Embed = emb; msg.Components = comp4; });
            }
        }
        catch
        {
            await smc.UpdateAsync(msg =>
            {
                msg.Embed = new EmbedBuilder()
                    .WithTitle("Profile Lookup")
                    .WithDescription(profileDesc)
                    .AddField("user", $"`{username}`", inline: true)
                    .AddField("platforms", "`ig, reddit, gh, twitter, tiktok, linkedin, pinterest, fb`", inline: true)
                    .WithColor(new Color(0x55, 0x55, 0x55))
                    .WithCurrentTimestamp()
                    .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
                    .Build();
                msg.Components = new ComponentBuilder().WithSelectMenu(buildMenu(sessionId)).Build();
            });
        }
    }

    // tool definitions per platform
    private List<ToolDef>? getTools(string platform)
    {
        return platform switch
        {
            "instagram" => new()
            {
                new ToolDef { Id = "socialapi", Name = "instagram socialapi", Fetch = fetchSocialApiIg },
                new ToolDef { Id = "serpapi", Name = "instagram serpapi", Fetch = fetchSerpIg }
            },
            "twitter" => new()
            {
                new ToolDef { Id = "apify_twitter", Name = "twitter apify", Fetch = fetchApifyTwitter },
                new ToolDef { Id = "twitter_api", Name = "twitter api v2", Fetch = fetchTwitter }
            },
            "tiktok" => new()
            {
                new ToolDef { Id = "apify_tiktok", Name = "tiktok apify", Fetch = fetchApifyTikTok },
                new ToolDef { Id = "tiktok_api", Name = "tiktok rapidapi", Fetch = fetchTikTok }
            },
            "linkedin" => new()
            {
                new ToolDef { Id = "apify_linkedin", Name = "linkedin apify", Fetch = fetchApifyLinkedIn },
                new ToolDef { Id = "linkedin_api", Name = "linkedin rapidapi", Fetch = fetchLinkedIn }
            },
            "pinterest" => new()
            {
                new ToolDef { Id = "apify_pinterest", Name = "pinterest apify", Fetch = fetchApifyPinterest },
                new ToolDef { Id = "pinterest_api", Name = "pinterest rapidapi", Fetch = fetchPinterest }
            },
            "reddit" => new()
            {
                new ToolDef { Id = "apify_reddit", Name = "reddit apify", Fetch = fetchReddit }
            },
            "github" => new()
            {
                new ToolDef { Id = "github_public", Name = "github public api", Fetch = fetchGitHub },
                new ToolDef { Id = "apify_github", Name = "github apify", Fetch = fetchApifyGitHub }
            },
            "facebook" => new()
            {
                new ToolDef { Id = "serpapi_facebook", Name = "facebook serpapi", Fetch = fetchFacebookSerp },
                new ToolDef { Id = "apify_facebook", Name = "facebook apify", Fetch = fetchApifyFacebook }
            },
            _ => null
        };
    }

    // instagram fetchers
    private async Task<(string summary, string? raw)> fetchSocialApiIg(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "socialapi");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add a socialapi key first", null);
        try
        {
            var data = await _sm.GetSocialApisInstagramUserAsync(username, token);
            if (data == null) return ("[ERR] no data from socialapis.io", null);
            if (data["error"] != null) return ($"[ERR] socialapis: {data["error"]}", null);

            var pretty = JsonConvert.SerializeObject(data, Formatting.Indented);
            var fullN = data["data"]?["full_name"]?.Value<string>() ?? data["full_name"]?.Value<string>() ?? "n/a";
            var accId = data["data"]?["id"]?.Value<string>() ?? data["id"]?.Value<string>() ?? "n/a";
            var followers = data["data"]?["followers_count"]?.Value<int>() ?? data["followers_count"]?.Value<int>() ?? 0;
            var following = data["data"]?["following_count"]?.Value<int>() ?? data["following_count"]?.Value<int>() ?? 0;
            var posts = data["data"]?["media_count"]?.Value<int>() ?? data["media_count"]?.Value<int>() ?? 0;
            var isPriv = data["data"]?["is_private"]?.Value<bool>() ?? data["is_private"]?.Value<bool>() ?? false;
            var isVerif = data["data"]?["is_verified"]?.Value<bool>() ?? data["is_verified"]?.Value<bool>() ?? false;
            var accT = data["data"]?["is_business_account"]?.Value<bool>() ?? data["is_business_account"]?.Value<bool>() ?? false;
            var extUrl = data["data"]?["external_url"]?.Value<string>() ?? data["external_url"]?.Value<string>() ?? "none";
            if (string.IsNullOrEmpty(extUrl)) extUrl = "none";

            var s = $"=================================\n" +
                    $" profile summary ==> {username}\n" +
                    $"=================================\n" +
                    $"full name      : {fullN}\n" +
                    $"account id     : {accId}\n" +
                    $"followers      : {followers}\n" +
                    $"following      : {following}\n" +
                    $"posts          : {posts}\n" +
                    $"private        : {(isPriv ? "yes" : "no")}\n" +
                    $"verified       : {(isVerif ? "yes" : "no")}\n" +
                    $"account type   : {(accT ? "business" : "personal")}\n" +
                    $"external url   : {extUrl}\n" +
                    $"=================================";
            return (s, pretty);
        }
        catch (Exception ex) { return ($"[ERR] socialapi: {ex.Message}", null); }
    }

    private async Task<(string summary, string? raw)> fetchSerpIg(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "serpapi");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add a serpapi key first", null);
        try
        {
            var data = await _sm.GetSerpApiInstagramUserAsync(username, token);
            if (data == null) return ("[ERR] no data from serpapi", null);
            if (data["error"] != null) return ($"[ERR] serpapi: {data["error"]}", null);

            var pretty = JsonConvert.SerializeObject(data, Formatting.Indented);
            var fn = data["full_name"]?.Value<string>() ?? "n/a";
            var ai = data["id"]?.Value<string>() ?? "n/a";
            var fw = data["followers"]?.Value<int>() ?? 0;
            var fg = data["following"]?.Value<int>() ?? 0;
            var po = data["posts_count"]?.Value<int>() ?? 0;
            var ip = data["is_private"]?.Value<bool>() ?? false;
            var iv = data["is_verified"]?.Value<bool>() ?? false;
            var ib = data["is_business_account"]?.Value<bool>() ?? false;
            var eu = data["external_url"]?.Value<string>() ?? "none";
            if (string.IsNullOrEmpty(eu)) eu = "none";
            var ca = data["created_at"]?.Value<string>() ?? "n/a";
            var cd = DateTime.TryParse(ca, out var dt) ? dt.ToString("yyyy-MM-dd") : ca;
            var pp = data["profile_pic_url"]?.Value<string>() ?? "n/a";

            var s = $"=================================\n" +
                    $" profile summary ==> {username}\n" +
                    $"=================================\n" +
                    $"full name      : {fn}\n" +
                    $"account id     : {ai}\n" +
                    $"followers      : {fw}\n" +
                    $"following      : {fg}\n" +
                    $"posts          : {po}\n" +
                    $"private        : {(ip ? "yes" : "no")}\n" +
                    $"verified       : {(iv ? "yes" : "no")}\n" +
                    $"account type   : {(ib ? "business" : "personal")}\n" +
                    $"external url   : {eu}\n" +
                    $"created at     : {cd}\n" +
                    $"profile pic    : {pp}\n" +
                    $"=================================";
            return (s, pretty);
        }
        catch (Exception ex) { return ($"[ERR] serpapi: {ex.Message}", null); }
    }

    // rapidapi fetchers
    private async Task<(string summary, string? raw)> fetchTwitter(string username, string discordId)
    {
        var key = await _apiKeySvc.GetApiKeyAsync(discordId, "twitter");
        if (string.IsNullOrEmpty(key)) return ("[ERR] no twitter api key, use /setapikey twitter <token>", null);
        var data = await _sm.GetTwitterUserAsync(username, discordId);
        if (data == null) return ("[ERR] no data from twitter api", null);
        var json = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        return (json, json);
    }

    private async Task<(string summary, string? raw)> fetchTikTok(string username, string discordId)
    {
        var key = await _apiKeySvc.GetApiKeyAsync(discordId, "tiktok");
        if (string.IsNullOrEmpty(key)) return ("[ERR] no tiktok api key", null);
        var data = await _sm.GetTikTokUserAsync(username, discordId);
        if (data == null) return ("[ERR] no data from tiktok api", null);
        var json = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        return (json, json);
    }

    private async Task<(string summary, string? raw)> fetchLinkedIn(string username, string discordId)
    {
        var key = await _apiKeySvc.GetApiKeyAsync(discordId, "linkedin");
        if (string.IsNullOrEmpty(key)) return ("[ERR] no linkedin api key", null);
        var data = await _sm.GetLinkedInUserAsync(username, discordId);
        if (data == null) return ("[ERR] no data from linkedin api", null);
        var json = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        return (json, json);
    }

    private async Task<(string summary, string? raw)> fetchPinterest(string username, string discordId)
    {
        var key = await _apiKeySvc.GetApiKeyAsync(discordId, "pinterest");
        if (string.IsNullOrEmpty(key)) return ("[ERR] no pinterest api key", null);
        var data = await _sm.GetPinterestUserAsync(username, discordId);
        if (data == null) return ("[ERR] no data from pinterest api", null);
        var json = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        return (json, json);
    }

    private async Task<(string summary, string? raw)> fetchReddit(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first (/setapikey apify <token>)", null);
        var data = await _sm.GetRedditAuthorAsync(username, token);
        if (data == null) return ("[ERR] no data from apify reddit scraper", null);

        var pretty = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        var dn = data["name"]?.Value<string>() ?? "n/a";
        var bio = data["about"]?.Value<string>() ?? "no bio";
        var cr = data["created"]?.Value<string>() ?? "";
        var age = DateTime.TryParse(cr, out var cd) ? $"{(DateTime.UtcNow - cd).Days} days" : "unknown";
        var pk = data["post_karma"]?.Value<int>() ?? 0;
        var ck = data["comment_karma"]?.Value<int>() ?? 0;
        var ver = data["verified"]?.Value<bool>() ?? false;
        var prem = data["is_premium"]?.Value<bool>() ?? false;
        var emp = data["is_employee"]?.Value<bool>() ?? false;
        var mod = data["is_moderator"]?.Value<bool>() ?? false;
        var sus = data["is_suspended"]?.Value<bool>() ?? false;
        var troph = data["trophies"]?.Count() ?? 0;

        var s = $"=================================\n" +
                $" reddit profile: {username}\n" +
                $"=================================\n" +
                $"display name : {dn}\n" +
                $"bio          : {bio}\n" +
                $"account age  : {age}\n" +
                $"post karma   : {pk}\n" +
                $"comment karma: {ck}\n" +
                $"total karma  : {pk + ck}\n" +
                $"verified     : {(ver ? "yes" : "no")}\n" +
                $"premium      : {(prem ? "yes" : "no")}\n" +
                $"employee     : {(emp ? "yes" : "no")}\n" +
                $"moderator    : {(mod ? "yes" : "no")}\n" +
                $"suspended    : {(sus ? "yes" : "no")}\n" +
                $"trophies     : {troph}\n" +
                $"=================================";
        return (s, pretty);
    }

    private async Task<(string summary, string? raw)> fetchGitHub(string username, string _)
    {
        var data = await _sm.GetGitHubUserAsync(username);
        if (data == null) return ("[ERR] user not found", null);
        var name = data["name"]?.Value<string>() ?? username;
        var repos = data["public_repos"]?.Value<int>() ?? 0;
        var fw = data["followers"]?.Value<int>() ?? 0;
        var fg = data["following"]?.Value<int>() ?? 0;
        var bio = data["bio"]?.Value<string>() ?? "none";
        var json = decodeUnicode(JsonConvert.SerializeObject(data, Formatting.Indented));
        var s = $"name: {name}\nbio: {bio}\npublic repos: {repos}\nfollowers: {fw}\nfollowing: {fg}";
        return (s, json);
    }

    // apify fetchers
    private async Task<(string summary, string? raw)> fetchApifyTwitter(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetTwitterUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify twitter", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private async Task<(string summary, string? raw)> fetchApifyTikTok(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetTikTokUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify tiktok", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private async Task<(string summary, string? raw)> fetchApifyLinkedIn(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetLinkedInUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify linkedin", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private async Task<(string summary, string? raw)> fetchApifyPinterest(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetPinterestUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify pinterest", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private async Task<(string summary, string? raw)> fetchApifyGitHub(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetGitHubUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify github", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private async Task<(string summary, string? raw)> fetchFacebookSerp(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "serpapi");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add a serpapi key first", null);
        var client = _http.CreateClient();
        var url = $"https://serpapi.com/search?engine=facebook_profile&username={Uri.EscapeDataString(username)}&api_key={token}";
        try
        {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return ($"[ERR] serpapi http {response.StatusCode}", null);
            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            if (data["error"] != null) return ($"[ERR] serpapi: {data["error"]}", json);

            var pretty = JsonConvert.SerializeObject(data, Formatting.Indented);
            var name = data["name"]?.ToString() ?? "n/a";
            var followers = data["followers"]?.ToString() ?? "0";
            var about = data["about"]?.ToString() ?? "none";
            var location = data["location"]?.ToString() ?? "unknown";
            var s = $"=================================\n" +
                    $" facebook profile: {username}\n" +
                    $"=================================\n" +
                    $"name      : {name}\n" +
                    $"followers : {followers}\n" +
                    $"about     : {about}\n" +
                    $"location  : {location}\n" +
                    $"=================================";
            return (s, pretty);
        }
        catch (Exception ex) { return ($"[ERR] serpapi: {ex.Message}", null); }
    }

    private async Task<(string summary, string? raw)> fetchApifyFacebook(string username, string discordId)
    {
        var token = await _apiKeySvc.GetApiKeyAsync(discordId, "apify");
        if (string.IsNullOrEmpty(token)) return ("[ERR] add an apify key first", null);
        var data = await _sm.GetFacebookUserByApify(username, token);
        if (data == null) return ("[ERR] no data from apify facebook", null);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return (json.Length > 4000 ? json[..4000] + "...(truncated)" : json, json);
    }

    private class ToolDef
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Func<string, string, Task<(string, string?)>>? Fetch { get; set; }
    }
}
