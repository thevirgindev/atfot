using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace atfot.core.storage
{
    public class UserSettings
    {
        public string DiscordId { get; set; } = "";
        public string Theme { get; set; } = "dark";
        public string Notifications { get; set; } = "public";
        public bool AiSummaryEnabled { get; set; } = false;
        public bool AiChatEnabled { get; set; } = false;
        public string SystemPrompt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    public class DatabaseService
    {
        private readonly string _conn;

        public DatabaseService()
        {
            var db_path = Path.Combine(AppContext.BaseDirectory, "atfot.db");
            _conn = $"Data Source={db_path}";
            init_db();
            migrate_multi_key();
            migrate_quota_columns();
            migrate_ai_summary_column();
            migrate_new_settings();
        }

        private void init_db()
        {
            using var conn = new SqliteConnection(_conn);
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
                    created_at TEXT DEFAULT (datetime('now')),
                    requests_today INTEGER DEFAULT 0,
                    total_quota INTEGER DEFAULT -1,
                    rate_limit_reset TEXT,
                    last_used TEXT,
                    is_active INTEGER DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS user_settings (
                    discord_id TEXT PRIMARY KEY,
                    default_export_format TEXT DEFAULT 'json',
                    default_instagram_scraper TEXT DEFAULT 'socialapi',
                    theme TEXT DEFAULT 'dark',
                    auto_export INTEGER DEFAULT 0,
                    notifications_enabled INTEGER DEFAULT 1,
                    ai_summary_enabled INTEGER DEFAULT 0,
                    updated_at TEXT DEFAULT (datetime('now'))
                );
                CREATE TABLE IF NOT EXISTS user_chat_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    discord_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TEXT DEFAULT (datetime('now'))
                );
                CREATE TABLE IF NOT EXISTS user_memories (
                    discord_id TEXT PRIMARY KEY,
                    memory_text TEXT NOT NULL,
                    updated_at TEXT DEFAULT (datetime('now'))
                );
            ";
            cmd.ExecuteNonQuery();
        }

        private void migrate_multi_key()
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='api_keys'";
            var exists = check.ExecuteScalar() != null;
            if (!exists) return;
            var copy = conn.CreateCommand();
            copy.CommandText = @"
                INSERT OR IGNORE INTO user_api_keys (discord_id, service, api_key, is_default)
                SELECT discord_id, service, api_key, 1 FROM api_keys
            ";
            copy.ExecuteNonQuery();
            var drop = conn.CreateCommand();
            drop.CommandText = "DROP TABLE api_keys";
            drop.ExecuteNonQuery();
        }

        private void migrate_quota_columns()
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            var cols = new[] { "requests_today", "total_quota", "rate_limit_reset", "last_used", "is_active" };
            foreach (var col in cols)
            {
                var check = conn.CreateCommand();
                check.CommandText = "PRAGMA table_info(user_api_keys)";
                bool found = false;
                using var reader = check.ExecuteReader();
                while (reader.Read()) if (reader.GetString(1) == col) { found = true; break; }
                if (!found)
                {
                    var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE user_api_keys ADD COLUMN {col}";
                    if (col == "requests_today") alter.CommandText += " INTEGER DEFAULT 0";
                    else if (col == "total_quota") alter.CommandText += " INTEGER DEFAULT -1";
                    else if (col == "rate_limit_reset") alter.CommandText += " TEXT";
                    else if (col == "last_used") alter.CommandText += " TEXT";
                    else if (col == "is_active") alter.CommandText += " INTEGER DEFAULT 1";
                    try { alter.ExecuteNonQuery(); } catch { }
                }
            }
        }

        private void migrate_ai_summary_column()
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            var check = conn.CreateCommand();
            check.CommandText = "PRAGMA table_info(user_settings)";
            bool found = false;
            using var reader = check.ExecuteReader();
            while (reader.Read()) if (reader.GetString(1) == "ai_summary_enabled") { found = true; break; }
            if (!found)
            {
                var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE user_settings ADD COLUMN ai_summary_enabled INTEGER DEFAULT 0";
                alter.ExecuteNonQuery();
            }
        }

        // recreates user_settings to add system_prompt and ai_chat_enabled columns
        private void migrate_new_settings()
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            var check = conn.CreateCommand();
            check.CommandText = "PRAGMA table_info(user_settings)";
            bool hasSystemPrompt = false;
            bool hasAiChat = false;
            using (var r = check.ExecuteReader())
            {
                while (r.Read())
                {
                    var col = r.GetString(1);
                    if (col == "system_prompt") hasSystemPrompt = true;
                    if (col == "ai_chat_enabled") hasAiChat = true;
                }
            }

            if (!hasSystemPrompt || !hasAiChat)
            {
                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS user_settings_new (
                        discord_id TEXT PRIMARY KEY,
                        theme TEXT DEFAULT 'dark',
                        notifications TEXT DEFAULT 'public',
                        ai_summary_enabled INTEGER DEFAULT 0,
                        ai_chat_enabled INTEGER DEFAULT 0,
                        system_prompt TEXT DEFAULT '',
                        updated_at TEXT DEFAULT (datetime('now'))
                    )";
                create.ExecuteNonQuery();

                var copy = conn.CreateCommand();
                copy.CommandText = @"
                    INSERT INTO user_settings_new (discord_id, theme, notifications, ai_summary_enabled, updated_at)
                    SELECT discord_id, theme, notifications, ai_summary_enabled, updated_at FROM user_settings";
                try { copy.ExecuteNonQuery(); } catch { }

                var dropOld = conn.CreateCommand();
                dropOld.CommandText = "DROP TABLE user_settings";
                try { dropOld.ExecuteNonQuery(); } catch { }

                var rename = conn.CreateCommand();
                rename.CommandText = "ALTER TABLE user_settings_new RENAME TO user_settings";
                rename.ExecuteNonQuery();
            }
        }

        // ========== USER AUTH & REDEMPTIONS (snake_case originals) ==========
        public async Task<bool> redeem_key(string key, string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var check = conn.CreateCommand();
            check.CommandText = "SELECT used_by FROM generated_keys WHERE key_value = $key";
            check.Parameters.AddWithValue("$key", key);
            var used = await check.ExecuteScalarAsync();
            if (used == null || used != DBNull.Value) return false;
            var update = conn.CreateCommand();
            update.CommandText = @"
                UPDATE generated_keys SET used_by = $discord WHERE key_value = $key;
                INSERT INTO users (discord_id, redeemed_key, redeemed_at) VALUES ($discord, $key, datetime('now'));
            ";
            update.Parameters.AddWithValue("$discord", discord_id);
            update.Parameters.AddWithValue("$key", key);
            var rows = await update.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> is_user_authorized(string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM users WHERE discord_id = $id";
            cmd.Parameters.AddWithValue("$id", discord_id);
            var res = await cmd.ExecuteScalarAsync();
            return res != null;
        }

        public async Task<string> gen_new_key()
        {
            var key = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO generated_keys (key_value, generated_at) VALUES ($key, datetime('now'))";
            cmd.Parameters.AddWithValue("$key", key);
            await cmd.ExecuteNonQueryAsync();
            return key;
        }

        public async Task log_redemption(string discord_id, string username, string? global_name, string? avatar_url, string account_created_at, string redeemed_key)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO redemptions (discord_id, username, global_name, avatar_url, account_created_at, redeemed_key, redeemed_at)
                VALUES ($id, $user, $global, $avatar, $created, $key, datetime('now'))
            ";
            cmd.Parameters.AddWithValue("$id", discord_id);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.Parameters.AddWithValue("$global", global_name ?? "");
            cmd.Parameters.AddWithValue("$avatar", avatar_url ?? "");
            cmd.Parameters.AddWithValue("$created", account_created_at);
            cmd.Parameters.AddWithValue("$key", redeemed_key);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(string discord_id, string username, string? global_name, string redeemed_key, string redeemed_at)>> get_all_redemptions()
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT discord_id, username, global_name, redeemed_key, redeemed_at FROM redemptions ORDER BY redeemed_at DESC";
            var res = new List<(string, string, string?, string, string)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                res.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)
                ));
            }
            return res;
        }

        // ========== USER SETTINGS ==========
        public async Task<UserSettings> get_user_settings(string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT discord_id, theme, notifications, ai_summary_enabled,
                       ai_chat_enabled, system_prompt, updated_at
                FROM user_settings WHERE discord_id = $id
            ";
            cmd.Parameters.AddWithValue("$id", discord_id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserSettings
                {
                    DiscordId = reader.GetString(0),
                    Theme = reader.GetString(1),
                    Notifications = reader.GetString(2),
                    AiSummaryEnabled = reader.GetInt32(3) == 1,
                    AiChatEnabled = reader.GetInt32(4) == 1,
                    SystemPrompt = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    UpdatedAt = reader.GetString(6)
                };
            }
            else
            {
                var insert = conn.CreateCommand();
                insert.CommandText = @"
                    INSERT INTO user_settings (discord_id, theme, notifications, ai_summary_enabled, ai_chat_enabled, system_prompt, updated_at)
                    VALUES ($id, 'dark', 'public', 0, 0, '', datetime('now'))
                ";
                insert.Parameters.AddWithValue("$id", discord_id);
                await insert.ExecuteNonQueryAsync();
                return await get_user_settings(discord_id);
            }
        }

        public async Task<bool> update_user_setting(string discord_id, string key, string value)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            string column = key.ToLower() switch
            {
                "theme" => "theme",
                "notifications" => "notifications",
                "ai_summary" => "ai_summary_enabled",
                "ai_chat" => "ai_chat_enabled",
                "system_prompt" => "system_prompt",
                _ => throw new ArgumentException("invalid key")
            };
            object dbVal = (key.ToLower() == "ai_summary" || key.ToLower() == "ai_chat")
                ? (value.ToLower() == "true" || value == "1" || value.ToLower() == "on" ? 1 : 0)
                : value;
            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE user_settings
                SET {column} = $val, updated_at = datetime('now')
                WHERE discord_id = $id
            ";
            cmd.Parameters.AddWithValue("$id", discord_id);
            cmd.Parameters.AddWithValue("$val", dbVal);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ========== API KEYS (snake_case originals) ==========
        public async Task<int> add_api_key(string discord_id, string service, string api_key, bool is_default = false, int total_quota = -1)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            if (is_default)
            {
                var unset = conn.CreateCommand();
                unset.CommandText = "UPDATE user_api_keys SET is_default = 0 WHERE discord_id = $id AND service = $service";
                unset.Parameters.AddWithValue("$id", discord_id);
                unset.Parameters.AddWithValue("$service", service.ToLowerInvariant());
                await unset.ExecuteNonQueryAsync();
            }
            var insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO user_api_keys (discord_id, service, api_key, is_default, total_quota)
                VALUES ($id, $service, $key, $def, $quota);
                SELECT last_insert_rowid();
            ";
            insert.Parameters.AddWithValue("$id", discord_id);
            insert.Parameters.AddWithValue("$service", service.ToLowerInvariant());
            insert.Parameters.AddWithValue("$key", api_key);
            insert.Parameters.AddWithValue("$def", is_default ? 1 : 0);
            insert.Parameters.AddWithValue("$quota", total_quota);
            var new_id = Convert.ToInt32(await insert.ExecuteScalarAsync());
            return new_id;
        }

        public async Task<bool> remove_api_key(string discord_id, int key_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM user_api_keys WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<List<(int id, string masked_key, bool is_default, int req_today, int total_quota, string? rate_limit_reset, bool is_active)>> get_api_keys_with_meta(string discord_id, string service)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, api_key, is_default, requests_today, total_quota, rate_limit_reset, is_active FROM user_api_keys WHERE discord_id = $id AND service = $service ORDER BY is_default DESC, id ASC";
            cmd.Parameters.AddWithValue("$id", discord_id);
            cmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
            var res = new List<(int, string, bool, int, int, string?, bool)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var full = reader.GetString(1);
                var masked = full.Length > 8 ? full[..4] + "****" + full[^4..] : "****";
                var is_def = reader.GetInt32(2) == 1;
                var req_today = reader.GetInt32(3);
                var quota = reader.GetInt32(4);
                var reset = reader.IsDBNull(5) ? null : reader.GetString(5);
                var active = reader.GetInt32(6) == 1;
                res.Add((id, masked, is_def, req_today, quota, reset, active));
            }
            return res;
        }

        public async Task<string?> get_raw_api_key_by_id(int key_id, string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT api_key FROM user_api_keys WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            return (await cmd.ExecuteScalarAsync())?.ToString();
        }

        public async Task<bool> inc_key_usage(int key_id, string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE user_api_keys SET requests_today = requests_today + 1, last_used = datetime('now') WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> mark_key_rate_limited(int key_id, string discord_id, DateTime? reset_time = null)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var reset_str = reset_time?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss");
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE user_api_keys SET is_active = 0, rate_limit_reset = $reset WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            cmd.Parameters.AddWithValue("$reset", reset_str);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> mark_key_quota_exhausted(int key_id, string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE user_api_keys SET is_active = 0 WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> reactivate_key(int key_id, string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE user_api_keys SET is_active = 1, requests_today = 0, rate_limit_reset = NULL WHERE id = $id AND discord_id = $did";
            cmd.Parameters.AddWithValue("$id", key_id);
            cmd.Parameters.AddWithValue("$did", discord_id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<List<(int id, string api_key)>> get_all_active_keys(string discord_id, string service)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, api_key FROM user_api_keys WHERE discord_id = $id AND service = $service AND is_active = 1 ORDER BY is_default DESC, id ASC";
            cmd.Parameters.AddWithValue("$id", discord_id);
            cmd.Parameters.AddWithValue("$service", service.ToLowerInvariant());
            var res = new List<(int, string)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                res.Add((reader.GetInt32(0), reader.GetString(1)));
            }
            return res;
        }

        public async Task<bool> set_default_key(string discord_id, string service, int key_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var unset = conn.CreateCommand();
            unset.CommandText = "UPDATE user_api_keys SET is_default = 0 WHERE discord_id = $id AND service = $service";
            unset.Parameters.AddWithValue("$id", discord_id);
            unset.Parameters.AddWithValue("$service", service.ToLowerInvariant());
            await unset.ExecuteNonQueryAsync();

            var set = conn.CreateCommand();
            set.CommandText = "UPDATE user_api_keys SET is_default = 1 WHERE id = $kid AND discord_id = $did";
            set.Parameters.AddWithValue("$kid", key_id);
            set.Parameters.AddWithValue("$did", discord_id);
            var rows = await set.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<Dictionary<string, List<(int id, string masked_key, bool is_default)>>> get_all_user_keys_grouped(string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT service, id, api_key, is_default FROM user_api_keys WHERE discord_id = $id";
            cmd.Parameters.AddWithValue("$id", discord_id);
            var res = new Dictionary<string, List<(int, string, bool)>>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var service = reader.GetString(0);
                var id = reader.GetInt32(1);
                var full = reader.GetString(2);
                var masked = full.Length > 8 ? full[..4] + "****" + full[^4..] : "****";
                var is_def = reader.GetInt32(3) == 1;
                if (!res.ContainsKey(service))
                    res[service] = new List<(int, string, bool)>();
                res[service].Add((id, masked, is_def));
            }
            return res;
        }

        public async Task<bool> revoke_user_access(string discord_id)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE generated_keys SET used_by = NULL WHERE used_by = $id;
                DELETE FROM users WHERE discord_id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", discord_id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ========== STATISTICS & PRUNING ==========
        public async Task<int> prune_old_redemptions(int days_to_keep)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM redemptions WHERE redeemed_at < datetime('now', '-' || $days || ' days')";
            cmd.Parameters.AddWithValue("$days", days_to_keep);
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<long> get_db_size()
        {
            var db_path = Path.Combine(AppContext.BaseDirectory, "atfot.db");
            return new FileInfo(db_path).Length;
        }

        public async Task<int> get_total_authorized_users()
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ========== PascalCase wrappers for service compatibility ==========
        public async Task<int> AddApiKeyAsync(string discordId, string service, string apiKey, bool isDefault = false, int totalQuota = -1)
            => await add_api_key(discordId, service, apiKey, isDefault, totalQuota);

        public async Task<bool> RemoveApiKeyAsync(string discordId, int keyId)
            => await remove_api_key(discordId, keyId);

        public async Task<bool> SetDefaultKeyAsync(string discordId, string service, int keyId)
            => await set_default_key(discordId, service, keyId);

        public async Task<Dictionary<string, List<(int id, string maskedKey, bool isDefault)>>> GetAllUserKeysGroupedAsync(string discordId)
            => await get_all_user_keys_grouped(discordId);

        public async Task<List<(int id, string apiKey)>> GetAllActiveKeysAsync(string discordId, string service)
            => await get_all_active_keys(discordId, service);

        public async Task IncrementKeyUsageAsync(int keyId, string discordId)
            => await inc_key_usage(keyId, discordId);

        public async Task MarkKeyRateLimitedAsync(int keyId, string discordId, DateTime? resetTime = null)
            => await mark_key_rate_limited(keyId, discordId, resetTime);

        public async Task MarkKeyQuotaExhaustedAsync(int keyId, string discordId)
            => await mark_key_quota_exhausted(keyId, discordId);

        public async Task<List<(int id, string maskedKey, bool isDefault)>> GetApiKeysWithMetaAsync(string discordId, string service)
        {
            var list = await get_api_keys_with_meta(discordId, service);
            return list.Select(x => (x.id, x.masked_key, x.is_default)).ToList();
        }

        public async Task<bool> RedeemKeyAsync(string key, string discordId)
            => await redeem_key(key, discordId);

        public async Task<bool> IsUserAuthorizedAsync(string discordId)
            => await is_user_authorized(discordId);

        public async Task<string> GenerateNewKeyAsync()
            => await gen_new_key();

        public async Task LogRedemptionAsync(string discordId, string username, string? globalName, string? avatarUrl, string accountCreatedAt, string redeemedKey)
            => await log_redemption(discordId, username, globalName, avatarUrl, accountCreatedAt, redeemedKey);

        public async Task<List<(string discordId, string username, string? globalName, string redeemedKey, string redeemedAt)>> GetAllRedemptionsAsync()
            => await get_all_redemptions();

        public async Task<UserSettings> GetUserSettingsAsync(string discordId)
            => await get_user_settings(discordId);

        public async Task<bool> UpdateUserSettingAsync(string discordId, string key, string value)
            => await update_user_setting(discordId, key, value);

        public async Task<bool> RevokeUserAccessAsync(string discordId)
            => await revoke_user_access(discordId);

        public async Task<int> PruneOldRedemptionsAsync(int daysToKeep)
            => await prune_old_redemptions(daysToKeep);

        public async Task<long> GetDatabaseSizeAsync()
            => await get_db_size();

        public async Task<int> GetTotalAuthorizedUsersAsync()
            => await get_total_authorized_users();

        // ========== CHAT HISTORY & MEMORY ==========
        public async Task SaveChatMessageAsync(string discordId, string role, string content)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO user_chat_history (discord_id, role, content) VALUES ($id, $role, $content)";
            cmd.Parameters.AddWithValue("$id", discordId);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(string role, string content)>> GetChatHistoryAsync(string discordId, int limit)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT role, content FROM user_chat_history WHERE discord_id = $id ORDER BY id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$id", discordId);
            cmd.Parameters.AddWithValue("$limit", limit);
            var res = new List<(string, string)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                res.Add((reader.GetString(0), reader.GetString(1)));
            }
            res.Reverse();
            return res;
        }

        public async Task ClearChatHistoryAsync(string discordId)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM user_chat_history WHERE discord_id = $id";
            cmd.Parameters.AddWithValue("$id", discordId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SaveUserMemoryAsync(string discordId, string memoryText)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO user_memories (discord_id, memory_text, updated_at)
                VALUES ($id, $text, datetime('now'))
                ON CONFLICT(discord_id) DO UPDATE SET memory_text = $text, updated_at = datetime('now')
            ";
            cmd.Parameters.AddWithValue("$id", discordId);
            cmd.Parameters.AddWithValue("$text", memoryText);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string> GetUserMemoryAsync(string discordId)
        {
            await using var conn = new SqliteConnection(_conn);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT memory_text FROM user_memories WHERE discord_id = $id";
            cmd.Parameters.AddWithValue("$id", discordId);
            var res = await cmd.ExecuteScalarAsync();
            return res?.ToString() ?? string.Empty;
        }
    }
}