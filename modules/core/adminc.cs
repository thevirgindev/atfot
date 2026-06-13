using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using atfot.core.services;
using atfot.core.storage;
using atfot.utils;
using System.Collections.Concurrent;

namespace atfot.modules.core;

[DefaultMemberPermissions(GuildPermission.Administrator)]
public class AdminCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly DatabaseService _dbService;
    private readonly BotConfig _botConfig;

    private static readonly Dictionary<string, Dictionary<string, List<(int id, string maskedKey, bool isDefault)>>> _keyPagesCache = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lastRedeemAttempt = new();
    private static readonly ConcurrentDictionary<string, Task> _pendingDmTasks = new();

    private static readonly List<string> _services = new()
    {
        "google_custom_search", "openai",
        "twitter", "tiktok", "linkedin", "pinterest", "socialapi", "serpapi", "apify",
        "osintcat", "leakinsight", "intelfetch", "indicia", "crowsint", "oathnet",
        "peopledatalabs", "ipgeolocation", "onionengine", "numverify"
    };

    public AdminCmd(KeyRedemptionService keyService, ApiKeyService apiKeyService, CooldownService cooldown,
        EmbedBuilderService embed, DatabaseService dbService, BotConfig botConfig)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _dbService = dbService;
        _botConfig = botConfig;
    }

    private async Task<bool> EnsureAuthorized() => await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    private async Task<bool> EnsureOwner() => Context.User.Id.ToString() == _botConfig.OwnerDiscordId;

    // ========== REDEEM WITH COOLDOWN ==========
    [SlashCommand("redeem", "redeem a master key to activate the bot (one attempt per hour)")]
    public async Task RedeemKey([Summary("key")] string key)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        string userId = Context.User.Id.ToString();

        // Check if already authorized
        if (await _keyService.IsAuthorizedAsync(userId))
        {
            await RespondAsync("[ERR] You already have a master key redeemed. One key per user.", ephemeral: true);
            return;
        }

        // Check cooldown
        if (_lastRedeemAttempt.TryGetValue(userId, out var lastAttempt))
        {
            var timeSince = DateTime.UtcNow - lastAttempt;
            if (timeSince < TimeSpan.FromHours(1))
            {
                var remainingTime = TimeSpan.FromHours(1) - timeSince;
                await RespondAsync($"[WAIT] You must wait {remainingTime.Minutes}m {remainingTime.Seconds}s before trying again.", ephemeral: true);
                return;
            }
        }

        // Attempt redemption
        var success = await _keyService.RedeemKeyAsync(key, userId);
        
        // Record attempt time
        _lastRedeemAttempt[userId] = DateTime.UtcNow;

        if (success)
        {
            var user = Context.User;
            var avatarUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            await _dbService.LogRedemptionAsync(user.Id.ToString(), user.Username, user.GlobalName, avatarUrl, user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"), key);
            if (!string.IsNullOrEmpty(_botConfig.OwnerDiscordId) && ulong.TryParse(_botConfig.OwnerDiscordId, out var ownerId))
            {
                var owner = await Context.Client.GetUserAsync(ownerId);
                if (owner != null)
                {
                    var notifyEmbed = _embed.CreateMonochromeEmbed("New Key Redemption",
                        $"**User:** {user.Username} ({user.Id})\n**Key:** `{key}`\n**Time:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", "white");
                    try { await owner.SendMessageAsync(embed: notifyEmbed); } catch { }
                }
            }
            var embed = _embed.CreateMonochromeEmbed("key redemption", "Master key redeemed. Full access granted.", "white");
            await RespondAsync(embed: embed, ephemeral: true);

            // No need for cooldown DM on success because user won't try again
        }
        else
        {
            var embed = _embed.CreateMonochromeEmbed("key redemption", "Invalid or already used master key.", "gray");
            await RespondAsync(embed: embed, ephemeral: true);

            // Schedule a DM after cooldown expires (only if not already scheduled)
            if (!_pendingDmTasks.ContainsKey(userId))
            {
                var dmTask = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromHours(1));
                    try
                    {
                        var user = await Context.Client.GetUserAsync(ulong.Parse(userId));
                        if (user != null)
                        {
                            var dmChannel = await user.CreateDMChannelAsync();
                            await dmChannel.SendMessageAsync("[INFO] **your 1-hour access window has started.** use `/guide` to get started.**");
                        }
                    }
                    catch { }
                    finally
                    {
                        _pendingDmTasks.TryRemove(userId, out _);
                    }
                });
                _pendingDmTasks[userId] = dmTask;
            }
        }
    }

    // ========== SET API KEY ==========
    [SlashCommand("setapikey", "add an API key (default or extra)")]
    public async Task SetApiKey(
        [Summary("service")] string service,
        [Summary("apikey")] string apiKey,
        [Summary("type", "default or other")] string type = "default",
        [Summary("quota", "daily request limit (-1 unlimited)")] int quota = -1)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }
        if (!_services.Contains(service))
        {
            await RespondAsync($"[ERR] Unknown service. Available: {string.Join(", ", _services)}", ephemeral: true);
            return;
        }
        bool isDefault = type.ToLower() == "default";
        var id = await _apiKeyService.AddApiKeyAsync(Context.User.Id.ToString(), service, apiKey, isDefault, quota);
        await RespondAsync($"[OK] API key for **{service}** added (ID: {id}, type: {type}). {Sarcasm.Get()}", ephemeral: true);
    }

    // ========== REMOVE API KEY ==========
    [SlashCommand("removeapikey", "remove an API key by ID")]
    public async Task RemoveApiKey([Summary("service")] string service, [Summary("keyid")] int keyId)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }
        if (!_services.Contains(service)) { await RespondAsync($"[ERR] Unknown service.", ephemeral: true); return; }
        var success = await _apiKeyService.RemoveApiKeyAsync(Context.User.Id.ToString(), keyId);
        if (success) await RespondAsync($"[OK] Removed key ID {keyId}. {Sarcasm.Get()}", ephemeral: true);
        else await RespondAsync("[ERR] Key not found or you don't own it.", ephemeral: true);
    }

    // ========== SET DEFAULT KEY (existing) ==========
    [SlashCommand("setdefaultkey", "set an existing key as default for its service")]
    public async Task SetDefaultKey([Summary("service")] string service, [Summary("keyid")] int keyId)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }
        if (!_services.Contains(service)) { await RespondAsync($"[ERR] Unknown service.", ephemeral: true); return; }
        var success = await _apiKeyService.SetDefaultKeyAsync(Context.User.Id.ToString(), service, keyId);
        if (success) await RespondAsync($"[OK] Key ID {keyId} is now default for **{service}**. {Sarcasm.Get()}", ephemeral: true);
        else await RespondAsync($"[ERR] Could not set key ID {keyId} as default.", ephemeral: true);
    }

    // ========== ADD NEW KEY ==========
    [SlashCommand("addnewkey", "add an additional API key for a service (does not change default)")]
    public async Task AddNewKey(
        [Summary("service")] string service,
        [Summary("apikey")] string apiKey,
        [Summary("quota", "daily request limit (-1 unlimited)")] int quota = -1)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }
        if (!_services.Contains(service))
        {
            await RespondAsync($"[ERR] Unknown service. Available: {string.Join(", ", _services)}", ephemeral: true);
            return;
        }
        var id = await _apiKeyService.AddApiKeyAsync(Context.User.Id.ToString(), service, apiKey, false, quota);
        await RespondAsync($"[OK] Additional API key for **{service}** added (ID: {id}). Use `/changekey` to make it default.", ephemeral: true);
    }

    // ========== CHANGE DEFAULT KEY ==========
    [SlashCommand("changekey", "change the default API key for a service using its numeric ID")]
    public async Task ChangeKey(
        [Summary("service")] string service,
        [Summary("keyid")] int keyId)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }
        if (!_services.Contains(service))
        {
            await RespondAsync($"[ERR] Unknown service.", ephemeral: true);
            return;
        }
        var success = await _apiKeyService.SetDefaultKeyAsync(Context.User.Id.ToString(), service, keyId);
        if (success)
            await RespondAsync($"[OK] Key ID {keyId} is now the default for **{service}**.", ephemeral: true);
        else
            await RespondAsync($"[ERR] Could not set key ID {keyId} as default. Make sure it belongs to you and the service matches.", ephemeral: true);
    }

    // ========== MYKEYS ==========
    [SlashCommand("mykeys", "show all your API keys (masked) with pagination")]
    public async Task MyKeys()
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WAIT] Wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] Redeem a master key first.", ephemeral: true); return; }

        var grouped = await _apiKeyService.GetAllUserKeysGroupedAsync(Context.User.Id.ToString());
        if (grouped.Count == 0)
        {
            await RespondAsync("[INFO] No API keys stored. Use `/setapikey` or `/addnewkey`.", ephemeral: true);
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        _keyPagesCache[sessionId] = grouped;
        await ShowKeyPage(sessionId, 0, null);
    }

    private async Task ShowKeyPage(string sessionId, int pageIndex, ulong? messageId)
    {
        if (!_keyPagesCache.TryGetValue(sessionId, out var grouped))
            return;

        var allKeys = new List<(string service, int id, string maskedKey, bool isDefault)>();
        foreach (var (service, keys) in grouped)
        {
            foreach (var (id, masked, isDefault) in keys)
                allKeys.Add((service, id, masked, isDefault));
        }

        const int keysPerPage = 10;
        int totalPages = (int)Math.Ceiling(allKeys.Count / (double)keysPerPage);
        if (pageIndex < 0) pageIndex = 0;
        if (pageIndex >= totalPages) pageIndex = totalPages - 1;

        var pageKeys = allKeys.Skip(pageIndex * keysPerPage).Take(keysPerPage).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("```");
        foreach (var (service, id, masked, isDefault) in pageKeys)
        {
            sb.AppendLine($"{service.ToUpper()}: [{id}] {masked}{(isDefault ? " (default)" : "")}");
        }
        sb.AppendLine("```");

        var embed = _embed.CreateMonochromeEmbed($"Your API Keys - Page {pageIndex + 1}/{totalPages}", sb.ToString(), "dark");
        var components = new ComponentBuilder()
            .WithButton("◀", $"keys_page:{sessionId}:{pageIndex - 1}", ButtonStyle.Secondary, disabled: pageIndex == 0)
            .WithButton("▶", $"keys_page:{sessionId}:{pageIndex + 1}", ButtonStyle.Secondary, disabled: pageIndex == totalPages - 1)
            .Build();

        if (messageId.HasValue)
        {
            var channel = Context.Channel as ISocketMessageChannel;
            var msg = await channel.GetMessageAsync(messageId.Value) as IUserMessage;
            if (msg != null)
                await msg.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
            else
                await FollowupAsync(embed: embed, components: components);
        }
        else
        {
            await FollowupAsync(embed: embed, components: components);
        }
    }

    [ComponentInteraction("keys_page:*:*", ignoreGroupNames: true)]
    public async Task HandleKeyPage(string sessionId, string pageStr)
    {
        await DeferAsync();
        if (!int.TryParse(pageStr, out int page)) return;
        var smc = Context.Interaction as SocketMessageComponent;
        if (smc == null) return;
        await ShowKeyPage(sessionId, page, smc.Message.Id);
    }

    // ========== GENKEY ==========
    [SlashCommand("genkey", "generate a new master key (owner only)")]
    [RequireOwner]
    public async Task GenerateMasterKey()
    {
        var newKey = await _keyService.GenerateNewKeyAsync();
        var embed = _embed.CreateMonochromeEmbed("new master key", $"`{newKey}`\n\nShare with a user to grant access.", "white");
        await RespondAsync(embed: embed, ephemeral: true);
    }

    // ========== STATUS ==========
    [SlashCommand("status", "change bot presence (owner only)")]
    [RequireOwner]
    public async Task SetStatus([Summary("status")] string status = "idle", [Summary("activity")] string activity = "ATFOT – All The Fucking OSINT Tools | /help")
    {
        var presenceStatus = status.ToLower() switch
        {
            "idle" => UserStatus.Idle,
            "dnd" => UserStatus.DoNotDisturb,
            "invisible" => UserStatus.Invisible,
            _ => UserStatus.Online
        };
        var client = Context.Client as DiscordSocketClient;
        if (client != null)
        {
            await client.SetStatusAsync(presenceStatus);
            await client.SetGameAsync(activity);
        }
        var embed = _embed.CreateMonochromeEmbed("bot status", $"Status: {presenceStatus}\nActivity: {activity}", "white");
        await RespondAsync(embed: embed, ephemeral: true);
    }

    // ========== REDEMPTIONS ==========
    [SlashCommand("redemptions", "view all key redemptions (owner only)")]
    [RequireOwner]
    public async Task ViewRedemptions()
    {
        var redemptions = await _dbService.GetAllRedemptionsAsync();
        if (redemptions.Count == 0)
        {
            await RespondAsync("[INFO] No redemptions yet.", ephemeral: true);
            return;
        }
        var sb = new StringBuilder("**Redemption Log:**\n");
        foreach (var r in redemptions)
            sb.AppendLine($"**{r.username}** ({r.discordId}) – `{r.redeemedKey}` at {r.redeemedAt}");
        var content = sb.ToString();
        if (content.Length > 1900)
        {
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            await RespondWithFileAsync(stream, "redemptions.txt", "Redemption log attached.");
        }
        else
        {
            var embed = _embed.CreateMonochromeEmbed("redemption log", content, "dark");
            await RespondAsync(embed: embed, ephemeral: true);
        }
    }

    // ========== RMK ==========
    [SlashCommand("rmk", "revoke a user's access (owner only)")]
    [RequireOwner]
    public async Task RevokeMasterKey([Summary("userid")] string userId)
    {
        if (!ulong.TryParse(userId, out _)) { await RespondAsync("[ERR] Invalid user ID.", ephemeral: true); return; }
        var success = await _dbService.RevokeUserAccessAsync(userId);
        if (success) await RespondAsync($"[OK] Revoked access for user {userId}. {Sarcasm.Get()}", ephemeral: true);
        else await RespondAsync($"[ERR] User {userId} not found or already revoked.", ephemeral: true);
    }

    // ========== DB BACKUP ==========
    [SlashCommand("db_backup", "backup the SQLite database (owner only)")]
    [RequireOwner]
    public async Task DbBackup()
    {
        await DeferAsync();
        try
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "atfot.db");
            var backupPath = Path.Combine(Path.GetTempPath(), $"atfot_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(dbPath, backupPath, true);
            var fileStream = File.OpenRead(backupPath);
            await FollowupWithFileAsync(fileStream, Path.GetFileName(backupPath), 
                $"[OK] Database backup created. Size: {new FileInfo(dbPath).Length / 1024} KB");
            File.Delete(backupPath);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"[ERR] Backup failed: {ex.Message}", ephemeral: true);
        }
    }

    // ========== DB RESTORE ==========
    [SlashCommand("db_restore", "restore database from backup file (owner only)")]
    [RequireOwner]
    public async Task DbRestore(IAttachment backupFile)
    {
        await DeferAsync();
        if (!backupFile.Filename.EndsWith(".db"))
        {
            await FollowupAsync("[ERR] Please upload a valid .db file.", ephemeral: true);
            return;
        }
        try
        {
            using var httpClient = new HttpClient();
            var backupBytes = await httpClient.GetByteArrayAsync(backupFile.Url);
            var tempBackup = Path.GetTempFileName();
            await File.WriteAllBytesAsync(tempBackup, backupBytes);
            var dbPath = Path.Combine(AppContext.BaseDirectory, "atfot.db");
            var oldBackup = dbPath + ".old";
            if (File.Exists(oldBackup)) File.Delete(oldBackup);
            File.Move(dbPath, oldBackup);
            File.Move(tempBackup, dbPath);
            await FollowupAsync($"[OK] Database restored from `{backupFile.Filename}`. Old database saved as `atfot.db.old`.", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"[ERR] Restore failed: {ex.Message}", ephemeral: true);
        }
    }

    // ========== DB PRUNE ==========
    [SlashCommand("db_prune", "delete old redemptions older than N days (owner only)")]
    [RequireOwner]
    public async Task DbPrune([Summary("days", "days to keep (default 30)")] int days = 30)
    {
        await DeferAsync();
        var deleted = await _dbService.PruneOldRedemptionsAsync(days);
        await FollowupAsync($"[OK] Deleted {deleted} redemptions older than {days} days.", ephemeral: true);
    }

    // ========== DB STATS ==========
    [SlashCommand("db_stats", "show database statistics (owner only)")]
    [RequireOwner]
    public async Task DbStats()
    {
        await DeferAsync();
        var size = await _dbService.GetDatabaseSizeAsync();
        var redemptions = await _dbService.GetAllRedemptionsAsync();
        var users = await _dbService.GetTotalAuthorizedUsersAsync();
        await FollowupAsync($"**Database Stats**\n[INFO] Size: {size / 1024} KB\n[INFO] Authorized users: {users}\n[INFO] Total redemptions: {redemptions.Count}", ephemeral: true);
    }
}