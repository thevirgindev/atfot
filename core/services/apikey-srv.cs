using System.Collections.Generic;
using System.Threading.Tasks;
using atfot.core.storage;

namespace atfot.core.services;

public class ApiKeyService
{
    private readonly DatabaseService _db;

    public ApiKeyService(DatabaseService db) => _db = db;

    public async Task<int> AddApiKeyAsync(string discordId, string service, string apiKey, bool isDefault = false, int totalQuota = -1)
        => await _db.AddApiKeyAsync(discordId, service, apiKey, isDefault, totalQuota);

    public async Task<bool> RemoveApiKeyAsync(string discordId, int keyId)
        => await _db.RemoveApiKeyAsync(discordId, keyId);

    public async Task<bool> SetDefaultKeyAsync(string discordId, string service, int keyId)
        => await _db.SetDefaultKeyAsync(discordId, service, keyId);

    public async Task<Dictionary<string, List<(int id, string maskedKey, bool isDefault)>>> GetAllUserKeysGroupedAsync(string discordId)
        => await _db.GetAllUserKeysGroupedAsync(discordId);

    public async Task<(int keyId, string apiKey)?> GetNextAvailableKeyAsync(string discordId, string service)
    {
        var keys = await _db.GetAllActiveKeysAsync(discordId, service);
        return keys.Count == 0 ? null : (keys[0].id, keys[0].apiKey);
    }

    public async Task IncrementUsageAsync(int keyId, string discordId) => await _db.IncrementKeyUsageAsync(keyId, discordId);
    public async Task MarkKeyRateLimitedAsync(int keyId, string discordId, System.DateTime? resetTime = null) => await _db.MarkKeyRateLimitedAsync(keyId, discordId, resetTime);
    public async Task MarkKeyQuotaExhaustedAsync(int keyId, string discordId) => await _db.MarkKeyQuotaExhaustedAsync(keyId, discordId);

    // Legacy compatibility
    public async Task<string?> GetApiKeyAsync(string discordId, string service)
        => (await GetNextAvailableKeyAsync(discordId, service))?.apiKey;

    public async Task<List<(int id, string maskedKey, bool isDefault)>> GetApiKeysAsync(string discordId, string service)
    {
        var keys = await _db.GetApiKeysWithMetaAsync(discordId, service);
        return keys.Select(k => (k.id, k.maskedKey, k.isDefault)).ToList();
    }
}