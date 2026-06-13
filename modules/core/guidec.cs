using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using atfot.core.services;

namespace atfot.modules.core;

public class GuideCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keySvc;
    private readonly CooldownService _cd;
    private readonly EmbedBuilderService _emb;

    private static readonly string[] _pages = {
        // page 1
        "**ATFOT - ALL THE FUCKING OSINT TOOLS**\n" +
        "------------------------------------------------------------\n\n" +
        "A Discord bot for OSINT, social media, Discord lookups, and more.\n\n" +
        "**Quick Start**\n" +
        "1. Owner runs `/genkey`\n" +
        "2. You redeem: `/redeem <key>`\n" +
        "3. Type `/guide` to revisit\n\n" +
        "**Navigation**\n" +
        "Use the buttons below to navigate. 12 pages total.\n\n" +
        "Made by @thevirgindev",

        // page 2
        "**KEY MANAGEMENT**\n" +
        "------------------------------------------------------------\n\n" +
        "**Master Key**\n" +
        "`/genkey` - generate key (owner only)\n" +
        "`/redeem <key>` - activate with key\n\n" +
        "**API Keys**\n" +
        "`/setapikey <service> <key> default [quota]`\n" +
        "`/addnewkey <service> <key> [quota]`\n" +
        "`/changekey <service> <keyid>`\n" +
        "`/mykeys` - view keys (paginated)\n" +
        "`/removeapikey <service> <keyid>`\n" +
        "`/setdefaultkey <service> <keyid>`\n\n" +
        "**Services**\n" +
        "socialapi, serpapi, apify, twitter, tiktok, linkedin, pinterest, oathnet, osintcat, leakinsight, intelfetch, indicia, crowsint, peopledatalabs, ipgeolocation, onionengine, numverify\n\n" +
        "Some tools (SpiderFoot) run in Docker and don't need API keys.",

        // page 3
        "**SOCIAL MEDIA OSINT**\n" +
        "------------------------------------------------------------\n\n" +
        "`/social username <handle>`\n\n" +
        "1. Loading animation\n" +
        "2. Profile image + text overlay\n" +
        "3. Single dropdown with all platforms\n" +
        "4. Real data fetched per platform\n" +
        "5. Carousel with arrow buttons\n" +
        "6. Export (TXT/JSON)\n\n" +
        "**Platforms**\n" +
        "- Instagram: socialapi / serpapi\n" +
        "- Reddit: apify\n" +
        "- GitHub: public API (no key)\n" +
        "- Twitter: twitter api v2 / apify\n" +
        "- TikTok: rapidapi / apify\n" +
        "- LinkedIn: rapidapi / apify\n" +
        "- Pinterest: rapidapi / apify\n" +
        "- Facebook: serpapi / apify\n\n" +
        "`/social username cristiano` -> pick platform -> view data",

        // page 4
        "**DISCORD OSINT**\n" +
        "------------------------------------------------------------\n\n" +
        "`/discord lookup <userid>`\n\n" +
        "Tools used:\n" +
        "- Public API (japi.rest + fallback) - no key\n" +
        "- OathNet (username history, Roblox) - key: oathnet\n" +
        "- OsintCat, LeakInsight, IntelFetch, Indicia, CrowSint - keys required\n\n" +
        "Returns: username, global name, badges, avatar, banner, linked accounts.\n" +
        "Carousel navigation with TXT/JSON export.",

        // page 5
        "**OSINT TOOLS**\n" +
        "------------------------------------------------------------\n\n" +
        "**CLI Tools (Docker)**\n" +
        "`/osint sherlock <username>` - 400+ sites username search\n" +
        "`/osint harvester <domain>` - emails / subdomains\n" +
        "`/osint sf <target>` - SpiderFoot (Shodan, VT, AlienVault, Hunter, Censys, AbuseIPDB, etc.)\n" +
        "`/osint recon <domain>` - Recon-ng reconnaissance\n" +
        "`/osint subfinder <domain>` - passive subdomain enum\n" +
        "`/osint amass <domain>` - attack surface mapping\n" +
        "`/osint torbot <onion_url>` - crawl onion sites\n" +
        "`/osint odcrawler <username>` - username disclosure\n" +
        "`/osint whocord <type> <target>` - all-in-one OSINT\n" +
        "`/osint sublist3r <domain>` - subdomain enum\n" +
        "`/osint whatweb <target>` - website fingerprinting\n" +
        "`/osint dnsrecon <domain>` - DNS enumeration\n\n" +
        "**Threat Intelligence**\n" +
        "`/threat cve <cve-id>` - CVE lookup (no key)\n" +
        "`/threat c2 <ip>` - C2 feed check (no key)\n" +
        "`/threat malware <hash>` - hash check (no key)\n\n" +
        "**Kept Individual**\n" +
        "`/osint pdl <email>` - PeopleDataLabs\n" +
        "`/osint ipgeo <ip>` - IP geolocation\n" +
        "`/osint ipapi <ip>` - free IP geo\n" +
        "`/osint onion <keyword>` - dark web search",

        // page 6
        "**CLI TOOLS (DOCKER)**\n" +
        "------------------------------------------------------------\n\n" +
        "Bot must run in Docker for CLI tools. All pre-installed:\n" +
        "- Sherlock (username search)\n" +
        "- theHarvester (email/subdomain)\n" +
        "- SpiderFoot (aggregated OSINT)\n" +
        "- Recon-ng (web recon)\n" +
        "- Subfinder (subdomain enum)\n" +
        "- AMASS (attack surface)\n" +
        "- TorBot (onion crawl)\n" +
        "- OD Crawler (username disclosure)\n" +
        "- WhoCord (all-in-one)\n" +
        "- Sublist3r, WhatWeb, DNSRecon\n\n" +
        "Each command: loading embed, 30-120s, truncated output, export buttons.\n" +
        "Without Docker: install tools manually or use Docker image.",

        // page 7
        "**AI FEATURES**\n" +
        "------------------------------------------------------------\n\n" +
        "**AI Summary** (auto-analysis after commands)\n" +
        "`/settings set ai_summary true` to enable\n" +
        "After any OSINT command, bot sends AI analysis using Pollinations API (free, no key).\n\n" +
        "**AI Chatbot**\n" +
        "`/ai chat <message>` - chat with AI assistant (free, unlimited)\n" +
        "`/ai chat-reset` - clear conversation history\n\n" +
        "Configure system prompt:\n" +
        "`/settings set ai_chat_system_prompt your custom prompt`\n\n" +
        "Both AI features use Pollinations (free, no API key needed).",

        // page 8
        "**SETTINGS**\n" +
        "------------------------------------------------------------\n\n" +
        "`/settings show` - view current settings\n" +
        "`/settings set <key> <value>` - update\n\n" +
        "Keys:\n" +
        "- `theme` - dark, gray, white\n" +
        "- `notifications` - silent (ephemeral), public\n" +
        "- `ai_summary` - on/off (AI analysis after commands)\n" +
        "- `ai_chat_system_prompt` - custom prompt for AI chat\n" +
        "- `loading_style` - minimal, verbose\n" +
        "- `auto_collapse` - on/off\n\n" +
        "Settings persist per user across restarts.",

        // page 9
        "**ADMIN COMMANDS**\n" +
        "------------------------------------------------------------\n\n" +
        "**Owner-only**\n" +
        "`/genkey` - generate master key\n" +
        "`/redemptions` - view all redeemed keys\n" +
        "`/status <online|idle|dnd|invisible> [activity]` - bot presence\n" +
        "`/rmk <userid>` - revoke user access\n" +
        "`/db_backup` - backup database\n" +
        "`/db_restore <attachment>` - restore from backup\n" +
        "`/db_prune [days]` - delete old redemptions\n" +
        "`/db_stats` - database statistics\n\n" +
        "**All authorized users**\n" +
        "`/redeem`, `/setapikey`, `/addnewkey`, `/changekey`, `/mykeys`, `/removeapikey`, `/setdefaultkey`\n",

        // page 10
        "**FAQ / TROUBLESHOOTING**\n" +
        "------------------------------------------------------------\n\n" +
        "**Q: Slash commands not responding**\n" +
        "A: Bot needs `applications.commands` scope.\n\n" +
        "**Q: CLI tool not found**\n" +
        "A: Use Docker or install tools manually.\n\n" +
        "**Q: Invalid master key**\n" +
        "A: Key was used/doesn't exist. Owner must generate new with `/genkey`.\n\n" +
        "**Q: Rate limiting / cooldown**\n" +
        "A: Most commands have a 5s cooldown.\n\n" +
        "**Q: Image generation fails**\n" +
        "A: Check `resources/profile-lookup.jpg` and `JetBrainsMono-Bold.ttf`.\n\n" +
        "**Q: Bot is slow**\n" +
        "A: CLI tools take 30-120s. API calls depend on external services.\n\n" +
        "**Q: AI chatbot not responding**\n" +
        "A: Pollinations API must be reachable. Check network.\n\n" +
        "**Q: Settings not saving**\n" +
        "A: Must be authorized. Try `/settings show`.",

        // page 11
        "**REPORTS & UPDATES**\n" +
        "------------------------------------------------------------\n\n" +
        "**Report bugs / request features**\n" +
        "`/report <description> [attachment]`\n" +
        "Sent to bot owner privately.\n\n" +
        "**Build from source**\n" +
        "```\n" +
        "git clone https://github.com/thevirgindev/atfot.git\n" +
        "cd atfot\n" +
        "dotnet build -c Release\n" +
        "dotnet run --no-build -c Release\n" +
        "```\n\n" +
        "**Update (Docker)**\n" +
        "```bash\n" +
        "docker pull ghcr.io/thevirgindev/atfot:latest && docker-compose up -d\n" +
        "```\n\n" +
        "**Dashboard**\n" +
        "Available at `http://localhost:5173` (separate process).\n\n" +
        "Made by @thevirgindev",

        // page 12
        "**ROADMAP**\n" +
        "------------------------------------------------------------\n\n" +
        "Coming next:\n" +
        "- **Dashboard** - React + TypeScript + shadcn/ui (in progress)\n" +
        "- **More OSINT modules** - additional API integrations\n" +
        "- **Webhook support** - alerts for specific OSINT findings\n" +
        "- **Scheduled scans** - set recurring OSINT checks\n" +
        "- **Export to multiple formats** - CSV, HTML reports\n\n" +
        "Use `/report` to suggest features.\n\n" +
        "Built with Discord.Net, SixLabors.ImageSharp, Pollinations AI."
    };

    public GuideCmd(KeyRedemptionService keySvc, CooldownService cd, EmbedBuilderService emb)
    {
        _keySvc = keySvc;
        _cd = cd;
        _emb = emb;
    }

    private async Task showPage(int page, ulong msgId)
    {
        var emb = _emb.CreateMonochromeEmbed($"ATFOT Guide - page {page + 1}/{_pages.Length}", _pages[page], "dark");
        var comps = new ComponentBuilder()
            .WithButton("◀", $"guide_page:{page - 1}", ButtonStyle.Secondary, disabled: page == 0)
            .WithButton("▶", $"guide_page:{page + 1}", ButtonStyle.Secondary, disabled: page == _pages.Length - 1)
            .Build();
        var channel = Context.Channel as ISocketMessageChannel;
        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = emb; m.Components = comps; });
        else
            await FollowupAsync(embed: emb, components: comps);
    }

    [SlashCommand("guide", "display the ATFOT bot guide")]
    public async Task Guide()
    {
        if (!await _keySvc.IsAuthorizedAsync(Context.User.Id.ToString()))
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var rem))
        {
            await RespondAsync($"[WARN] wait {rem.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());
        await DeferAsync();
        var emb = _emb.CreateMonochromeEmbed("ATFOT Guide - page 1/12", _pages[0], "dark");
        var comps = new ComponentBuilder()
            .WithButton("◀", "guide_page:0", ButtonStyle.Secondary, disabled: true)
            .WithButton("▶", "guide_page:1", ButtonStyle.Secondary)
            .Build();
        await FollowupAsync(embed: emb, components: comps);
    }

    [ComponentInteraction("guide_page:*", ignoreGroupNames: true)]
    public async Task onPage(string pageStr)
    {
        await DeferAsync();
        if (!int.TryParse(pageStr, out int page)) return;
        if (page < 0 || page >= _pages.Length) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await showPage(page, smc.Message.Id);
    }
}