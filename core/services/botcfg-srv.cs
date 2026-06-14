using System;
using System.Linq;
using DotNetEnv;

namespace atfot.core.services;

public class BotConfig
{
    public string DiscordToken { get; }
    public string OwnerDiscordId { get; }
    public string AllowedChannelId { get; }

    // IntelCheck.cc – arrays for multiple accounts
    public string[] IcSessions { get; }
    public string[] IcDevices { get; }
    public string[] CfClearances { get; }

    public BotConfig()
    {
        Env.Load();

        DiscordToken = Environment.GetEnvironmentVariable("discord_token") ?? string.Empty;
        OwnerDiscordId = Environment.GetEnvironmentVariable("owner_id") ?? string.Empty;
        AllowedChannelId = Environment.GetEnvironmentVariable("channel_id") ?? string.Empty;

        string icSessionRaw = Environment.GetEnvironmentVariable("ic_session") ?? string.Empty;
        string icDeviceRaw = Environment.GetEnvironmentVariable("ic_device") ?? string.Empty;
        string cfClearanceRaw = Environment.GetEnvironmentVariable("cf_clearance") ?? string.Empty;

        IcSessions = icSessionRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        IcDevices = icDeviceRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        CfClearances = cfClearanceRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

        // Validation: all arrays must have same length
        if (IcSessions.Length != IcDevices.Length || IcSessions.Length != CfClearances.Length)
        {
            throw new InvalidOperationException("IntelCheck cookie arrays (ic_session, ic_device, cf_clearance) must have the same number of entries.");
        }
    }

    public int IntelCheckAccountCount => IcSessions.Length;
}