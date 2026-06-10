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
        [Summary("vector", "target email address or phone number")] string vector,
        [Summary("export", "export format (none, txt, json)")] string export = "none")
    {
        if (!await EnsureAuthorized())
        {
            await RespondAsync("🔒 You need to redeem a master key first using `/redeem`.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
            await RespondAsync($"⏳ Please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var dto = new ScanResultDto
        {
            TargetLookup = vector,
            ModuleSource = "communications_intelligence"
        };

        var links = new List<string>();
        bool isEmail = vector.Contains('@');
        string encoded = Uri.EscapeDataString(vector);

        if (isEmail)
        {
            links.Add($"[Hunter.io](https://hunter.io/try/search/{encoded})");
            links.Add($"[EmailRep](https://emailrep.io/{encoded})");
            links.Add($"[HaveIBeenPwned](https://haveibeenpwned.com/unifiedsearch/{encoded})");
            links.Add($"[Epieos](https://epieos.com/?q={encoded})");
            links.Add($"[Holehe (CLI)](https://github.com/megadose/holehe)");

            var emailRepKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), "emailrep");
            if (!string.IsNullOrEmpty(emailRepKey))
            {
                try
                {
                    var client = _httpFactory.CreateClient();
                    client.DefaultRequestHeaders.Add("Key", emailRepKey);
                    var response = await client.GetAsync($"https://emailrep.io/{encoded}");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        dto.RawApiResponse = json;
                        var obj = JObject.Parse(json);
                        var reputation = obj["reputation"]?.ToString() ?? "unknown";
                        dto.ExtractedData["emailrep_reputation"] = reputation;
                        dto.Summary = $"EmailRep reputation: {reputation}. {obj["details"]?["num_delivered"]} emails delivered.";
                    }
                    else
                    {
                        dto.ExtractedData["emailrep_error"] = $"HTTP {response.StatusCode}";
                    }
                }
                catch (Exception ex)
                {
                    dto.ExtractedData["emailrep_exception"] = ex.Message;
                }
            }
            else
            {
                dto.ExtractedData["emailrep"] = "No API key. Use /setapikey emailrep <key> to enable automated checks.";
            }
        }
        else
        {
            links.Add($"[Truecaller](https://www.truecaller.com/search/global/{encoded})");
            links.Add($"[PhoneInfoga](https://github.com/sundowndev/PhoneInfoga) (CLI tool)");
            links.Add($"[Numverify](https://numverify.com/) (API)");
            links.Add($"[SpyDialer](http://spydialer.com/)");

            var truecallerKey = await _apiKeyService.GetApiKeyAsync(Context.User.Id.ToString(), "truecaller");
            if (!string.IsNullOrEmpty(truecallerKey))
            {
                dto.ExtractedData["truecaller"] = "API key present, but manual link recommended due to restrictions.";
            }
            else
            {
                dto.ExtractedData["truecaller"] = "No API key. Use link for manual lookup.";
            }
        }

        dto.DeepLinks = links;

        var description = $"**Vector:** `{vector}`\n\n**Investigation Links:**\n{string.Join("\n", links)}";
        var embed = _embed.CreateMonochromeEmbed("contact intelligence", description, "gray");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "contact.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "contact.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }
}