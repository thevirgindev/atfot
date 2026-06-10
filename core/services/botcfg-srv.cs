using System;
using DotNetEnv;

namespace atfot.core.services;

public class BotConfig
{
    public string DiscordToken { get; }
    public string OwnerDiscordId { get; }

    public BotConfig()
    {
        Env.Load();

        DiscordToken = Environment.GetEnvironmentVariable("discord_token") ?? string.Empty;
        OwnerDiscordId = Environment.GetEnvironmentVariable("owner_id") ?? string.Empty;
    }
}
