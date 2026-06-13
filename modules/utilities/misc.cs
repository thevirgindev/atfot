using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using atfot.core.services;
using atfot.models;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Linq;

namespace atfot.modules.utilities;

[Group("misc", "miscellaneous OSINT utilities")]
public class MiscCmd : InteractionModuleBase<SocketInteractionContext>
{
    private readonly KeyRedemptionService _keyService;
    private readonly CooldownService _cooldown;
    private readonly EmbedBuilderService _embed;
    private readonly ExportService _export;

    public MiscCmd(
        KeyRedemptionService keyService,
        CooldownService cooldown,
        EmbedBuilderService embed,
        ExportService export)
    {
        _keyService = keyService;
        _cooldown = cooldown;
        _embed = embed;
        _export = export;
    }

    private async Task<bool> EnsureAuthorized()
    {
        return await _keyService.IsAuthorizedAsync(Context.User.Id.ToString());
    }

    [SlashCommand("cyberchef", "basic encoding/hashing using CyberChef-like operations")]
    public async Task CyberChef(
        [Summary("input", "string to process")] string input,
        [Summary("operation", "md5, sha1, sha256, base64_encode, base64_decode, url_encode, url_decode")] string operation,
        [Summary("export", "export format (none, txt, json)")] string export = "none")
    {
        if (!await EnsureAuthorized())
        {
await RespondAsync("[ERR] you need to redeem a master key first.", ephemeral: true);
            return;
        }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var remaining))
        {
await RespondAsync($"[WARN] please wait {remaining.TotalSeconds:F0} seconds.", ephemeral: true);
            return;
        }
        _cooldown.SetUsed(Context.User.Id.ToString());

        await DeferAsync();

        var dto = new ScanResultDto
        {
            TargetLookup = input,
            ModuleSource = "cyberchef"
        };

        string result = "";
        try
        {
            switch (operation.ToLower())
            {
                case "md5":
                    result = BitConverter.ToString(MD5.HashData(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLower();
                    break;
                case "sha1":
                    result = BitConverter.ToString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLower();
                    break;
                case "sha256":
                    result = BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLower();
                    break;
                case "base64_encode":
                    result = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
                    break;
                case "base64_decode":
                    result = Encoding.UTF8.GetString(Convert.FromBase64String(input));
                    break;
                case "url_encode":
                    result = Uri.EscapeDataString(input);
                    break;
                case "url_decode":
                    result = Uri.UnescapeDataString(input);
                    break;
                default:
                    result = "Unknown operation. Available: md5, sha1, sha256, base64_encode, base64_decode, url_encode, url_decode";
                    break;
            }
        }
        catch (Exception ex)
        {
            result = $"Error: {ex.Message}";
        }

        dto.Summary = result;
        var description = $"**Operation:** {operation}\n**Input:** `{input}`\n**Result:** `{result}`";
        var embed = _embed.CreateMonochromeEmbed("cyberchef utility", description, "dark");

        if (export.ToLower() == "json")
            await FollowupWithFileAsync(_export.BuildJsonStream(dto), "cyberchef.json", embed: embed);
        else if (export.ToLower() == "txt")
            await FollowupWithFileAsync(_export.BuildTextStream(dto), "cyberchef.txt", embed: embed);
        else
            await FollowupAsync(embed: embed);
    }

}



