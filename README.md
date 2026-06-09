Here is the final `README.md` for **ATFOT** (All The Fucking OSINT Tools). Place this in the root of your repository.

```markdown
# ATFOT – All The Fucking OSINT Tools

[![Docker Build](https://github.com/YOUR_USERNAME/atfot/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/YOUR_USERNAME/atfot/actions/workflows/docker-publish.yml)

**ATFOT** is a powerful, all‑in‑one Discord bot for Open Source Intelligence (OSINT).  
It combines dozens of APIs and CLI tools into one easy‑to‑use interface.

> **Name**: ATFOT (because that’s exactly what it is – all the fucking OSINT tools)

---

## Features

- **Social Media OSINT** – Instagram, Reddit, GitHub, Twitter, TikTok, LinkedIn, Telegram, Pinterest  
  (carousel, export, loading animations)
- **Deep Discord Lookup** – public API, official API (mutual guilds), OathNet (Roblox correlation)
- **Extended OSINT Tools** – HaveIBeenPwned, AbuseIPDB, Hunter.io, PeopleDataLabs, ipgeolocation, ip‑api, OnionEngine
- **Command‑Line Tools** – Sherlock, theHarvester, SpiderFoot, Recon‑ng, Subfinder, AMASS, TorBot, OD Crawler, WhoCord (pre‑installed in Docker)
- **Infrastructure & DNS** – Shodan, VirusTotal, Censys, DNSDumpster, SecurityTrails, Ahmia (dark web)
- **Utility Commands** – geolocation, image reverse search/EXIF, keyword trends, maritime, vehicle, news, fact‑check, World Bank data, change detection, privacy tools, CyberChef, WiGLE
- **Multi‑key support** – add multiple API keys per service, set default, remove, list
- **Master key system** – owner generates keys, users redeem to access the bot
- **Fully containerised** – runs anywhere with Docker, all CLI tools pre‑installed

---

## Quick Start (Docker)

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) installed on your system

### 1. Create a `.env` file

```env
discord_token=your_bot_token_here
owner_id=your_discord_user_id_here
```

### 2. Run the bot

```bash
docker run -d --name atfot --env-file .env ghcr.io/YOUR_USERNAME/atfot:latest
```

Or using docker‑compose (recommended):

```bash
git clone https://github.com/YOUR_USERNAME/atfot.git
cd atfot
cp .env.example .env
# edit .env with your token and owner ID
docker-compose up -d
```

### 3. Invite the bot to your server

Use the OAuth2 URL generator in the [Discord Developer Portal](https://discord.com/developers/applications).  
Scopes: `bot` + `applications.commands`  
Permissions: `Send Messages`, `Embed Links`, `Attach Files`, `Use Slash Commands`

### 4. Generate your first master key (owner only)

```
/genkey
```

Share the key with other users. They run `/redeem <key>`.

### 5. Add your own API keys (optional)

```
/setapikey shodan your_shodan_key --default
/setapikey hunter your_hunter_key
/mykeys
```

### 6. Start exploring

Type `/guide` for the full interactive manual (11 pages).

---

## Updating

When a new version is released:

```bash
docker pull ghcr.io/YOUR_USERNAME/atfot:latest
docker stop atfot
docker rm atfot
docker run -d --name atfot --env-file .env ghcr.io/YOUR_USERNAME/atfot:latest
```

Or with docker‑compose:

```bash
docker-compose pull
docker-compose up -d
```

---

## Building from source (without Docker)

**Prerequisites**  
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)  
- Python 3.9+ and pip (for CLI tools)  
- Go (for Subfinder and AMASS)

**Install CLI tools manually** (or use Docker to avoid this):

```bash
pip install sherlock-project theHarvester spiderfoot recon-ng torbot od-crawler whocord
go install github.com/subfinder/subfinder/v2/cmd/subfinder@latest
go install github.com/owasp-amass/amass/v4/...@master
```

**Run**:

```bash
git clone https://github.com/YOUR_USERNAME/atfot.git
cd atfot
dotnet build -c Release
dotnet run --no-build -c Release
```

But Docker is **strongly recommended** – it includes all dependencies.

---

## Troubleshooting

- **Bot doesn't respond to slash commands** – Ensure you invited the bot with `applications.commands` scope. Slash commands may take a few minutes to register.
- **`/discord lookup` shows "mutual guild required"** – Use the public API tool (first in the carousel) instead of the official API.
- **CLI tools not found** – You are not using Docker. Either switch to Docker or install the tools manually.
- **Image generation fails** – Make sure `resources/profile-lookup.jpg` and `JetBrainsMono-Bold.ttf` exist in the mounted directory (or in the container). The default image includes placeholders; you can replace them.
- **"Invalid or already used master key"** – The key was already redeemed or doesn't exist. Owner must generate a new one with `/genkey`.

---

## Commands

Full documentation is inside the bot: `/guide` (paginated, with FAQ and troubleshooting).  
For a quick list, use `/help`.

---

## Reporting Issues

Use `/report <description>` – your report will be sent directly to the bot owner.

---

## License

MIT – made by [@thevirgindev](https://github.com/thevirgindev)

---

## Credits
- me (the guy behind this project)
- All the amazing OSINT tools and APIs (Sherlock, theHarvester, OsintCat, etc..)
- The open‑source community

---
