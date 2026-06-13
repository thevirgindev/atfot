using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace atfot.core.services;

public class ImageService : IDisposable
{
    private readonly Image<Rgba32> _tpl;
    private readonly Font _font;

    public ImageService()
    {
        var baseDir = AppContext.BaseDirectory;
        var tplPath = Path.Combine(baseDir, "resources", "profile-lookup.jpg");
        if (!File.Exists(tplPath))
            throw new FileNotFoundException($"template not found: {tplPath}");
        _tpl = Image.Load<Rgba32>(tplPath);

        var fontPath = Path.Combine(baseDir, "resources", "JetBrainsMono-Bold.ttf");
        Font? font = null;
        if (File.Exists(fontPath))
        {
            try
            {
                var col = new FontCollection();
                var fam = col.Add(fontPath);
                font = fam.CreateFont(42, FontStyle.Bold);
            }
            catch
            {
                try { font = SystemFonts.CreateFont("Consolas", 42, FontStyle.Bold); }
                catch { try { font = SystemFonts.CreateFont("Arial", 42, FontStyle.Bold); } catch { throw; } }
            }
        }
        _font = font!;
    }

    // generates profile lookup image with username overlay
    public async Task<Stream> profileLookupImg(string username)
    {
        using var img = _tpl.Clone();
        var pos = new Point(390, 663);
        img.Mutate(ctx => ctx
            .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
            .DrawText(username, _font, Color.White, pos));
        var stream = new MemoryStream();
        await img.SaveAsJpegAsync(stream);
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        _tpl?.Dispose();
    }
}