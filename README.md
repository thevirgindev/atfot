# PewBot – Ultimate OSINT Discord Bot

A powerful, all‑in‑one Discord bot for Open Source Intelligence (OSINT).  
Scrape social media, investigate Discord users, check data breaches, run CLI OSINT tools, map networks, and more.

## Features

- **Social Media OSINT** – Instagram, Reddit, GitHub, Twitter, TikTok, LinkedIn, Telegram, Pinterest (carousel, export)
- **Deep Discord Lookup** – Public API, official API (mutual guilds), OathNet (Roblox correlation)
- **Extended OSINT Tools** – HaveIBeenPwned, AbuseIPDB, Hunter.io, PeopleDataLabs, ipgeolocation, ip-api, OnionEngine, Sherlock, theHarvester, SpiderFoot, Recon‑ng, Subfinder, AMASS, TorBot, OD Crawler, WhoCord
- **Infrastructure & DNS** – Shodan, VirusTotal, Censys, DNSDumpster, SecurityTrails, dark web Ahmia
- **Utility Commands** – Geolocation, image reverse search/EXIF, keyword trends, maritime, vehicle, news, fact‑check, World Bank data, change detection, privacy tools, CyberChef, WiGLE
- **Multi‑key support** – Add multiple API keys per service, set default, remove, list
- **Master key system** – Owner generates keys, users redeem to access the bot
- **Fully containerised** – Run with Docker (all CLI tools pre‑installed)

---

## Quick Start (Docker)

1. **Install Docker**  
   - [Windows / macOS](https://docs.docker.com/get-docker/)  
   - Linux: `sudo apt install docker.io docker-compose`

2. **Clone the repository** (or just use the Docker image directly)

   ```bash
   git clone https://github.com/YOUR_USERNAME/PewBot.git
   cd PewBot