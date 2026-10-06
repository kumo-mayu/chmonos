using System.Buffers.Binary;
using System.Text;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 寸法がとても大きい画像は、頭だけ読んで断る（外部の点検 2026-10-06）。
/// 本当に画素を作ると試験が重いので、PNG の頭（IHDR）だけを組んだ作り物で確かめる
/// </summary>
public sealed class ImageLimitsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-image-limits-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public ImageLimitsTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
    }

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

    /// <summary>寸法だけを書いた PNG の頭（署名・IHDR・IEND）。画素の中身は無い。</summary>
    internal static byte[] PngHeaderOnly(int width, int height) => Png(width, height, withPixels: false);

    /// <summary>
    /// 灰色1色の PNG。中身（IDAT）は0の行を圧縮した物なので、9,000×9,000 でも数百KBになる。
    /// 直しが無いと、これを原寸で復号して保存まで進む（直しを外すと試験が落ちることを確かめた）
    /// </summary>
    internal static byte[] GrayPng(int width, int height) => Png(width, height, withPixels: true);

    private static byte[] Png(int width, int height, bool withPixels)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // 1色8ビット
        ihdr[9] = 0;  // 灰色
        WriteChunk(stream, "IHDR", ihdr);
        if (withPixels)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[width + 1];  // 先頭は行の濾し方（0＝無し）
                for (var y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            WriteChunk(stream, "IDAT", compressed.ToArray());
        }

        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    [Fact]
    public void JudgesFromTheHeaderAlone()
    {
        Assert.True(ImageLimits.IsTooLarge(Image.Identify(PngHeaderOnly(20_000, 20_000))));
        Assert.True(ImageLimits.IsTooLarge(Image.Identify(PngHeaderOnly(8193, 8192))));
        Assert.False(ImageLimits.IsTooLarge(Image.Identify(PngHeaderOnly(8192, 8192))));
        Assert.False(ImageLimits.IsTooLarge(Image.Identify(PngHeaderOnly(3000, 3000))));
    }

    [Fact]
    public async Task AddingAHugeImageIsRefusedBeforeDecoding()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "100",
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "商品" },
        });

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings();
        var images = new ImagePipeline(client, _paths, settings);
        var commands = new Commands.CommandHandler(null!, new ItemService(_store, client, images, settings));

        var result = await commands.ExecuteAsync(new Commands.UiCommand.AddUserImage("100", GrayPng(9000, 9000)));

        Assert.IsNotType<Commands.CommandResult.UserImageAdded>(result);
        Assert.Empty(Directory.Exists(_paths.ItemImagesDir("100")) ? Directory.GetFiles(_paths.ItemImagesDir("100")) : []);
    }
}
