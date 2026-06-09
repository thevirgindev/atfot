using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using pewbot.core.services;

namespace pewbot.modules.core;

public class ReportCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotConfig _config;
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;

    public ReportCmd(BotConfig config, KeyRedemptionService keyService, CooldownService cooldown)
    {
        _config = config;
        _keyService = keyService;
        _cooldown = cooldown;
    }

    [SlashCommand("report", "report a bug or request a feature (sent to bot owner)")]
    public async Task Report(
        [Summary("description", "describe the issue or request")] string description,
        [Summary("attachment", "optional file (screenshot, log)")] IAttachment? attachment = null)
    {
        if (!await _keyService.IsAuthorizedAsync(Context.User.Id.ToString()))
        {
            await RespondAsync("You need a master key to use this command.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Wait {remaining.TotalSeconds:F0}s.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        if (string.IsNullOrEmpty(_config.OwnerDiscordId) || !ulong.TryParse(_config.OwnerDiscordId, out var ownerId))
        {
            await RespondAsync("Owner not configured. Cannot send report.", ephemeral: true);
            return;
        }

        var owner = await Context.Client.GetUserAsync(ownerId);
        if (owner == null)
        {
            await RespondAsync("Owner not found.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("User Report")
            .WithDescription(description)
            .AddField("User", $"{Context.User.Username} ({Context.User.Id})")
            .AddField("Channel", Context.Channel.Name)
            .WithColor(new Color(0xFF5555))
            .WithCurrentTimestamp()
            .Build();

        try
        {
            if (attachment != null)
                await owner.SendFileAsync(attachment.Url, embed: embed);
            else
                await owner.SendMessageAsync(embed: embed);
            await RespondAsync("Report sent to the bot owner. Thank you!", ephemeral: true);
        }
        catch
        {
            await RespondAsync("Failed to send report. The owner may have DMs disabled.", ephemeral: true);
        }
    }
}