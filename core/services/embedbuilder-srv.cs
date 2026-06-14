using Discord;

namespace atfot.core.services;

public class EmbedBuilderService
{
    public const string FooterText = "atfot || made by @thevirgindev";

    private static readonly Color DarkBlack = new(0x0A, 0x0A, 0x0A);
    private static readonly Color IndustrialGray = new(0x55, 0x55, 0x55);
    private static readonly Color StarkWhite = new(0xFA, 0xFA, 0xFA);

    private static readonly Random _rng = new();

    private static Color ResolveColor(string variant) => variant.ToLower() switch
    {
        "white" => StarkWhite,
        "gray" => IndustrialGray,
        "random" => new[] { DarkBlack, IndustrialGray, StarkWhite }[_rng.Next(3)],
        _ => DarkBlack
    };

    public Embed CreateMonochromeEmbed(string title, string description, string variant = "dark")
    {
        return new EmbedBuilder()
            .WithTitle($"</{title}> ")
            .WithDescription(description)
            .WithColor(ResolveColor(variant))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = FooterText)
            .Build();
    }

    public EmbedBuilder CreateMonochromeEmbedBuilder(string title, string variant = "dark")
    {
        return new EmbedBuilder()
            .WithTitle($"</{title}> ")
            .WithColor(ResolveColor(variant))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = FooterText);
    }

    public Embed CreateStatusEmbed(string message, string tag = "info", string variant = "gray")
    {
        var label = tag.ToLower() switch
        {
            "err" or "error" => "[ERR]",
            "warn" or "warning" => "[WARN]",
            "done" or "ok" or "success" => "[DONE]",
            _ => "[INFO]"
        };

        return new EmbedBuilder()
            .WithDescription($"{label} {message}")
            .WithColor(ResolveColor(variant))
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = FooterText)
            .Build();
    }

    public Embed CreateLoadingEmbed(string message)
    {
        return new EmbedBuilder()
            .WithDescription($"[info] {message}")
            .WithColor(IndustrialGray)
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = FooterText)
            .Build();
    }

    public Embed CreateErrorEmbed(string message)
    {
        return new EmbedBuilder()
            .WithDescription($"[err] {message}")
            .WithColor(IndustrialGray)
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = FooterText)
            .Build();
    }
}