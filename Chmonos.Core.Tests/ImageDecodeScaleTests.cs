using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 画像を縮めながら復号する（2026-09-24）。前は元の大きさで復号してから縮めていた。
/// **保存される絵の大きさ（長辺の設定どおり・引き伸ばさない）は前と同じ**ことを、前の縮め方で出した大きさと突き合わせて確かめる。
/// </summary>
public sealed class ImageDecodeScaleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-decode-scale-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Encode(int width, int height, bool jpeg)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x * 255 / width), (byte)(y * 255 / height), 128);
                }
            }
        });
        using var stream = new MemoryStream();
        if (jpeg)
        {
            image.SaveAsJpeg(stream);
        }
        else
        {
            image.SaveAsPng(stream);
        }

        return stream.ToArray();
    }

    /// <summary>前の作り：元の大きさで復号し、長辺を超えていたら Max で縮める。</summary>
    private static Size BeforeSize(byte[] bytes, int maxEdge)
    {
        using var image = Image.Load(bytes);
        if (image.Width > maxEdge || image.Height > maxEdge)
        {
            image.Mutate(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxEdge, maxEdge) }));
        }

        return image.Size;
    }

    [Theory]
    [InlineData(3000, 2000, true)]
    [InlineData(2000, 3000, true)]
    [InlineData(3000, 384, true)]
    [InlineData(384, 1000, true)]
    [InlineData(1000, 385, true)]
    [InlineData(500, 100, true)]
    [InlineData(3001, 1999, true)]
    [InlineData(200, 150, true)]
    [InlineData(384, 384, true)]
    [InlineData(1600, 900, false)]
    [InlineData(100, 3000, false)]
    [InlineData(120, 80, false)]
    public async Task SavedSizeIsTheSameAsBefore(int width, int height, bool jpeg)
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { ModificationImageMaxEdgePixels = 384 };
        var pipeline = new ImagePipeline(client, paths, settings);
        var bytes = Encode(width, height, jpeg);

        var name = await pipeline.SaveModificationImageAsync("mod-0000a001", bytes);

        Assert.NotNull(name);
        var saved = Image.Identify(Path.Combine(paths.ModificationImagesDir("mod-0000a001"), name!));
        Assert.Equal(BeforeSize(bytes, 384), saved.Size);
    }

    /// <summary>原寸で保存する設定（長辺の上限なし）では縮めない。</summary>
    [Fact]
    public async Task OriginalSizeStaysOriginal()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var client = new OffUiThreadTests.OfflineClient();
        var pipeline = new ImagePipeline(client, paths, new AppSettings { SaveModificationImagesAtOriginalSize = true });

        var name = await pipeline.SaveModificationImageAsync("mod-0000a001", Encode(1200, 800, jpeg: true));

        Assert.Equal(new Size(1200, 800), Image.Identify(Path.Combine(paths.ModificationImagesDir("mod-0000a001"), name!)).Size);
    }
}
