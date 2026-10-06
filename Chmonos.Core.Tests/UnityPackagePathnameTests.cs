using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Chmonos.Core.Resolution;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// unitypackage の pathname を、長さの上限を置いて読むこと（外部の点検 2026-10-06）。
/// 前は ReadLine で1行丸ごと読み、改行の無い巨大な pathname を掴むと、その大きさの文字列を作るまで読み続けた。
/// 上限を超える項目は扱わず、ほかの項目は前と同じに読む。
/// </summary>
public sealed class UnityPackagePathnameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-pathname-" + Guid.NewGuid().ToString("N")[..8]);

    public UnityPackagePathnameTests() => Directory.CreateDirectory(_root);

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

    /// <summary>pathname の中身をそのまま書いた unitypackage。</summary>
    private static byte[] Package(params byte[][] pathnames)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            for (var index = 0; index < pathnames.Length; index++)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{index:x32}/pathname")
                {
                    DataStream = new MemoryStream(pathnames[index]),
                });
            }
        }

        return memory.ToArray();
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    private IReadOnlyList<string> PathsInFolder(byte[] package)
    {
        var folder = Path.Combine(_root, "作り物の衣装");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Outfit.unitypackage"), package);
        var entry = new UnityPackageEntry(folder, "Outfit.unitypackage", package.Length) { InFolder = true };
        return [.. UnityHandoff.ReadAssetsFromDisk(entry).Select(asset => asset.Path)];
    }

    private static string Long(int chars) => "Assets/" + new string('a', chars - "Assets/".Length);

    [Fact]
    public void 改行の無い巨大な名前は扱わず_ほかの名前は読む()
    {
        var huge = Text("Assets/" + new string('x', 8 * 1024 * 1024));

        var paths = PathsInFolder(Package(Text("Assets/Sample/Outfit.prefab\n00"), huge, Text("Assets/Sample/Tex.png")));

        Assert.Equal(["Assets/Sample/Outfit.prefab", "Assets/Sample/Tex.png"], paths);
    }

    [Fact]
    public void 字数は上限ちょうどまで読み_超えた物は扱わない()
    {
        var paths = PathsInFolder(Package(
            Text(Long(UnityPackagePathname.MaxPathChars - 1)),
            Text(Long(UnityPackagePathname.MaxPathChars)),
            Text(Long(UnityPackagePathname.MaxPathChars + 1))));

        Assert.Equal([UnityPackagePathname.MaxPathChars - 1, UnityPackagePathname.MaxPathChars], paths.Select(path => path.Length));
    }

    [Fact]
    public void 項目の大きさは上限ちょうどまで読み_超えた物は読まない()
    {
        // 1行目は短く、2行目で大きさを合わせる（字数ではなく項目の大きさで断ることを見る）
        static byte[] Sized(string path, int bytes)
        {
            var head = Text(path + "\n");
            return [.. head, .. Enumerable.Repeat((byte)'0', bytes - head.Length)];
        }

        var paths = PathsInFolder(Package(
            Sized("Assets/A/at.prefab", UnityPackagePathname.MaxEntryBytes),
            Sized("Assets/B/over.prefab", UnityPackagePathname.MaxEntryBytes + 1)));

        Assert.Equal(["Assets/A/at.prefab"], paths);
    }

    [Fact]
    public void 頭のBOMと行末のCRは名前に入れない()
        => Assert.Equal(["Assets/Sample/Outfit.prefab"], PathsInFolder(Package(Text("﻿Assets/Sample/Outfit.prefab\r\n00"))));

    [Fact]
    public void 手掛かりの読み取りも巨大な名前を扱わない()
    {
        var zip = Path.Combine(_root, "with-package.zip");
        using (var stream = File.Create(zip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        using (var entry = archive.CreateEntry("Outfit.unitypackage").Open())
        {
            entry.Write(Package(
                Text("Assets/SampleAuthor/SampleProduct/Outfit.prefab"),
                Text("Assets/HugeAuthor/HugeProduct/" + new string('x', 8 * 1024 * 1024))));
        }

        var hints = UnityPackageInspector.Inspect(zip);

        Assert.Equal(["SampleAuthor"], hints.AuthorNamespaces);
    }
}
