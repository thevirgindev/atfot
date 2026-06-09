using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using pewbot.core.services;

namespace pewbot.modules.core;

public class GuideCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;

    private static readonly string[] _pages = {
        // PAGE 1 – INTRODUCTION
        "**PEWBOT – COMPLETE OSINT GUIDE**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "This bot provides open‑source intelligence (OSINT) for social media,\n" +
        "Discord users, network infrastructure, data breaches, and more.\n\n" +
        "**Before you start**\n" +
        "1. The bot owner must generate a master key: `/genkey`\n" +
        "2. You redeem it: `/redeem <key>`\n" +
        "3. Then all commands become available.\n\n" +
        "**Navigation**\n" +
        "Use the ◀ and ▶ buttons below to flip through this guide.\n" +
        "There are 11 pages in total.\n\n" +
        "**Support**\n" +
        "- Report bugs or request features: `/report` (see page 11)\n" +
        "- Check logs: `logs/bot.log` in the bot's directory.\n" +
        "- Credits: made by @thevirgindev.",

        // PAGE 2 – KEYS MANAGEMENT
        "**MANAGING KEYS**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "**Master key (owner only)**\n" +
        "```\n/genkey\n```\nGenerates a 16‑character code like `D62DEDE6DDA34F04`.\n" +
        "Give it to users you trust. They run:\n```\n/redeem D62DEDE6DDA34F04\n```\n\n" +
        "**API keys for external services**\n" +
        "Many commands require their own API keys (Shodan, Hunter, etc.).\n" +
        "Add a key:\n```\n/setapikey <service> <key> [--default]\n```\n" +
        "Example:\n```\n/setapikey shodan abc123 --default\n```\n" +
        "You can store **multiple keys** per service (e.g., two Shodan keys).\n" +
        "View your keys:\n```\n/mykeys\n```\n" +
        "Remove a key:\n```\n/removeapikey <service> <keyid>\n```\n" +
        "Change default key:\n```\n/setdefaultkey <service> <keyid>\n```\n\n" +
        "**Where to get API keys**\n" +
        "- Shodan → shodan.io (free 10 queries/month)\n" +
        "- Hunter.io → hunter.io (50 free/month)\n" +
        "- VirusTotal → virustotal.com (500/day)\n" +
        "- AbuseIPDB → abuseipdb.com (100k/month)\n" +
        "- PeopleDataLabs → peopledatalabs.com (100 credits free)\n" +
        "- ipgeolocation.io → 3000 calls/month free\n" +
        "- OnionEngine → free tier\n" +
        "- OathNet → oathnet.org (10 daily free lookups)\n" +
        "- SocialApi, SerpApi, Apify, Twitter, RapidAPI – see their websites.",

        // PAGE 3 – SOCIAL MEDIA OSINT
        "**SOCIAL MEDIA FOOTPRINTS**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "Command:\n```\n/social username <handle>\n```\n\n" +
        "**What happens**\n" +
        "1. Loading animation with status messages.\n" +
        "2. A dynamic image appears with the handle.\n" +
        "3. A dropdown menu lets you choose a platform.\n" +
        "4. The bot fetches data using one or more tools per platform.\n" +
        "5. Results appear in a **carousel** (◀/▶ buttons).\n" +
        "6. Export buttons (TXT/JSON) let you save raw data.\n\n" +
        "**Supported platforms and required keys**\n" +
        "- Instagram: SocialApi, SerpApi (both need keys)\n" +
        "- Reddit: Apify (needs Apify token, stored as `apify`)\n" +
        "- GitHub: public API (no key)\n" +
        "- Twitter: Twitter API v2 (needs Bearer token, service `twitter`)\n" +
        "- TikTok, LinkedIn, Telegram, Pinterest: RapidAPI (service names `tiktok`, `linkedin`, `telegram`, `pinterest`)\n\n" +
        "**Example workflow**\n" +
        "```\n/social username cristiano\n```\nWait for the image → pick Instagram → choose SocialApi.\nThe embed will show followers, following, posts, etc.",

        // PAGE 4 – DEEP DISCORD LOOKUP
        "**DISCORD USER OSINT**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "Command:\n```\n/discord lookup <userid>\n```\n\n" +
        "**Tools used**\n" +
        "- **Public API** (japi.rest + fallback) – no key, works for any user. Returns username, global name, badges, accent colour, avatar, banner, linked accounts (rare).\n" +
        "- **Official Discord API** – requires mutual guild (bot must share a server with the user). Shows mutual guilds if `GuildMembers` intent is enabled.\n" +
        "- **OathNet** – requires free API key (`oathnet` service). Provides username history and Roblox correlation.\n" +
        "- Other tools (OsintCat, LeakInsight, etc.) are placeholders.\n\n" +
        "**Example output (Public API)**\n" +
        "```diff\n" +
        "ID          : 792430467176857610\n" +
        "Username    : ty.64\n" +
        "Global Name : Justice\n" +
        "Created At  : 2020-12-26 16:35:46\n" +
        "Badges      : HypeSquad Bravery\n" +
        "Avatar URL  : https://cdn.discordapp.com/avatars/...\n" +
        "Banner      : None\n" +
        "```\n" +
        "The carousel lets you switch between tools. Export buttons save the raw JSON response.",

        // PAGE 5 – API‑BASED OSINT TOOLS (osint group)
        "**API‑BASED OSINT TOOLS**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "All commands under `/osint`:\n\n" +
        "**HaveIBeenPwned** (no key)\n" +
        "```\n/osint hibp email@example.com\n```\nShows breaches containing the email.\n\n" +
        "**AbuseIPDB** (needs key `abuseipdb`)\n" +
        "```\n/osint abuseip 8.8.8.8\n```\nReturns abuse score, total reports, country.\n\n" +
        "**Hunter.io** (needs key `hunter`)\n" +
        "```\n/osint hunter example.com\n```\nLists email addresses found for the domain.\n\n" +
        "**PeopleDataLabs** (needs key `peopledatalabs`)\n" +
        "```\n/osint pdl email@example.com\n```\nName, location, job title, company.\n\n" +
        "**ipgeolocation.io** (needs key `ipgeolocation`)\n" +
        "```\n/osint ipgeo 8.8.8.8\n```\nCity, country, ISP, coordinates.\n\n" +
        "**ip-api.com** (no key)\n" +
        "```\n/osint ipapi 8.8.8.8\n```\nFree geolocation, no rate limit for non‑commercial.\n\n" +
        "**OnionEngine** (needs key `onionengine`)\n" +
        "```\n/osint onion keyword\n```\nSearches dark web for the keyword.\n\n" +
        "All commands show a loading animation, then replace the embed with the result and export buttons.",

        // PAGE 6 – COMMAND‑LINE TOOLS (Docker version)
        "**COMMAND‑LINE OSINT TOOLS (DOCKER)**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "If you run the bot using the official Docker image, all CLI tools are pre‑installed.\n" +
        "You don't need to install anything. The bot will run them directly.\n\n" +
        "**Available commands**\n" +
        "- `/osint sherlock <username>` – Search across 400+ sites.\n" +
        "- `/osint harvester <domain>` – Emails, subdomains, virtual hosts.\n" +
        "- `/osint spiderfoot <target>` – Automated OSINT scanning.\n" +
        "- `/osint recon <domain>` – Recon-ng web reconnaissance.\n" +
        "- `/osint subfinder <domain>` – Passive subdomain enumeration.\n" +
        "- `/osint amass <domain>` – Attack surface mapping.\n" +
        "- `/osint torbot <onion_url>` – Crawl onion sites (requires Tor).\n" +
        "- `/osint odcrawler <username>` – Username disclosure.\n" +
        "- `/osint whocord <type> <target>` – All‑in‑one (username, email, or Discord ID).\n\n" +
        "**Behaviour**\n" +
        "- Each command shows a loading embed: \"Running tool on target... may take 30‑120 seconds\".\n" +
        "- After completion, the embed is replaced with the output (truncated to 4000 characters).\n" +
        "- If the tool times out, the bot shows an error.\n" +
        "- Export buttons (TXT/JSON) allow saving the raw console output.\n\n" +
        "**Running without Docker**\n" +
        "If you run the bot natively (not in Docker), you must install each tool manually using `pip` or `go`. The official Docker image is the recommended way.",

        // PAGE 7 – INFRASTRUCTURE & NETWORK
        "**INFRASTRUCTURE & NETWORK**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "Command:\n```\n/infra investigate <ip|domain|onion>\n```\n" +
        "Provides links to Shodan, VirusTotal, Censys, DNSDumpster, SecurityTrails.\n" +
        "For .onion addresses, also Ahmia and DarkSearch.io.\n" +
        "If you have set API keys for Shodan, VirusTotal, or SecurityTrails,\n" +
        "the bot will display live data (ISP, open ports, subdomains, etc.).\n\n" +
        "**DNS investigation**\n" +
        "```\n/infra dns <domain>\n```\n" +
        "Shows DNS history and subdomain enumeration (requires SecurityTrails API key).\n\n" +
        "**Threat actors**\n" +
        "- `/threatactor lookup <name>` – links to Malpedia, MITRE ATT&CK, APT groups.\n" +
        "- `/threatactor map` – live cyber threat maps (Kaspersky, Check Point, etc.).\n\n" +
        "Most of these commands are link aggregators; they don't require API keys unless you want live data.",

        // PAGE 8 – UTILITY COMMANDS
        "**UTILITY COMMANDS**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "**Geolocation**\n" +
        "```\n/geo locate \"New York\"\n```\nReturns Google Maps, SunCalc, Wikimapia, what3words links.\n\n" +
        "**Image analysis**\n" +
        "```\n/image reverse <url>\n```\nTinEye, Google Lens, Yandex, PimEyes links.\n" +
        "```\n/image metadata <url>\n```\nExtracts EXIF data (GPS, camera model, software).\n\n" +
        "**Keyword trends**\n" +
        "```\n/keyword trends <topic>\n```\nGoogle Trends, KeywordTool, Ubersuggest links.\n\n" +
        "**Maritime**\n" +
        "```\n/maritime vessel <name>\n```\nVesselFinder, MarineTraffic, FleetMon links.\n\n" +
        "**Vehicle**\n" +
        "```\n/vehicle plate <plate>\n```\nUS/Europe license plate lookup links.\n\n" +
        "**News & fact check**\n" +
        "```\n/news latest <topic>\n```\nGoogle News, Reuters, BBC, AP links.\n" +
        "```\n/news factcheck <claim>\n```\nSnopes, FactCheck.org, PolitiFact links.\n\n" +
        "**World Bank data**\n" +
        "```\n/data worldbank <indicator> [country]\n```\nGDP, population, etc. (no key).\n\n" +
        "**Web monitoring**\n" +
        "```\n/monitor alert <url>\n```\nChangeDetection, Visualping, Google Alerts links.\n\n" +
        "**Privacy tools**\n" +
        "```\n/privacy tools\n```\nList of recommended privacy tools.\n\n" +
        "**Miscellaneous**\n" +
        "```\n/misc cyberchef <input> <operation>\n```\nEncoding/hashing (md5, base64, url encode, etc.).\n" +
        "```\n/misc wigle <ssid>\n```\nWi‑Fi network mapping (wigle.net).",

        // PAGE 9 – ADMIN & OWNER COMMANDS
        "**ADMIN & OWNER COMMANDS**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "**Owner‑only** (Discord ID set in `.env`)\n" +
        "```\n/genkey\n```\nGenerate a new master key.\n" +
        "```\n/redemptions\n```\nView all redeemed keys.\n" +
        "```\n/status <online|idle|dnd|invisible> [activity]\n```\nChange bot presence.\n" +
        "```\n/rmk <userid>\n```\nRevoke a user's access.\n\n" +
        "**All authorised users**\n" +
        "```\n/redeem <key>\n```\nActivate the bot with a master key.\n" +
        "```\n/setapikey <service> <key> [--default]\n```\nAdd an API key.\n" +
        "```\n/mykeys\n```\nList your keys (masked).\n" +
        "```\n/removeapikey <service> <keyid>\n```\nDelete a key.\n" +
        "```\n/setdefaultkey <service> <keyid>\n```\nChange default key for a service.\n\n" +
        "All these commands are top‑level (no `/admin` prefix).",

        // PAGE 10 – FAQ & TROUBLESHOOTING
        "**FAQ / TROUBLESHOOTING**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "**Q: \"No data returned from Discord API (mutual guild required)\"**\n" +
        "A: Your bot does not share any server with the target user. Use the public API tool in `/discord lookup` instead.\n\n" +
        "**Q: CLI tool not found**\n" +
        "A: If you're not using Docker, you must install the tool. If you are using Docker, ensure the image is up‑to‑date (see page 11).\n\n" +
        "**Q: \"Invalid or already used master key\"**\n" +
        "A: The key was already redeemed or doesn't exist. The owner must generate a new one with `/genkey`.\n\n" +
        "**Q: Rate limiting / cooldown**\n" +
        "A: Most commands have a 5‑second cooldown per user. Wait before running again.\n\n" +
        "**Q: Image generation fails (profile image not appearing)**\n" +
        "A: Make sure `resources/profile-lookup.jpg` and `JetBrainsMono-Bold.ttf` exist in the bot's working directory.\n\n" +
        "**Q: The bot is slow**\n" +
        "A: CLI tools can take 30‑120 seconds. API calls depend on external services. Be patient.\n\n" +
        "**Q: How do I report a bug or request a feature?**\n" +
        "A: Use the `/report` command (see next page).\n\n" +
        "**Q: Where are the logs?**\n" +
        "A: `logs/bot.log` inside the bot's directory. In Docker, use `docker logs <container>`.",

        // PAGE 11 – REPORT, CREDITS, UPDATES
        "**REPORT, CREDITS & UPDATES**\n" +
        "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
        "**Reporting issues**\n" +
        "Use the command:\n```\n/report <description>\n```\nThe report will be sent to the bot owner privately.\n" +
        "You can also include a file (e.g., screenshot) by attaching it to the command message.\n\n" +
        "**Credits**\n" +
        "- Developed by @thevirgindev\n" +
        "- Open‑source on GitHub (link in the owner's profile)\n" +
        "- Built with Discord.Net, SixLabors.ImageSharp, and many public OSINT APIs.\n\n" +
        "**Updating the bot (for Docker users)**\n" +
        "If you run the bot via Docker, updates are automatic when the image is rebuilt.\n" +
        "The owner pushes a new image to `ghcr.io/thevirgindev/pewbot:latest`.\n" +
        "To get the latest version, run:\n" +
        "```\ndocker pull ghcr.io/thevirgindev/pewbot:latest\ndocker stop pewbot\ndocker rm pewbot\ndocker run -d --name pewbot --env-file .env ghcr.io/thevirgindev/pewbot:latest\n```\n" +
        "If you run the bot natively, pull the latest code from GitHub and rebuild with `dotnet build -c Release`.\n\n" +
        "**Enjoy the bot!**\n" +
        "Type `/guide` again to revisit any page. For emergencies, contact the owner directly."
    };

    public GuideCmd(KeyRedemptionService keyService, CooldownService cooldown, EmbedBuilderService embed)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
    }

    private async Task ShowGuidePage(int page, ulong messageId)
    {
        var embed = _embed.CreateMonochromeEmbed($"PewBot Guide – page {page + 1}/{_pages.Length}", _pages[page], "dark");
        var components = new ComponentBuilder()
            .WithButton("◀", $"guide_page:{page - 1}", ButtonStyle.Secondary, disabled: page == 0)
            .WithButton("▶", $"guide_page:{page + 1}", ButtonStyle.Secondary, disabled: page == _pages.Length - 1)
            .Build();

        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(messageId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        else
            await FollowupAsync(embed: embed, components: components);
    }

    [SlashCommand("guide", "display the comprehensive OSINT bot guide (no emojis, markdown)")]
    public async Task Guide()
    {
        if (!await _keyService.IsAuthorizedAsync(Context.User.Id.ToString()))
        {
            await RespondAsync("Redeem a master key first. Then maybe I'll talk to you.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s. Don't be so impatient.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();
        var embed = _embed.CreateMonochromeEmbed("PewBot Guide – page 1/11", _pages[0], "dark");
        var components = new ComponentBuilder()
            .WithButton("◀", "guide_page:0", ButtonStyle.Secondary, disabled: true)
            .WithButton("▶", "guide_page:1", ButtonStyle.Secondary)
            .Build();
        await FollowupAsync(embed: embed, components: components);
    }

    [ComponentInteraction("guide_page:*", ignoreGroupNames: true)]
    public async Task HandleGuidePage(string pageStr)
    {
        await DeferAsync();
        if (!int.TryParse(pageStr, out int page)) return;
        if (page < 0 || page >= _pages.Length) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await ShowGuidePage(page, smc.Message.Id);
    }
}