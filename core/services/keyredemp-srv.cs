using System.Threading.Tasks;
using pewbot.core.storage;

namespace pewbot.core.services;

public class KeyRedemptionService
{
    private readonly DatabaseService _db;

    public KeyRedemptionService(DatabaseService db)
    {
        _db = db;
    }

    public async Task<bool> RedeemKeyAsync(string key, string discordId)
    {
        return await _db.RedeemKeyAsync(key, discordId);
    }

    public async Task<bool> IsAuthorizedAsync(string discordId)
    {
        return await _db.IsUserAuthorizedAsync(discordId);
    }

    public async Task<string> GenerateNewKeyAsync()
    {
        return await _db.GenerateNewKeyAsync();
    }
}