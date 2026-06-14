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
        "A powerful all-in-one Discord bot for open source intelligence, social media footprint analysis, deep Discord user lookups, CLI tool integration, and AI-powered analysis.\n\n" +
        "**What ATFOT does:**\n" +
        "- Search social media platforms (Instagram, Reddit, GitHub, Twitter/X, TikTok, LinkedIn, Pinterest, Facebook) via API integrations\n" +
        "- Deep Discord user OSINT using public and authenticated APIs\n" +
        "- Run powerful CLI tools (Sherlock, SpiderFoot, theHarvester, Recon-ng, Subfinder, AMASS, TorBot, WhoCord, Sublist3r, etc.) directly from Discord\n" +
        "- Threat intelligence (CVE lookup, C2 feed checking, malware hash analysis)\n" +
        "- AI-powered summaries after any OSINT command (requires `pollinations` API key)\n" +
        "- Free unlimited AI chatbot (responds to normal messages when enabled; requires `pollinations` API key)\n" +
        "- Dashboard (React+TS, in progress)\n\n" +
        "**Getting started:**\n" +
        "1. The bot owner runs `/genkey` to generate a master key\n" +
        "2. Share the key with a trusted user who runs `/redeem <key>`\n" +
        "3. After redemption, all commands become available\n" +
        "4. To use AI features, set a Pollinations key: `/setapikey pollinations <key> default`\n" +
        "5. Type `/guide` to revisit this guide\n" +
        "6. Type `/settings show` to check and configure preferences\n\n" +
        "**How navigation works:**\n" +
        "Use the buttons below to flip between pages (11 pages).\n\n" +
        "Made by @thevirgindev — fully automated, fully containerized.",

        // page 2
        "**KEY MANAGEMENT**\n" +
        "------------------------------------------------------------\n\n" +
        "**Master Key System**\n" +
        "The bot owner generates a master key that users must redeem to access the bot. This ensures only trusted users can use the bot.\n\n" +
        "`/genkey` — generate a new master key (owner only)\n" +
        "`/redeem <key>` — redeem a master key to activate access\n" +
        "`/rmk <userid>` — revoke a user's access (owner only)\n" +
        "`/redemptions` — view all key redemptions (owner only)\n\n" +
        "**API Key Management**\n" +
        "External OSINT services require API keys. You manage keys per service:\n\n" +
        "`/setapikey <service> <key> default [quota]` — add/set as default\n" +
        "`/addnewkey <service> <key> [quota]` — add an additional key\n" +
        "`/changekey <service> <keyid>` — change the default key by ID\n" +
        "`/mykeys` — view all your API keys (paginated, masked)\n" +
        "`/removeapikey <service> <keyid>` — remove a specific key\n" +
        "`/setdefaultkey <service> <keyid>` — set an existing key as default\n\n" +
        "**Supported services:**\n" +
        "`socialapi`, `serpapi`, `apify`, `twitter`, `tiktok`, `linkedin`, `pinterest`, `oathnet`, `osintcat`, `leakinsight`, `intelfetch`, `indicia`, `crowsint`, `peopledatalabs`, `ipgeolocation`, `onionengine`, `numverify`, `pollinations`\n\n" +
        "**Note:** Some tools (SpiderFoot, Sherlock, theHarvester, etc.) run in Docker and do NOT require API keys.",

        // page 3
        "**SOCIAL MEDIA OSINT**\n" +
        "------------------------------------------------------------\n\n" +
        "**Command:**\n" +
        "```\n/social username <handle>\n```\n\n" +
        "Enter the username you want to investigate across social media platforms. Select a platform from the dropdown to gather public data, social footprints, and associated accounts across the selected service.\n\n" +
        "**What happens:**\n" +
        "1. Loading animation with status markers ([INFO], [DONE])\n" +
        "2. Profile image + text overlay with user and platforms\n" +
        "3. Single dropdown menu with all 8 platforms\n" +
        "4. Real data fetched per platform using multiple API sources\n" +
        "5. Carousel with arrow buttons to browse tool results\n" +
        "6. Export buttons (TXT/JSON) for each result\n\n" +
        "**Platforms and sources:**\n" +
        "- Instagram: socialapi + serpapi\n" +
        "- Reddit: apify\n" +
        "- GitHub: public API (no key) + apify\n" +
        "- Twitter: twitter api v2 + apify\n" +
        "- TikTok: rapidapi + apify\n" +
        "- LinkedIn: rapidapi + apify\n" +
        "- Pinterest: rapidapi + apify\n" +
        "- Facebook: serpapi + apify\n\n" +
        "Example: `/social username cristiano` → pick platform → view data",

        // page 4
        "**DISCORD USER OSINT**\n" +
        "------------------------------------------------------------\n\n" +
        "**Command:**\n" +
        "```\n/discord lookup <userid>\n```\n\n" +
        "The Discord lookup command runs multiple OSINT tools against a Discord user ID and presents each tool's output in a navigable carousel.\n\n" +
        "**Tools available (carousel-based):**\n" +
        "- **Public API** (`japi.rest` + fallback `discordlookup.com`) — no API key required\n" +
        "- **OathNet** — requires key (service: `oathnet`). Username history, Roblox correlation\n" +
        "- **OsintCat** — requires key (service: `osintcat`). Additional username lookups\n" +
        "- **LeakInsight** — requires key (service: `leakinsight`). Breach checks\n" +
        "- **IntelFetch** — requires key (service: `intelfetch`). Extended Discord metadata\n" +
        "- **Indicia** — requires key (service: `indicia`). Cross-referencing\n" +
        "- **CrowSint** — requires key (service: `crowsint`). Aggregated OSINT\n\n" +
        "**Navigation:**\n" +
        "Carousel arrows (◀ ▶) to browse, export buttons (txt/json), back to profile.\n\n" +
        "Returns: username, global name, badges, avatar, banner, linked accounts.",

        // page 5
        "**OSINT TOOLS & THREAT INTEL**\n" +
        "------------------------------------------------------------\n\n" +
        "**API-based OSINT tools** (require specific API keys):\n" +
        "`/osint pdl <email>` — PeopleDataLabs: person lookup (service: `peopledatalabs`)\n" +
        "`/osint ipgeo <ip>` — IP geolocation (service: `ipgeolocation`)\n" +
        "`/osint ipapi <ip>` — free IP geo (no key needed)\n" +
        "`/osint onion <keyword>` — dark web search (service: `onionengine`)\n\n" +
        "**Threat Intelligence** (all free, no API key required):\n" +
        "- `/threat cve <cve-id>` — CVE lookup\n" +
        "- `/threat c2 <ip>` — C2 feed check\n" +
        "- `/threat malware <hash>` — hash check\n\n" +
        "**How these differ from CLI tools:**\n" +
        "API-based tools make direct HTTP calls and return results instantly. CLI tools (page 6) run in Docker and take 30-120s.",

        // page 6
        "**CLI TOOLS (DOCKER)**\n" +
        "------------------------------------------------------------\n\n" +
        "Bot must run in Docker for CLI tools. All tools are pre-installed.\n\n" +
        "**Available tools:**\n" +
        "- `/osint sherlock <username>` — 400+ sites username search\n" +
        "- `/osint harvester <domain>` — emails/subdomains\n" +
        "- `/osint sf <target>` — SpiderFoot\n" +
        "- `/osint recon <domain>` — Recon-ng\n" +
        "- `/osint subfinder <domain>` — subdomain enumeration\n" +
        "- `/osint amass <domain>` — attack surface mapping\n" +
        "- `/osint torbot <onion_url>` — onion crawl\n" +
        "- `/osint odcrawler <username>` — username disclosure\n" +
        "- `/osint whocord <type> <target>` — all-in-one OSINT\n" +
        "- `/osint sublist3r <domain>` — subdomain enum\n" +
        "- `/osint whatweb <target>` — website fingerprinting\n" +
        "- `/osint dnsrecon <domain>` — DNS enumeration\n\n" +
        "Each command runs inside Docker, returns parsed output with TXT/JSON export. Expect 30-120s.",

        // page 7
        "**AI FEATURES**\n" +
        "------------------------------------------------------------\n\n" +
        "**Setup:** AI features require a Pollinations API key. Get one at enter.pollinations.ai, then:\n" +
        "```\n/setapikey pollinations <your-key> default\n```\n\n" +
        "**AI Summary** (auto-analysis after commands)\n" +
        "`/settings set ai_summary on` — enables AI analysis after OSINT commands\n" +
        "`/settings set ai_summary off` — disables it\n\n" +
        "**AI Chatbot**\n" +
        "Direct Chat — send messages directly to the bot in DMs or mention the bot in channels.\n" +
        "`/chat-reset` — clear conversation history and memory\n" +
        "`/settings set ai_chat true` — bot responds to all your normal messages in channels\n" +
        "`/settings set ai_chat false` — only responds in DMs/mentions\n\n" +
        "**Custom system prompt:**\n" +
        "`/settings set system_prompt your prompt here`\n\n" +
        "Both features use Pollinations API via gen.pollinations.ai.",

        // page 8
        "**SETTINGS**\n" +
        "------------------------------------------------------------\n\n" +
        "`/settings show` — view current settings\n" +
        "`/settings set <key> <value>` — update a setting\n\n" +
        "**Available settings:**\n" +
        "- `theme` — `dark`, `gray`, `white`\n" +
        "- `notifications` — `silent` (ephemeral) or `public`\n" +
        "- `ai_summary` — `on`/`off` (AI analysis after OSINT commands)\n" +
        "- `ai_chat` — `true`/`false` (bot responds to normal messages with AI)\n" +
        "- `system_prompt` — custom prompt for AI chatbot\n\n" +
        "**Examples:**\n" +
        "```\n/settings set theme dark\n/settings set notifications silent\n/settings set ai_summary on\n/settings set ai_chat true\n/settings set system_prompt you are an osint analyst...\n```\n\n" +
        "Settings persist per user across restarts.",

        // page 9
        "**ADMIN COMMANDS**\n" +
        "------------------------------------------------------------\n\n" +
        "**Key management (owner only):**\n" +
        "`/genkey` — generate new master key\n" +
        "`/rmk <userid>` — revoke user access\n" +
        "`/redemptions` — view all redemptions\n\n" +
        "**Bot presence (owner only):**\n" +
        "`/status <status> <activity>` — change bot status\n\n" +
        "**Server management:**\n" +
        "`/db_backup` — backup database\n" +
        "`/db_restore` — restore from backup\n" +
        "`/db_prune` — prune old redemptions\n" +
        "`/db_stats` — database statistics\n\n" +
        "API keys are stored per user, encrypted at rest.",

        // page 10
        "**FAQ & TROUBLESHOOTING**\n" +
        "------------------------------------------------------------\n\n" +
        "**Q: AI features don't work?**\n" +
        "A: Set a Pollinations API key: `/setapikey pollinations <key> default`. Get one at enter.pollinations.ai\n\n" +
        "**Q: Bot doesn't respond to commands**\n" +
        "A: Redeem a master key first with `/redeem <key>`.\n\n" +
        "**Q: CLI tools return nothing**\n" +
        "A: Bot must run in Docker for CLI tools.\n\n" +
        "**Q: API key errors**\n" +
        "A: Verify your keys with `/mykeys`. Some services have daily quotas.\n\n" +
        "**Q: How do I get API keys?**\n" +
        "A: Visit SerpAPI, Apify, SocialAPI, enter.pollinations.ai, etc. for free tiers. Set them with `/setapikey`.\n\n" +
        "**Q: Bot is slow**\n" +
        "A: API tools are fast (2-5s). CLI tools in Docker take 30-120s.",

        // page 11
        "**UPDATES & ROADMAP**\n" +
        "------------------------------------------------------------\n\n" +
        "**Current version:** v1.0 (Docker)\n\n" +
        "**Recent features:**\n" +
        "- Unified social media carousel (8 platforms)\n" +
        "- AI-powered chatbot and command summaries (Pollinations API)\n" +
        "- Settings system (theme, notifications, AI config)\n" +
        "- Dashboard (React+TS) in progress\n\n" +
        "**Planned:**\n" +
        "- Dashboard: stats, redemptions, API usage\n" +
        "- More OSINT tool integrations\n" +
        "- Batch scanning\n" +
        "- Custom report generation\n\n" +
        "**Support:**\n" +
        "github.com/thevirgindev/atfot\n\n" +
        "Made with ❤️ by @thevirgindev"
    };

    public GuideCmd(KeyRedemptionService keySvc, CooldownService cd, EmbedBuilderService emb)
    {
        _keySvc = keySvc;
        _cd = cd;
        _emb = emb;
    }

    [SlashCommand("guide", "show the ATFOT guide")]
    public async Task ShowGuide()
    {
        if (!await _keySvc.IsAuthorizedAsync(Context.User.Id.ToString()))
        {
            await RespondAsync("[ERR] redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cd.IsOnCooldown(Context.User.Id.ToString(), out var _))
        {
            await RespondAsync("[WAIT] wait a bit.", ephemeral: true);
            return;
        }
        _cd.SetUsed(Context.User.Id.ToString());
        await SendPage(0);
    }

    private async Task SendPage(int idx)
    {
        if (idx < 0 || idx >= _pages.Length) return;
        var embed = _emb.CreateMonochromeEmbed(
            $"📖 Guide ({idx + 1}/{_pages.Length})",
            _pages[idx],
            "dark");
        var comps = new ComponentBuilder()
            .WithButton("◀", $"gp:{Context.User.Id}:{idx - 1}", ButtonStyle.Secondary, disabled: idx == 0)
            .WithButton("▶", $"gp:{Context.User.Id}:{idx + 1}", ButtonStyle.Secondary, disabled: idx == _pages.Length - 1)
            .Build();
        if (idx == 0 && !Context.Interaction.HasResponded)
            await RespondAsync(embed: embed, components: comps);
        else if (Context.Interaction is SocketMessageComponent mc)
            await mc.UpdateAsync(msg => { msg.Embed = embed; msg.Components = comps; });
        else
            await RespondAsync(embed: embed, components: comps);
    }

    [ComponentInteraction("gp:*:*", ignoreGroupNames: true)]
    public async Task OnPage(string userIdStr, string idxStr)
    {
        if (!ulong.TryParse(userIdStr, out var uid) || uid != Context.User.Id) return;
        if (!int.TryParse(idxStr, out var idx)) return;
        await SendPage(idx);
    }
}