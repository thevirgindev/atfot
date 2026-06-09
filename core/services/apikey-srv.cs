using System.Collections.Generic;
using System.Threading.Tasks;
using pewbot.core.storage;

namespace pewbot.core.services;

public class ApiKeyService
{
    private readonly DatabaseService _db;

    public ApiKeyService(DatabaseService db)
    {
        _db = db;
    }

    public async Task<int> AddApiKeyAsync(string discordId, string service, string apiKey, bool isDefault = false)
        => await _db.AddApiKeyAsync(discordId, service, apiKey, isDefault);

    public async Task<bool> RemoveApiKeyAsync(string discordId, int keyId)
        => await _db.RemoveApiKeyAsync(discordId, keyId);

    public async Task<List<(int id, string maskedKey, bool isDefault)>> GetApiKeysAsync(string discordId, string service)
        => await _db.GetApiKeysAsync(discordId, service);

    public async Task<string?> GetDefaultApiKeyAsync(string discordId, string service)
        => await _db.GetDefaultApiKeyAsync(discordId, service);

    public async Task<bool> SetDefaultKeyAsync(string discordId, string service, int keyId)
        => await _db.SetDefaultKeyAsync(discordId, service, keyId);

    public async Task<Dictionary<string, List<(int id, string maskedKey, bool isDefault)>>> GetAllUserKeysGroupedAsync(string discordId)
        => await _db.GetAllUserKeysGroupedAsync(discordId);

    // Backward compatibility for old code that expects a single default key
    public async Task<string?> GetApiKeyAsync(string discordId, string service)
        => await GetDefaultApiKeyAsync(discordId, service);
}