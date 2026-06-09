using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace pewbot.core.services;

public class ImageService : IDisposable
{
    private readonly Image<Rgba32> _templateImage;
    private readonly Font _targetFont;

    public ImageService()
    {
        var baseDir = AppContext.BaseDirectory;
        Console.WriteLine($"[ImageService] dir: {baseDir}, checked");

        var templatePath = Path.Combine(baseDir, "resources", "profile-lookup.jpg");
        Console.WriteLine($"[ImageService] Template path: {templatePath}, were good to go!");
        Console.WriteLine($"[ImageService] Template exists: {File.Exists(templatePath)}, were good to go!");

        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template image not found at {templatePath}");

        _templateImage = Image.Load<Rgba32>(templatePath);

        Font? font = null;
        var fontPath = Path.Combine(baseDir, "resources", "JetBrainsMono-Bold.ttf");
        Console.WriteLine($"[ImageService] Font path: {fontPath}, were good to go!");
        Console.WriteLine($"[ImageService] Font exists: {File.Exists(fontPath)}, were good to go!");

        if (File.Exists(fontPath))
        {
            try
            {
                var collection = new FontCollection();
                var family = collection.Add(fontPath);
                font = family.CreateFont(42, FontStyle.Bold);
                Console.WriteLine("[ImageService] Jetbrains font loaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImageService] Failed to load jb font: {ex.Message}");
            }
        }

        if (font is null)
        {
            try
            {
                font = SystemFonts.CreateFont("Consolas", 42, FontStyle.Bold);
                Console.WriteLine("[ImageService] Using Consolas font.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImageService] Failed to load Consolas: {ex.Message}");
                try
                {
                    font = SystemFonts.CreateFont("Arial", 42, FontStyle.Bold);
                    Console.WriteLine("[ImageService] Using Arial font.");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[ImageService] Failed to load Arial: {ex2.Message}");
                    throw;
                }
            }
        }

        _targetFont = font!;
    }

    public async Task<Stream> profilelookupImgAsync(string username)
    {
        Console.WriteLine($"[ImageService] profilelookup called for username: {username}");

        using var image = _templateImage.Clone();
        var position = new Point(390, 663);

        image.Mutate(ctx => ctx
            .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
            .DrawText(username, _targetFont, Color.White, position));

        var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream);
        stream.Position = 0;
        // image generation success
        return stream;
    }

    public void Dispose()
    {
        _templateImage?.Dispose();
    }
}