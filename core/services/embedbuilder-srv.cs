using Discord;

namespace pewbot.core.services;

public class EmbedBuilderService
{
    private static readonly Color DarkBlack = new(0x0A, 0x0A, 0x0A);
    private static readonly Color IndustrialGray = new(0x55, 0x55, 0x55);
    private static readonly Color StarkWhite = new(0xFA, 0xFA, 0xFA);

    public Embed CreateMonochromeEmbed(string title, string description, string variant = "dark")
    {
        var color = variant.ToLower() switch
        {
            "white" => StarkWhite,
            "gray" => IndustrialGray,
            _ => DarkBlack
        };

        return new EmbedBuilder()
            .WithTitle($"</{title}> ")
            .WithDescription(description)
            .WithColor(color)
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "pewbot osint || made by thevirgindev")
            .Build();
    }

    public EmbedBuilder CreateMonochromeEmbedBuilder(string title, string variant = "dark")
    {
        var color = variant.ToLower() switch
        {
            "white" => StarkWhite,
            "gray" => IndustrialGray,
            _ => DarkBlack
        };

        return new EmbedBuilder()
            .WithTitle($"</{title}> ")
            .WithColor(color)
            .WithCurrentTimestamp()
            .WithFooter(f => f.Text = "ATFOT (All The Fucking OSINT Tools) || made by thevirgindev");
    }
}