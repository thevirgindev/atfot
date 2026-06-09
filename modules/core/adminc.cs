using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using pewbot.core.services;
using pewbot.core.storage;
using pewbot.utils;

namespace pewbot.modules.core;

[DefaultMemberPermissions(GuildPermission.Administrator)]
public class AdminCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly DatabaseService _dbService;
    private readonly BotConfig _botConfig;

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

    [SlashCommand("redeem", "redeem a master key to activate the bot")]
    public async Task RedeemKey([Summary("key", "the master key provided by the bot owner")] string key)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Please wait {remaining.TotalSeconds:F0} seconds...", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        var success = await _keyService.RedeemKeyAsync(key, Context.User.Id.ToString());
        if (success)
        {
            var user = Context.User;
            var accountCreatedAt = user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC");
            var avatarUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();

            await _dbService.LogRedemptionAsync(
                user.Id.ToString(), user.Username, user.GlobalName, avatarUrl, accountCreatedAt, key);

            if (!string.IsNullOrEmpty(_botConfig.OwnerDiscordId) && ulong.TryParse(_botConfig.OwnerDiscordId, out var ownerUlong))
            {
                var owner = await Context.Client.GetUserAsync(ownerUlong);
                if (owner != null)
                {
                    var notifyEmbed = _embed.CreateMonochromeEmbed("New Key Redemption",
                        $"**User:** {user.Username} (ID: {user.Id})\n**Global Name:** {user.GlobalName ?? "None"}\n" +
                        $"**Account Created:** {accountCreatedAt}\n**Avatar:** [Click here]({avatarUrl})\n" +
                        $"**Key Used:** `{key}`\n**Redeemed At:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", "white");
                    try { await owner.SendMessageAsync(embed: notifyEmbed); } catch { }
                }
            }

            var embed = _embed.CreateMonochromeEmbed("key redemption", "Master key redeemed successfully! You now have full access to the bot.", "white");
            await RespondAsync(embed: embed, ephemeral: true);
        }
        else
        {
            var embed = _embed.CreateMonochromeEmbed("key redemption", "Invalid or already used master key.", "gray");
            await RespondAsync(embed: embed, ephemeral: true);
        }
    }

    [SlashCommand("setapikey", "add an API key for a service (optionally as default)")]
    public async Task SetApiKey(
        [Summary("service", "service name (e.g., shodan, hunter)")] string service,
        [Summary("apikey", "the API key")] string apiKey,
        [Summary("default", "set as default? (true/false)")] bool isDefault = false)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        if (!await EnsureAuthorized())
        {
            await RespondAsync("You must redeem a master key first.", ephemeral: true);
            return;
        }

        var id = await _apiKeyService.AddApiKeyAsync(Context.User.Id.ToString(), service, apiKey, isDefault);
        await RespondAsync($"✅ API key for **{service}** added (ID: {id}). {Sarcasm.Get()}", ephemeral: true);
    }

    [SlashCommand("removeapikey", "remove an API key by its ID")]
    public async Task RemoveApiKey(
        [Summary("service", "service name")] string service,
        [Summary("keyid", "the key ID (from /mykeys)")] int keyId)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        if (!await EnsureAuthorized())
        {
            await RespondAsync("Redeem a master key first.", ephemeral: true);
            return;
        }

        var success = await _apiKeyService.RemoveApiKeyAsync(Context.User.Id.ToString(), keyId);
        if (success)
            await RespondAsync($"🗑️ Removed API key ID {keyId}. {Sarcasm.Get()}", ephemeral: true);
        else
            await RespondAsync("❌ Key not found or you don't own it.", ephemeral: true);
    }

    [SlashCommand("setdefaultkey", "set an existing API key as the default for its service")]
    public async Task SetDefaultKey(
        [Summary("service", "service name (e.g., shodan, hunter)")] string service,
        [Summary("keyid", "the key ID (from /mykeys)")] int keyId)
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        if (!await EnsureAuthorized())
        {
            await RespondAsync("You must redeem a master key first.", ephemeral: true);
            return;
        }

        var success = await _apiKeyService.SetDefaultKeyAsync(Context.User.Id.ToString(), service, keyId);
        if (success)
            await RespondAsync($"✅ Key ID {keyId} is now the default for **{service}**. {Sarcasm.Get()}", ephemeral: true);
        else
            await RespondAsync($"❌ Could not set key ID {keyId} as default. Make sure it belongs to you and the service matches.", ephemeral: true);
    }

    [SlashCommand("mykeys", "show all your stored API keys (masked)")]
    public async Task MyKeys()
    {
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        if (!await EnsureAuthorized())
        {
            await RespondAsync("Redeem a master key first.", ephemeral: true);
            return;
        }

        var grouped = await _apiKeyService.GetAllUserKeysGroupedAsync(Context.User.Id.ToString());
        if (grouped.Count == 0)
        {
            await RespondAsync("No API keys stored. Use `/setapikey` to add one.", ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("```");
        foreach (var (service, keys) in grouped)
        {
            sb.AppendLine($"\n{service.ToUpper()}:");
            foreach (var (id, masked, isDefault) in keys)
                sb.AppendLine($"  [{id}] {masked}{(isDefault ? " (default)" : "")}");
        }
        sb.AppendLine("```");
        var embed = _embed.CreateMonochromeEmbed("your api keys", sb.ToString(), "dark");
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("genkey", "generate a new master key (owner only)")]
    [RequireOwner]
    public async Task GenerateMasterKey()
    {
        var newKey = await _keyService.GenerateNewKeyAsync();
        var embed = _embed.CreateMonochromeEmbed("new master key", $"`{newKey}`\n\nShare this key with a user to grant access.", "white");
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("status", "change bot presence (owner only)")]
    [RequireOwner]

    public async Task SetStatus(
        [Summary("status", "online, idle, dnd, invisible")] string status = "idle",
        [Summary("activity", "playing activity text")] string activity = "ATFOT – All The Fucking OSINT Tools | /help")
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
            if (!string.IsNullOrEmpty(activity))
                await client.SetGameAsync(activity);
            else
                await client.SetGameAsync(null);
        }
        var embed = _embed.CreateMonochromeEmbed("bot status", $"Status: {presenceStatus}\nActivity: {activity}", "white");
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("redemptions", "view all key redemptions (owner only)")]
    [RequireOwner]
    public async Task ViewRedemptions()
    {
        var redemptions = await _dbService.GetAllRedemptionsAsync();
        if (redemptions.Count == 0)
        {
            await RespondAsync("No redemptions yet.", ephemeral: true);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("**Key Redemption Log:**\n");
        foreach (var r in redemptions)
        {
            sb.AppendLine($"**User:** {r.username} (ID: {r.discordId})");
            sb.AppendLine($"**Global Name:** {r.globalName ?? "None"}");
            sb.AppendLine($"**Key:** `{r.redeemedKey}`");
            sb.AppendLine($"**Time:** {r.redeemedAt}");
            sb.AppendLine("---");
        }
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

    [SlashCommand("rmk", "revoke a user's access by removing their master key (owner only)")]
    [RequireOwner]
    public async Task RevokeMasterKey([Summary("userid", "Discord user ID to revoke")] string userId)
    {
        if (!ulong.TryParse(userId, out _))
        {
            await RespondAsync("Invalid user ID.", ephemeral: true);
            return;
        }
        var success = await _dbService.RevokeUserAccessAsync(userId);
        if (success)
            await RespondAsync($"✅ Revoked access for user {userId}. {Sarcasm.Get()}", ephemeral: true);
        else
            await RespondAsync($"❌ User {userId} not found or already revoked.", ephemeral: true);
    }
}