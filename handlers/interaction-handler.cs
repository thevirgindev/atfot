using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

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
    }
}