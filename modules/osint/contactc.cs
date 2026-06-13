using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using atfot.core.services;
using atfot.models;

namespace atfot.modules.osint;

[Group("contact", "email and phone number investigation")]
public class ContactCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly ApiKeyService _apiKeyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Dictionary<ulong, (string summary, string? rawJson, string targetLookup)> _exportCache = new();

    public ContactCmd(
        KeyRedemptionService keyService,
        ApiKeyService apiKeyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export,
        IHttpClientFactory httpFactory)
    {
        _keyService = keyService;
        _apiKeyService = apiKeyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
        _httpFactory = httpFactory;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("lookup", "query communication vectors across databases")]
    public async Task ContactLookup(
        [Summary("vector", "target email address or phone number")] string vector)
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("[ERR] you need to redeem a master key first using `/redeem`.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"[WARN] please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        string encoded = Uri.EscapeDataString(vector);
        bool isEmail = vector.Contains('@');

        string dataSummary = $"**Vector:** `{vector}`\n\n";
        string? rawJson = null;

        if (isEmail)
        {
            dataSummary += "**Email Lookup Links:**\n" +
                $"[Hunter.io](https://hunter.io/try/search/{encoded})\n" +
                $"[HaveIBeenPwned](https://haveibeenpwned.com/unifiedsearch/{encoded})\n" +
                $"[Epieos](https://epieos.com/?q={encoded})\n" +
                $"[Holehe (CLI)](https://github.com/megadose/holehe)\n\n";

            dataSummary += $"[info] for comprehensive intelligence, run:\n" +
                $"  • `/osint sf {vector}` — SpiderFoot (breaches, reputation, Shodan, VT, etc.)\n" +
                $"  • `/osint pdl {vector}` — PeopleDataLabs person enrichment (requires API key)\n\n";
        }
        else
        {
            dataSummary += "**Phone Lookup Links:**\n" +
                $"[PhoneInfoga](https://github.com/sundowndev/PhoneInfoga) (CLI tool)\n" +
                $"[Numverify](https://numverify.com/) (API — requires key)\n" +
                $"[SpyDialer](http://spydialer.com/)\n\n";

            dataSummary += $"[info] for phone intelligence, run:\n" +
                $"  • `/osint sf {vector}` — SpiderFoot comprehensive scan\n" +
                $"  • `/osint phoneinfoga {vector}` — PhoneInfoga CLI (Docker only)\n";
        }

        var embed = _embed.CreateMonochromeEmbed("contact intelligence", dataSummary, "gray");
        var msg = await FollowupAsync(embed: embed);

        _exportCache[msg.Id] = (dataSummary, rawJson, vector);

        var hasJson = !string.IsNullOrEmpty(rawJson);
        var components = new ComponentBuilder()
            .WithButton("TXT", $"contact_export:{msg.Id}:txt", ButtonStyle.Secondary)
            .WithButton("JSON", $"contact_export:{msg.Id}:json", ButtonStyle.Secondary, disabled: !hasJson);
        await msg.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("contact_export:*:*", ignoreGroupNames: true)]
    public async Task HandleContactExport(string msgIdStr, string format)
    {
        await DeferAsync(ephemeral: true);
        if (!ulong.TryParse(msgIdStr, out var msgId) || !_exportCache.TryGetValue(msgId, out var data))
        {
            await FollowupAsync("Export data expired or not found. Run the command again.", ephemeral: true);
            return;
        }

        if (format == "json" && string.IsNullOrEmpty(data.rawJson))
        {
            await FollowupAsync("No raw JSON data to export.", ephemeral: true);
            return;
        }

        var dto = new ScanResultDto
        {
            TargetLookup = data.targetLookup,
            ModuleSource = "communications_intelligence",
            RawApiResponse = data.rawJson,
            Summary = data.summary
        };

        string filename = $"contact_{data.targetLookup.Replace(" ", "_").Replace("@", "_at_")}_{DateTime.Now:yyyyMMddHHmmss}";
        using var stream = format == "json"
            ? _export.BuildJsonStream(dto)
            : _export.BuildTextStream(dto);
        await FollowupWithFileAsync(stream, $"{filename}.{format}", $"Exported contact intelligence data.");
    }
}
