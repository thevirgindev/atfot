using System;
using DotNetEnv;

namespace pewbot.core.services;

public class BotConfig
{
    public string DiscordToken { get; }
    public string OwnerDiscordId { get; }

    public BotConfig()
    {
        // Load .env file from current directory
        Env.Load();

        DiscordToken = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? string.Empty;
        OwnerDiscordId = Environment.GetEnvironmentVariable("OWNER_DISCORD_ID") ?? string.Empty;
    }
}