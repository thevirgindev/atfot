using System;
using System.IO;
using System.Threading.Tasks;

namespace atfot.core.services;

public class ImageService : IDisposable
{
    private readonly string _tplPath;

    public ImageService()
    {
        var baseDir = AppContext.BaseDirectory;
        _tplPath = Path.Combine(baseDir, "resources", "atfot.jpg");
        if (!File.Exists(_tplPath))
            throw new FileNotFoundException($"template not found: {_tplPath}");
    }

    // returns the raw profile lookup image as a stream (no text overlay)
    public Task<Stream> profileLookupImg()
    {
        Stream stream = new MemoryStream();
        using (var fs = new FileStream(_tplPath, FileMode.Open, FileAccess.Read))
            fs.CopyTo(stream);
        stream.Position = 0;
        return Task.FromResult(stream);
    }

    public void Dispose() { }
}