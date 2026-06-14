# ATFOT — All The Fucking OSINT Tools

A self-hosted Discord bot for open source intelligence operations. Consolidates social media profiling, Discord user analysis, CLI-based OSINT tools, threat intelligence, network mapping, geolocation, image forensics, and an AI assistant into a single containerized application controlled entirely through Discord slash commands.

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Deployment](#deployment)
- [Configuration](#configuration)
- [Command Reference](#command-reference)
- [AI System](#ai-system)
- [API Keys](#api-keys)
- [Building from Source](#building-from-source)
- [Updating](#updating)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Features

**Social Media Intelligence**
Search and correlate accounts across Instagram, Reddit, GitHub, Twitter/X, TikTok, LinkedIn, Pinterest, and Facebook. Results are returned as paginated embeds with export to JSON or TXT.

**Discord User OSINT**
Deep profile analysis using the Discord public API combined with third-party providers (OathNet, OsintCat, LeakInsight, IntelFetch, Indicia, CrowSint). Extracts username history, Roblox account links, badge data, and account creation timestamps. Results are displayed in a carousel with per-tool export.

**CLI Tool Integration**
Run professional OSINT tools directly from Discord. All tools are pre-installed inside the Docker image and executed as subprocess calls with output streamed back to the channel.

| Tool | Purpose |
|---|---|
| Sherlock | Username search across 400+ platforms |
| theHarvester | Email and subdomain harvesting |
| SpiderFoot | Full OSINT aggregation framework |
| Recon-ng | Web reconnaissance framework |
| Subfinder | Passive subdomain enumeration |
| AMASS | Attack surface mapping |
| TorBot | Dark web onion site crawler |
| WhoCord | Comprehensive Discord OSINT |
| Sublist3r | Subdomain enumeration via OSINT sources |
| WhatWeb | Website technology fingerprinting |
| DNSRecon | DNS enumeration and zone analysis |
| Maigret | Username OSINT across 3000+ sites |
| Photon | Fast web crawler for OSINT data |
| Holehe | Email to account registration checker |

**Threat Intelligence**
CVE vulnerability lookups via NVD, C2 feed checking via Abuse.ch Feodo Tracker, and malware hash analysis via MalwareBazaar. No API keys required for any threat module.

**Network and Infrastructure**
Infrastructure investigation (`/infra investigate`) performs routing audit, ASN lookup, WHOIS, and reputation checks. DNS history and subdomain enumeration (`/infra dns`) pulls from passive DNS sources.

**Contact Intelligence**
Email and phone number investigation (`/contact lookup`) queries communication vector databases.

**Geolocation**
Address and coordinate resolution with links to Google Maps, OpenStreetMap, and SunCalc solar data (`/geo locate`).

**Image Forensics**
EXIF metadata extraction from any image URL (`/image metadata`). Extracts GPS coordinates, device information, timestamps, and camera settings.

**AI Assistant**
Persistent AI chatbot with long-term user memory backed by SQLite. Responds to direct messages, bot mentions, and optionally all channel messages. Uses Pollinations AI (free, no registration required).

**AI-Powered OSINT Summaries**
Automatically generates structured threat intelligence summaries after any OSINT tool result when enabled. Outputs threat indicators, subject profiles, reasoning, and follow-up recommendations.

**Key and Access Management**
Master key system: the bot owner generates time-limited access keys that users redeem to unlock all commands. Multi-key API key management with quota tracking, rate limit detection, and key rotation per service.

**Data Export**
All OSINT results can be exported as structured JSON or formatted plain text directly from Discord via button interactions. Includes custom export formatting per module.

---

## Architecture

```
atfot/
  Program.cs                    — DI setup, bot startup
  core/
    services/                   — AiChat, AiSummary, ApiKey, BotConfig,
                                  Cooldown, EmbedBuilder, Export, Image,
                                  KeyRedemption, Settings, SocialMedia
    storage/
      database-srv.cs           — SQLite: users, keys, settings, chat history, memory
  handlers/
    interaction-handler.cs      — Slash command router + MessageReceived AI handler
  modules/
    core/                       — admin, guide, settings, report, ai (chat-reset)
    osint/                      — discord lookup, OSINT tools, social media, threat, contact
    network/                    — infra (routing, DNS)
    utilities/                  — geo, image, data-statistics, misc, sarcasm
  dashboard/                    — React+TypeScript web dashboard (in progress)
```

**Runtime stack:**
- .NET 10 (C#)
- Discord.Net 3.19 (slash commands via InteractionService)
- SQLite via Microsoft.Data.Sqlite
- Serilog for structured logging
- Pollinations AI (`gen.pollinations.ai/v1/chat/completions`) for AI features
- Docker: `mcr.microsoft.com/dotnet/runtime:10.0` base with Python 3, Go, and all OSINT CLI tools pre-installed

---

## Requirements

- Docker and Docker Compose
- A Discord bot token (from [discord.com/developers](https://discord.com/developers/applications))
- Your Discord user ID (owner ID)

No other dependencies are required on the host. Everything runs inside the container.

---

## Deployment

### 1. Clone the repository

```bash
git clone https://github.com/thevirgindev/atfot.git
cd atfot
```

### 2. Create the environment file

```bash
cp .env.example .env
```

Edit `.env`:

```env
discord_token=your_bot_token_here
owner_id=your_discord_user_id_here

# Optional: IntelCheck.cc session cookies (for contact lookups)
ic_session=
ic_device=
cf_clearance=
```

### 3. Pull and start

```bash
docker pull ghcr.io/thevirgindev/atfot:latest
docker-compose up -d
```

The bot will connect to Discord, register all slash commands globally, and begin accepting requests within a few seconds.

### 4. Invite the bot

Generate an OAuth2 URL from the Discord Developer Portal with the following settings:

- Scopes: `bot`, `applications.commands`
- Bot permissions: `Send Messages`, `Embed Links`, `Attach Files`, `Read Message History`

### 5. Generate the first access key

In any Discord channel or DM with the bot:

```
/genkey
```

The key will be sent to you ephemerally. Share it with users who should have access.

### 6. Redeem the key

```
/redeem <key>
```

After redemption, all commands become available.

### 7. Review the guide

```
/guide
```

The bot contains a full 11-page interactive guide covering every feature.

---

## Configuration

### User Settings

Managed via `/settings show` and `/settings set <key> <value>`:

| Key | Values | Description |
|---|---|---|
| `theme` | `dark`, `light` | Embed color theme |
| `notifications` | `public`, `private` | Response visibility preference |
| `ai_summary` | `true`, `false` | Enable AI summaries on OSINT results |
| `ai_chat` | `true`, `false` | Enable AI responses to all channel messages |
| `system_prompt` | any string | Custom AI personality / instructions |

### Volumes

| Path | Purpose |
|---|---|
| `atfot-data:/app/data` | SQLite database (persistent) |
| `./logs:/app/logs` | Serilog log files |
| `./resources:/app/resources` | Static assets (fonts, images) |

---

## Command Reference

### Access Management

| Command | Description | Access |
|---|---|---|
| `/redeem <key>` | Activate access with a master key | All |
| `/genkey` | Generate a new master key | Owner |
| `/rmk <userid>` | Revoke a user's access | Owner |
| `/redemptions` | View all key redemption records | Owner |

### API Key Management

| Command | Description |
|---|---|
| `/setapikey <service> <key> <default>` | Add an API key for a service |
| `/addnewkey <service> <key>` | Add an additional key without changing default |
| `/removeapikey <id>` | Remove an API key by its numeric ID |
| `/setdefaultkey <service> <id>` | Set a key as default for its service |
| `/changekey <service> <id>` | Change the active default key |
| `/mykeys` | List all your API keys (masked) |

### Social Media — `/social`

| Command | Description |
|---|---|
| `/social username <username>` | Search username across Instagram, Reddit, GitHub, Twitter/X, TikTok, LinkedIn, Pinterest, Facebook |

Results include profile image, bio, follower count, and linked accounts where available. Returned as a carousel with JSON/TXT export.

### Discord OSINT — `/discord`

| Command | Description |
|---|---|
| `/discord lookup <userid>` | Deep profile analysis using public API, OathNet, and additional providers |

Returns account metadata, username history, Roblox correlations, and linked accounts. AI summary applied per-result if enabled.

### OSINT Tools — `/osint`

| Command | Description | Key Required |
|---|---|---|
| `/osint pdl <email>` | Person enrichment via PeopleDataLabs | Yes (pdl) |
| `/osint ipgeo <ip>` | IP geolocation via ipgeolocation.io | Yes (ipgeolocation) |
| `/osint ipapi <ip>` | IP geolocation (free, no key) | No |
| `/osint onion <query>` | Dark web search via OnionEngine | Yes (onionengine) |
| `/osint sf <target>` | SpiderFoot OSINT aggregation | No (CLI) |
| `/osint sherlock <username>` | Username search across 400+ sites | No (CLI) |
| `/osint harvester <domain>` | Email and subdomain harvesting | No (CLI) |
| `/osint recon <target>` | Web reconnaissance via Recon-ng | No (CLI) |
| `/osint subfinder <domain>` | Passive subdomain enumeration | No (CLI) |
| `/osint amass <domain>` | Attack surface mapping | No (CLI) |
| `/osint torbot <url>` | Onion site crawler | No (CLI) |
| `/osint odcrawler <username>` | Username disclosure crawl | No (CLI) |
| `/osint whocord <userid>` | Comprehensive Discord OSINT | No (CLI) |
| `/osint sublist3r <domain>` | Subdomain enumeration via OSINT | No (CLI) |
| `/osint whatweb <url>` | Website technology fingerprinting | No (CLI) |
| `/osint dnsrecon <domain>` | DNS enumeration | No (CLI) |

### Contact Intelligence — `/contact`

| Command | Description |
|---|---|
| `/contact lookup <email or phone>` | Query email/phone across communication databases |

### Threat Intelligence — `/threat`

| Command | Description |
|---|---|
| `/threat cve <CVE-ID>` | CVE lookup from NVD (free) |
| `/threat c2 <ip>` | C2 feed check via Abuse.ch Feodo Tracker (free) |
| `/threat malware <sha256>` | Malware hash lookup via MalwareBazaar (free) |

### Infrastructure and Network — `/infra`

| Command | Description |
|---|---|
| `/infra investigate <domain or ip>` | Routing audit, ASN, WHOIS, reputation check |
| `/infra dns <domain>` | DNS history and subdomain enumeration |

### Geolocation — `/geo`

| Command | Description |
|---|---|
| `/geo locate <address or coordinates>` | Map links, coordinate resolution, SunCalc data |

### Image Forensics — `/image`

| Command | Description |
|---|---|
| `/image metadata <url>` | EXIF extraction: GPS, device, timestamps, camera settings |

### Data and Statistics — `/data`

| Command | Description |
|---|---|
| `/data worldbank <query>` | Query World Bank open development data |

### Miscellaneous — `/misc`

| Command | Description |
|---|---|
| `/misc cyberchef <operation> <input>` | Encoding and hashing (Base64, SHA256, MD5, URL, hex, etc.) |

### Settings — `/settings`

| Command | Description |
|---|---|
| `/settings show` | View your current settings |
| `/settings set <key> <value>` | Update a setting |

### AI

| Command | Description |
|---|---|
| `/chat-reset` | Clear your AI conversation history and memory |

### System (Owner)

| Command | Description |
|---|---|
| `/status <type> <text>` | Change bot presence |
| `/db_backup` | Download SQLite database backup |
| `/db_restore` | Restore database from backup file |
| `/db_prune <days>` | Delete redemption records older than N days |
| `/db_stats` | Show database statistics |

### General

| Command | Description |
|---|---|
| `/guide` | Open the interactive 11-page guide |
| `/report <description>` | Send a bug report or feature request to the bot owner |

---

## AI System

ATFOT includes two independent AI features powered by the Pollinations API (`gen.pollinations.ai`). A Pollinations API key is not required but can be set to bypass rate limits.

### AI Chatbot

The bot responds to messages in three modes:

- **Direct message** — any DM to the bot triggers the AI
- **Bot mention** — `@ATFOT your message` in any channel
- **Channel mode** — enable with `/settings set ai_chat true` to respond to all messages in a channel

**Memory system:** The AI automatically extracts and persists permanent facts about you (name, preferences, targets) to SQLite. These are injected into every subsequent conversation as context. Memory is cleared with `/chat-reset`.

**Custom personality:** Set your own system prompt via `/settings set system_prompt <your instructions>`. If left empty, a default OSINT-focused assistant prompt is used.

**Conversation history:** The last 20 messages per user are stored in SQLite and included in every request. History persists across bot restarts.

### AI OSINT Summaries

When `ai_summary` is enabled (`/settings set ai_summary true`), every OSINT tool result is automatically analyzed by the AI and replaced with a structured intelligence report containing:

- Threat indicators (IPs, domains, hashes, emails)
- Subject profile with confidence level
- Cross-referenced reasoning
- Recommended next commands

---

## API Keys

Some features require API keys from third-party services. Keys are stored encrypted per user in SQLite and never shared.

| Service | Used For | Free Tier |
|---|---|---|
| `pollinations` | AI chat and summaries | Yes (unlimited) |
| `pdl` | Person enrichment via PeopleDataLabs | No |
| `ipgeolocation` | Precise IP geolocation | Limited |
| `onionengine` | Dark web search | Limited |
| `oathnet` | Discord username history, Roblox lookup | Requires account |
| `osintcat` | Extended Discord OSINT (coming soon) | — |
| `leakinsight` | Breach data lookups (coming soon) | — |
| `intelfetch` | AI-powered OSINT (coming soon) | — |
| `indicia` | Digital footprint correlation (coming soon) | — |
| `crowsint` | Cross-platform correlation (coming soon) | — |

Add a key:
```
/setapikey <service> <your-key> default
```

---

## Building from Source

Docker is strongly recommended. Building from source gives you only the .NET binary without any of the CLI OSINT tools.

```bash
git clone https://github.com/thevirgindev/atfot.git
cd atfot
dotnet build -c Release
dotnet run -c Release
```

To build the Docker image locally instead of pulling from GHCR:

```bash
docker build -t atfot:local .
```

Then update `docker-compose.yml` to use `image: atfot:local`.

**Why Docker is required for CLI tools:**
The OSINT CLI tools (Sherlock, SpiderFoot, theHarvester, etc.) are Python and Go binaries installed during the Docker image build. They do not exist on your host unless manually installed. Running outside Docker means all `/osint cli` commands will fail.

---

## Updating

```bash
docker pull ghcr.io/thevirgindev/atfot:latest
docker-compose up -d
```

The database (`atfot-data` volume) persists between updates. All user keys, settings, chat history, and memory are preserved.

---

## Troubleshooting

**Slash commands not appearing**
Ensure the bot was invited with the `applications.commands` scope. Global command registration can take up to one hour on first deployment.

**CLI tool returns "command not found"**
You are running outside Docker. Pull the image and use `docker-compose up -d`.

**AI returns nothing or times out**
The Pollinations API has public rate limits. Set a Pollinations API key via `/setapikey pollinations <key> default` to get a higher limit, or wait and retry.

**Image generation fails in social media commands**
Verify that `resources/profile-lookup.jpg` and `resources/JetBrainsMono-Bold.ttf` exist inside the container (mapped via the `./resources:/app/resources` volume).

**Master key is invalid**
The key has already been used or does not exist. The owner must generate a new one with `/genkey`.

**Bot does not respond to messages (AI chat)**
Ensure `ai_chat` is enabled via `/settings set ai_chat true`, or use DMs or bot mentions instead.

**Database errors on startup**
The bot performs automatic schema migrations on every start. If the `atfot-data` volume is corrupted, restore from a `/db_backup` or delete the volume to start fresh (all user data will be lost).

---

## Reporting Issues

```
/report <description>
```

Reports are sent directly to the bot owner as a Discord DM with an optional attachment.

Alternatively, open a GitHub issue at [github.com/thevirgindev/atfot](https://github.com/thevirgindev/atfot/issues).

---

## License

MIT License — built and maintained by @thevirgindev.