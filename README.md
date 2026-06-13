# ATFOT – All The Fucking OSINT Tools

A powerful Discord bot for Open Source Intelligence (OSINT), social media footprints, Discord lookups, CLI tools, and more.

## Features

- **Social Media OSINT** – Instagram, Reddit, GitHub, Twitter, TikTok, LinkedIn, Pinterest, Facebook (carousel export, profile image)
- **Discord OSINT** – Public API, OathNet (username history, Roblox correlation)
- **CLI Tools (Docker)** – Sherlock, theHarvester, SpiderFoot, Recon-ng, Subfinder, AMASS, TorBot, OD Crawler, WhoCord, Sublist3r, WhatWeb, DNSRecon
- **Threat Intelligence** – CVE lookup, C2 feed check, malware hash check (no keys needed)
- **AI Features** – AI summary (auto-analysis), AI chatbot (`/ai chat`) using Pollinations API (free, no key)
- **Multi-key support** – Rotate API keys per service, quota tracking
- **Master key system** – Owner generates keys, users redeem to access
- **Dockerized** – All CLI tools pre-installed in the Docker image

## Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/)

### 1. Create `.env`

```env
discord_token=your_bot_token
owner_id=your_discord_id
```

### 2. Run

```bash
docker-compose up -d
```

### 3. Invite Bot

OAuth2 URL: Scope `bot` + `applications.commands`, Permissions: `Send Messages`, `Embed Links`, `Attach Files`

### 4. Generate Key

```
/genkey
```

### 5. Start Exploring

```
/guide
```

## Commands

Full documentation inside the bot via `/guide` (12 pages, paginated).

## Building from Source

```bash
git clone https://github.com/thevirgindev/atfot.git
cd atfot
dotnet build -c Release
dotnet run --no-build -c Release
```

Docker recommended for CLI tools.

## Updating

```bash
docker pull ghcr.io/thevirgindev/atfot:latest && docker-compose up -d
```

## Troubleshooting

- **Slash commands not responding** → Ensure bot was invited with `applications.commands` scope
- **CLI tool not found** → Use Docker or install tools manually
- **Image generation fails** → Check `resources/profile-lookup.jpg` and `JetBrainsMono-Bold.ttf` exist
- **Invalid master key** → Key used or doesn't exist; owner generates new with `/genkey`

## Reporting Issues

```
/report <description> [attachment]
```

## License

MIT — @thevirgindev