using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存先に選んだ場所から、実際に使う場所を決める。
/// 友人は「選んだフォルダの中に1階層作ってくれる」と思って選んでいた。
/// </summary>
public sealed class StoreRootForTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "store-root-for-" + Guid.NewGuid().ToString("N"));

    public StoreRootForTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void CreatesAFolderInsideAnOrdinaryPlace()
    {
        File.WriteAllText(Path.Combine(_dir, "他のファイル.txt"), "");

        Assert.Equal(Path.Combine(_dir, StoreLocation.FolderName), StoreLocation.RootFor(_dir));
    }

    /// <summary>既にライブラリがある場所は、そのまま開く（中に作ると別のライブラリになってしまう）。</summary>
    [Fact]
    public void UsesAnExistingLibraryAsIs()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");

        Assert.Equal(_dir, StoreLocation.RootFor(_dir));
    }

    /// <summary>名前が既に「Chmonos」なら、さらに中へは作らない。</summary>
    [Fact]
    public void DoesNotNestTheFolderTwice()
    {
        var named = Path.Combine(_dir, StoreLocation.FolderName);
        Directory.CreateDirectory(named);

        Assert.Equal(named, StoreLocation.RootFor(named + Path.DirectorySeparatorChar));
    }
}

/// <summary>書きかけで残った一時ファイルを片付ける。</summary>
public sealed class StaleTemporaryFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stale-tmp-" + Guid.NewGuid().ToString("N"));

    public StaleTemporaryFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>
    /// 古い .tmp だけを消す。今まさに書いているかもしれない新しいものと、本体は残す。
    /// 消すのはアプリの一時ファイルの形の名前だけで、使う人や別の道具が置いた .tmp は古くても残す（外部の点検 2026-10-07）
    /// </summary>
    [Fact]
    public void DeletesOnlyOldTemporaryFiles()
    {
        var old = Path.Combine(_dir, "9000001.h2.html.1a2b-3.tmp");
        var oldImage = Path.Combine(_dir, "a.webp.0123456789abcdef0123456789abcdef.tmp");
        var fresh = Path.Combine(_dir, "1.json.1a2b-4.tmp");
        var real = Path.Combine(_dir, "1.json");
        var someoneElses = Path.Combine(_dir, "メモ.tmp");
        foreach (var path in new[] { old, oldImage, fresh, real, someoneElses })
        {
            File.WriteAllText(path, "");
        }

        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(oldImage, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(someoneElses, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(2, JsonStore.DeleteStaleTemporaryFiles(_dir));
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(oldImage));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(real));
        Assert.True(File.Exists(someoneElses));
    }

    [Fact]
    public void IgnoresAMissingFolder()
    {
        Assert.Equal(0, JsonStore.DeleteStaleTemporaryFiles(Path.Combine(_dir, "無い")));
    }
}
