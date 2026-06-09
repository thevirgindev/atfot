using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace pewbot.core.services;

public class CooldownService
{
    private readonly ConcurrentDictionary<string, DateTime> _lastUsed = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromSeconds(5);

    public bool IsOnCooldown(string discordId, out TimeSpan remaining)
    {
        if (_lastUsed.TryGetValue(discordId, out var last))
        {
            var elapsed = DateTime.UtcNow - last;
            if (elapsed < _cooldown)
            {
                remaining = _cooldown - elapsed;
                return true;
            }
        }
        remaining = TimeSpan.Zero;
        return false;
    }

    public void SetUsed(string discordId)
    {
        _lastUsed[discordId] = DateTime.UtcNow;
    }
}