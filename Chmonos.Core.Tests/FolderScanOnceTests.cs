using System.IO.Enumeration;
using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 走査を「木を1回だけたどる」形に変えた（2026-09-24）。**見つかる物は前と同じ**であることを、
/// 前の作り（フォルダを全部並べ・親のファイルを並べ直し・展開先を測り直し・ファイルを全部並べて1件ずつ問い直す）を
/// ここに写した物と、並びまで含めて突き合わせて確かめる。
/// </summary>
public sealed class FolderScanOnceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-scan-once-" + Guid.NewGuid().ToString("N"));

    public FolderScanOnceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, int size)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public void FindsTheSameThingsInTheSameOrderAsBefore()
    {
        Write("ショップA/商品1/本体.zip", 10);
        Write("ショップA/商品1/説明.pdf", 20);
        Write("ショップA/商品1/本体/Assets/a.png", 30);
        Write("ショップA/商品1/本体/Assets/b.fbx", 40);
        Write("ショップA/商品1/本体/入れ子.zip", 50);
        Write("ショップA/商品1/本体/入れ子/c.psd", 60);
        Write("ショップA/商品1/本体/入れ子/d.txt", 70);
        Write("ショップA/商品2/衣装 v1.2.zip", 80);
        Write("ショップA/商品2/衣装 v1.2/e.png", 90);
        Write("ショップB/f.mp3", 100);
        Write("ショップB/g.unitypackage", 110);
        Write("ショップB/深い/もっと深い/h.vrm", 120);
        Write("隠し.png", 130);
        Write("システム.png", 140);
        Write("システムのフォルダ/中.png", 150);
        Write("直下.zip", 160);
        Write("直下/中.png", 170);
        File.SetAttributes(Path.Combine(_root, "隠し.png"), FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(_root, "システム.png"), FileAttributes.System);
        File.SetAttributes(Path.Combine(_root, "システムのフォルダ"), FileAttributes.Directory | FileAttributes.System);

        var now = new FolderScanner().Scan(_root);
        var before = BeforeScan(_root);

        Assert.Equal(before.Files, now.Files.Select(file => (file.Path, file.SizeBytes, file.ModifiedAtUtc, file.Extension)));
        Assert.Equal(
            before.Unpacked,
            now.UnpackedFolders.Select(folder => (folder.Path, folder.ArchivePath, folder.FileCount, folder.TotalBytes)));
        Assert.Equal(before.Skipped, now.SkippedInsideUnpackedFolders);

        // 入れ子の展開先も前と同じく数える。中のファイルは外側にも内側にも数える
        Assert.Equal(4, now.UnpackedFolders.Count);
        Assert.DoesNotContain(now.Files, file => file.Path.Contains("システム", StringComparison.Ordinal));
        Assert.Contains(now.Files, file => file.Path.EndsWith("隠し.png", StringComparison.Ordinal));
    }

    [Fact]
    public void RootWithTrailingSeparatorGivesTheSamePaths()
    {
        Write("a/b.zip", 1);
        Write("a/b/c.png", 2);
        Write("a/d.png", 3);

        var plain = new FolderScanner().Scan(_root);
        var trailing = new FolderScanner().Scan(_root + Path.DirectorySeparatorChar);

        Assert.Equal(plain.Files.Select(file => file.Path), trailing.Files.Select(file => file.Path));
        Assert.Equal(plain.UnpackedFolders.Select(folder => folder.ArchivePath), trailing.UnpackedFolders.Select(folder => folder.ArchivePath));
    }

    // ── 前の作り（2026-09-23 の FolderScanner）をそのまま写した物 ──

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System,
    };

    private static IEnumerable<(string Path, FileAttributes Attributes)> Walk(string root, bool directories)
        => new FileSystemEnumerable<(string, FileAttributes)>(
            root,
            (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Attributes),
            RecursiveOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry)
                => entry.IsDirectory == directories && (entry.Attributes & FileAttributes.ReparsePoint) == 0,
            ShouldRecursePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        };

    private static (List<(string, long, DateTimeOffset, string)> Files, List<(string, string, int, long)> Unpacked, int Skipped) BeforeScan(string root)
    {
        var unpacked = new List<(string Path, string Archive, int Count, long Bytes)>();
        foreach (var (directory, _) in Walk(root, directories: true))
        {
            var parent = Path.GetDirectoryName(directory)!;
            var archive = UnpackedFolderDetector.FindMatchingArchive(
                Path.GetFileName(directory),
                Directory.GetFiles(parent).Select(Path.GetFileName).Select(name => name!));
            if (archive is null)
            {
                continue;
            }

            var count = 0;
            long bytes = 0;
            foreach (var (path, _) in Walk(directory, directories: false))
            {
                bytes += new FileInfo(path).Length;
                count++;
            }

            unpacked.Add((directory, Path.Combine(parent, archive), count, bytes));
        }

        var files = new List<(string, long, DateTimeOffset, string)>();
        var skipped = 0;
        foreach (var (path, attributes) in Walk(root, directories: false))
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension) || !FolderScanner.TargetExtensions.Contains(extension))
            {
                continue;
            }

            if (unpacked.Any(folder => path.StartsWith(folder.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            if (FolderScanner.IsOnlineOnly(attributes))
            {
                continue;
            }

            var info = new FileInfo(path);
            files.Add((path, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), extension.ToLowerInvariant()));
        }

        return (files, unpacked, skipped);
    }
}
