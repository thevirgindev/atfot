using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace atfot.core.storage;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "atfot.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
        MigrateToMultiKey();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                discord_id TEXT PRIMARY KEY,
                redeemed_key TEXT UNIQUE NOT NULL,
                redeemed_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS generated_keys (
                key_value TEXT PRIMARY KEY,
                generated_at TEXT NOT NULL,
                used_by TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS redemptions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                discord_id TEXT NOT NULL,
                username TEXT NOT NULL,
                global_name TEXT,
                avatar_url TEXT,
                account_created_at TEXT,
                redeemed_key TEXT NOT NULL,
                redeemed_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS user_api_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                discord_id TEXT NOT NULL,
                service TEXT NOT NULL,
                api_key TEXT NOT NULL,
                is_default INTEGER DEFAULT 0,
                created_at TEXT DEFAULT (datetime('now'))
            );
        ";
        cmd.ExecuteNonQuery();
    }

    private void MigrateToMultiKey()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var checkTable = conn.CreateCommand();
        checkTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='api_keys'";
        var exists = checkTable.ExecuteScalar() != null;
        if (!exists) return;

        var copyCmd = conn.CreateCommand();
        copyCmd.CommandText = @"
            INSERT OR IGNORE INTO user_api_keys (discord_id, service, api_key, is_default)
            SELECT discord_id, service, api_key, 1 FROM api_keys
        ";
        copyCmd.ExecuteNonQuery();

        var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = "DROP TABLE api_keys";
        dropCmd.ExecuteNonQuery();
    }

    public async Task<bool> RedeemKeyAsync(string key, string discordId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT used_by FROM generated_keys WHERE key_value = $key";
        checkCmd.Parameters.AddWithValue("$key", key);
        var usedByObj = await checkCmd.ExecuteScalarAsync();
        if (usedByObj == null || usedByObj != DBNull.Value) return false;
        var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE generated_keys SET used_by = $discord WHERE key_value = $key;
            INSERT INTO users (discord_id, redeemed_key, redeemed_at) VALUES ($discord, $key, datetime('now'));
        ";
        updateCmd.Parameters.AddWithValue("$discord", discordId);
        updateCmd.Parameters.AddWithValue("$key", key);
        var rows = await updateCmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> IsUserAuthorizedAsync(string discordId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM users WHERE discord_id = $id";
        cmd.Parameters.AddWithValue("$id", discordId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<string> GenerateNewKeyAsync()
    {
        var key = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO generated_keys (key_value, generated_at) VALUES ($key, datetime('now'))";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync();
        return key;
    }

    public async Task LogRedemptionAsync(string discordId, string username, string? globalName, string? avatarUrl, string accountCreatedAt, string redeemedKey)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO redemptions (discord_id, username, global_name, avatar_url, account_created_at, redeemed_key, redeemed_at)
            VALUES ($id, $user, $global, $avatar, $created, $key, datetime('now'))
        ";
        cmd.Parameters.AddWithValue("$id", discordId);
        cmd.Parameters.AddWithValue("$user", username);
        cmd.Parameters.AddWithValue("$global", globalName ?? "");
        cmd.Parameters.AddWithValue("$avatar", avatarUrl ?? "");
        cmd.Parameters.AddWithValue("$created", accountCreatedAt);
        cmd.Parameters.AddWithValue("$key", redeemedKey);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(string discordId, string username, string? globalName, string redeemedKey, string redeemedAt)>> GetAllRedemptionsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT discord_id, username, global_name, redeemed_key, redeemed_at FROM redemptions ORDER BY redeemed_at DESC";
        var results = new List<(string, string, string?, string, string)>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)
            ));
        }
        return results;
    }

    public async Task<int> AddApiKeyAsync(string discordId, string service, string apiKey, bool isDefault = false)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        if (isDefault)
        {
            var unsetCmd = conn.CreateCommand();
            unsetCmd.CommandText = "UPDATE user_api_keys SET is_default = 0 WHERE discord_id = $id AND service = $service";
            unsetCmd.Parameters.AddWithValue("$id", discordId);
            unsetCmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
            await unsetCmd.ExecuteNonQueryAsync();
        }
        var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO user_api_keys (discord_id, service, api_key, is_default)
            VALUES ($id, $service, $key, $def);
            SELECT last_insert_rowid();
        ";
        insertCmd.Parameters.AddWithValue("$id", discordId);
        insertCmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
        insertCmd.Parameters.AddWithValue("$key", apiKey);
        insertCmd.Parameters.AddWithValue("$def", isDefault ? 1 : 0);
        var newId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        return newId;
    }

    public async Task<bool> RemoveApiKeyAsync(string discordId, int keyId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_api_keys WHERE id = $id AND discord_id = $did";
        cmd.Parameters.AddWithValue("$id", keyId);
        cmd.Parameters.AddWithValue("$did", discordId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<List<(int id, string maskedKey, bool isDefault)>> GetApiKeysAsync(string discordId, string service)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, api_key, is_default FROM user_api_keys WHERE discord_id = $id AND service = $service";
        cmd.Parameters.AddWithValue("$id", discordId);
        cmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
        var result = new List<(int, string, bool)>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var fullKey = reader.GetString(1);
            var masked = fullKey.Length > 8 ? fullKey[..4] + "****" + fullKey[^4..] : "****";
            var isDefault = reader.GetInt32(2) == 1;
            result.Add((id, masked, isDefault));
        }
        return result;
    }

    public async Task<string?> GetDefaultApiKeyAsync(string discordId, string service)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT api_key FROM user_api_keys WHERE discord_id = $id AND service = $service AND is_default = 1";
        cmd.Parameters.AddWithValue("$id", discordId);
        cmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task<string?> GetApiKeyAsync(string discordId, string service)
    {
        return await GetDefaultApiKeyAsync(discordId, service);
    }

    public async Task<bool> SetDefaultKeyAsync(string discordId, string service, int keyId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var unsetCmd = conn.CreateCommand();
        unsetCmd.CommandText = "UPDATE user_api_keys SET is_default = 0 WHERE discord_id = $id AND service = $service";
        unsetCmd.Parameters.AddWithValue("$id", discordId);
        unsetCmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
        await unsetCmd.ExecuteNonQueryAsync();

        var setCmd = conn.CreateCommand();
        setCmd.CommandText = "UPDATE user_api_keys SET is_default = 1 WHERE id = $kid AND discord_id = $did";
        setCmd.Parameters.AddWithValue("$kid", keyId);
        setCmd.Parameters.AddWithValue("$did", discordId);
        var rows = await setCmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<Dictionary<string, List<(int id, string maskedKey, bool isDefault)>>> GetAllUserKeysGroupedAsync(string discordId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT service, id, api_key, is_default FROM user_api_keys WHERE discord_id = $id";
        cmd.Parameters.AddWithValue("$id", discordId);
        var result = new Dictionary<string, List<(int, string, bool)>>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var service = reader.GetString(0);
            var id = reader.GetInt32(1);
            var fullKey = reader.GetString(2);
            var masked = fullKey.Length > 8 ? fullKey[..4] + "****" + fullKey[^4..] : "****";
            var isDefault = reader.GetInt32(3) == 1;
            if (!result.ContainsKey(service))
                result[service] = new List<(int, string, bool)>();
            result[service].Add((id, masked, isDefault));
        }
        return result;
    }

    public async Task<bool> RevokeUserAccessAsync(string discordId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE generated_keys SET used_by = NULL WHERE used_by = $id;
            DELETE FROM users WHERE discord_id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", discordId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
