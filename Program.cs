using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Serilog;
using atfot.core.http;
using atfot.core.services;
using atfot.core.storage;
using atfot.handlers;

namespace atfot;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/bot.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            Log.Information("Starting ATFOT OSINT framework...");

            var services = ConfigureServices();
            var client = services.GetRequiredService<DiscordSocketClient>();
            var interactionHandler = services.GetRequiredService<InteractionHandler>();
            var config = services.GetRequiredService<BotConfig>();

            client.Log += OnClientLog;
            client.Ready += async () =>
            {
                if (!string.IsNullOrEmpty(config.OwnerDiscordId) && ulong.TryParse(config.OwnerDiscordId, out var ownerId))
                {
                    var owner = await client.GetUserAsync(ownerId);
                    if (owner != null)
                        Log.Information("Bot owner: {OwnerName} (ID: {OwnerId})", owner.Username, owner.Id);
                    else
                        Log.Warning("Owner Discord ID {OwnerId} not found.", config.OwnerDiscordId);
                }
                else
                {
                    Log.Warning("Owner Discord ID not set in environment variables.");
                }
                Log.Information("ATFOT is ready. Logged in as {BotName} (ID: {BotId})", client.CurrentUser.Username, client.CurrentUser.Id);
            };

            if (string.IsNullOrEmpty(config.DiscordToken))
            {
                Log.Error("Discord token not found. Please set DISCORD_TOKEN environment variable.");
                return;
            }

            await interactionHandler.InitializeAsync();
            await client.LoginAsync(TokenType.Bot, config.DiscordToken);
            await client.StartAsync();

            await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during bot startup");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false
        };

        return new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
            .AddSingleton<InteractionHandler>()
            .AddSingleton<BotConfig>()
            .AddSingleton<DatabaseService>()
            .AddSingleton<KeyRedemptionService>()
            .AddSingleton<ApiKeyService>()
            .AddSingleton<CooldownService>()
            .AddSingleton<ExportService>()
            .AddSingleton<EmbedBuilderService>()
            .AddSingleton<SocialMediaService>()
            .AddSingleton<ImageService>()
            .AddSingleton<SettingsService>()
            .AddSingleton<AiSummaryService>()
            .AddSingleton<AiChatService>()
            .AddHttpClient()
            .BuildServiceProvider();
    }

    private static Task OnClientLog(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                Log.Error(msg.ToString());
                break;
            case LogSeverity.Warning:
                Log.Warning(msg.ToString());
                break;
            case LogSeverity.Info:
                Log.Information(msg.ToString());
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                Log.Debug(msg.ToString());
                break;
        }
        return Task.CompletedTask;
    }
}