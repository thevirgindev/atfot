using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using atfot.core.services;
using atfot.core.storage;

namespace atfot.handlers;

public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;

    public InteractionHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
    }

    public async Task InitializeAsync()
    {
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        Log.Information("All modules loaded successfully.");

        _client.Ready += async () =>
        {
            await _interactions.RegisterCommandsGloballyAsync(true);
            Log.Information("Slash commands registered globally.");
        };
        
        _client.InteractionCreated += async (interaction) =>
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(context, _services);
            if (!result.IsSuccess)
            {
                Log.Error($"Command execution failed: {result.ErrorReason}");
            }
        };

        _client.MessageReceived += async (message) =>
        {
            if (message is not SocketUserMessage userMsg || message.Author.IsBot || message.Author.IsWebhook)
                return;

            var userId = userMsg.Author.Id.ToString();
            
            try
            {
                var keySvc = _services.GetRequiredService<KeyRedemptionService>();
                if (!await keySvc.IsAuthorizedAsync(userId))
                    return;

                var settingsSvc = _services.GetRequiredService<SettingsService>();
                var userSettings = await settingsSvc.GetUserSettingsAsync(userId);

                bool isDm = userMsg.Channel is Discord.IDMChannel;
                bool isMentioned = userMsg.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id);
                bool isAiChatEnabled = userSettings.AiChatEnabled;

                if (isAiChatEnabled || isDm || isMentioned)
                {
                    var cd = _services.GetRequiredService<CooldownService>();
                    if (cd.IsOnCooldown(userId, out _))
                        return;

                    cd.SetUsed(userId);

                    var content = userMsg.Content;
                    if (isMentioned)
                    {
                        var botMention = $"<@{_client.CurrentUser.Id}>";
                        var botMentionNick = $"<@!{_client.CurrentUser.Id}>";
                        content = content.Replace(botMention, "").Replace(botMentionNick, "").Trim();
                    }

                    if (string.IsNullOrWhiteSpace(content))
                        return;

                    var apiKeySvc = _services.GetRequiredService<ApiKeyService>();
                    var apiKey = await apiKeySvc.GetApiKeyAsync(userId, "pollinations");
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        await userMsg.Channel.SendMessageAsync("[ERR] Please set a Pollinations API key first using `/setapikey`. You can obtain a free key at [pollinations.ai](https://pollinations.ai).", messageReference: new MessageReference(userMsg.Id));
                        return;
                    }

                    using var typing = userMsg.Channel.EnterTypingState();

                    var aiChat = _services.GetRequiredService<AiChatService>();
                    var sysPrompt = string.IsNullOrEmpty(userSettings.AiChatSystemPrompt)
                        ? "you are atfot's ai assistant. help with osint analysis, technical questions, and general knowledge. be concise and direct."
                        : userSettings.AiChatSystemPrompt;

                    var reply = await aiChat.chatAsync(userId, content, sysPrompt);

                    if (!string.IsNullOrEmpty(reply))
                    {
                        if (reply.Length > 2000)
                        {
                            var parts = reply.Length / 2000 + 1;
                            for (int i = 0; i < parts; i++)
                            {
                                var partText = reply.Substring(i * 2000, Math.Min(2000, reply.Length - i * 2000));
                                if (!string.IsNullOrWhiteSpace(partText))
                                {
                                    await userMsg.Channel.SendMessageAsync(partText, messageReference: new MessageReference(userMsg.Id));
                                }
                            }
                        }
                        else
                        {
                            await userMsg.Channel.SendMessageAsync(reply, messageReference: new MessageReference(userMsg.Id));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling message in direct chat handler");
            }
        };
    }
}