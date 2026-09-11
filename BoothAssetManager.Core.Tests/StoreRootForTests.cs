using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

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

    /// <summary>名前が既に「BoothAssetManager」なら、さらに中へは作らない。</summary>
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

    /// <summary>古い .tmp だけを消す。今まさに書いているかもしれない新しいものと、本体は残す。</summary>
    [Fact]
    public void DeletesOnlyOldTemporaryFiles()
    {
        var old = Path.Combine(_dir, "9000001.h2.html.tmp");
        var fresh = Path.Combine(_dir, "1.json.tmp");
        var real = Path.Combine(_dir, "1.json");
        File.WriteAllText(old, "");
        File.WriteAllText(fresh, "");
        File.WriteAllText(real, "");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(1, JsonStore.DeleteStaleTemporaryFiles(_dir));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(real));
    }

    [Fact]
    public void IgnoresAMissingFolder()
    {
        Assert.Equal(0, JsonStore.DeleteStaleTemporaryFiles(Path.Combine(_dir, "無い")));
    }
}
