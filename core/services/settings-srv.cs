using System.Threading.Tasks;
using atfot.core.storage;  // Use the UserSettings class from storage namespace

namespace atfot.core.services;

public class SettingsService
{
    private readonly DatabaseService _db;

    public SettingsService(DatabaseService db)
    {
        _db = db;
    }

    public async Task<UserSettings> GetUserSettingsAsync(string discordId)
    {
        return await _db.GetUserSettingsAsync(discordId);
    }

    public async Task<bool> UpdateSettingAsync(string discordId, string key, string value)
    {
        return await _db.UpdateUserSettingAsync(discordId, key, value);
    }
}