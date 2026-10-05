using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

public class UnpackedFolderRemoverTests : IDisposable
{
    private readonly string _root;

    public UnpackedFolderRemoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    /// <summary>zipとその展開先フォルダを並べて作る。</summary>
    private UnpackedFolder CreatePair(string stem, int fileCount = 2, int bytesPerFile = 16)
    {
        var archive = Path.Combine(_root, stem + ".zip");
        File.WriteAllBytes(archive, new byte[8]);

        var folder = Path.Combine(_root, stem);
        Directory.CreateDirectory(folder);
        for (var index = 0; index < fileCount; index++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"file{index}.png"), new byte[bytesPerFile]);
        }

        return new UnpackedFolder
        {
            Path = folder,
            ArchivePath = archive,
            FileCount = fileCount,
            TotalBytes = fileCount * bytesPerFile,
        };
    }

    /// <summary>実際に消す代わりに、呼ばれたパスを記録するだけの削除役。</summary>
    private static UnpackedFolderRemover Recording(List<string> deleted, IReadOnlyList<string>? registered = null)
        => new(
            (path, _) =>
            {
                deleted.Add(path);
                Directory.Delete(path, recursive: true);
                return Task.CompletedTask;
            },
            _ => Task.FromResult<IReadOnlyList<string>?>(registered ?? []));

    // ---- 商品に登録したフォルダは消さない（2026-10-05・file-lifecycle.md「気になった所」4）----
    // zip の隣のフォルダを商品として登録していると（zip が後から来た・「zipで登録し直す」の前）、
    // 名前が合うので展開先と見え、ごみ箱へ送っていた。送った間、その商品のフォルダは「見つからない」になる。

    [Fact]
    public async Task 商品に登録したフォルダは消さず_理由を返す()
    {
        var folder = CreatePair("作り物_1.0");
        var deleted = new List<string>();

        var results = await Recording(deleted, [folder.Path + Path.DirectorySeparatorChar]).RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Equal("商品に登録したフォルダです。削除しません。", results[0].Reason);
        Assert.Empty(deleted);
        Assert.True(Directory.Exists(folder.Path));
    }

    [Fact]
    public async Task 中に商品に登録したフォルダがあれば消さない()
    {
        var folder = CreatePair("作り物_1.0");
        var inner = Path.Combine(folder.Path, "中身");
        Directory.CreateDirectory(inner);
        var deleted = new List<string>();

        var results = await Recording(deleted, [inner.ToUpperInvariant()]).RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Equal("中に商品に登録したフォルダがあります。削除しません。", results[0].Reason);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task 商品に登録したフォルダの中にあれば消さない()
    {
        var folder = CreatePair("作り物_1.0");
        var deleted = new List<string>();

        var results = await Recording(deleted, [_root]).RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Equal("商品に登録したフォルダの中にあります。削除しません。", results[0].Reason);
        Assert.Empty(deleted);
    }

    /// <summary>名前の頭が同じだけの登録（"作り物_1.0" と "作り物_1.0.1"）では止めない。</summary>
    [Fact]
    public async Task 名前の頭が同じだけの登録では止めない()
    {
        var folder = CreatePair("作り物_1.0");
        var deleted = new List<string>();

        var results = await Recording(deleted, [folder.Path + ".1"]).RemoveAsync([folder, CreatePair("別の_2.0")]);

        Assert.All(results, result => Assert.True(result.Removed));
    }

    /// <summary>読めない商品の記録があると、どのフォルダを登録しているか分からないので消さない。</summary>
    [Fact]
    public async Task 登録を確かめきれないときは消さない()
    {
        var folder = CreatePair("作り物_1.0");
        var deleted = new List<string>();
        var remover = new UnpackedFolderRemover(
            (path, _) =>
            {
                deleted.Add(path);
                return Task.CompletedTask;
            },
            _ => Task.FromResult<IReadOnlyList<string>?>(null));

        var results = await remover.RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Equal("読めない商品の記録があり、商品に登録したフォルダか確かめられません。削除しません。", results[0].Reason);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task RemovesFolderWhenArchiveIsStillThere()
    {
        var folder = CreatePair("Kipfel_1.2.0", fileCount: 3, bytesPerFile: 32);
        var deleted = new List<string>();

        var results = await Recording(deleted).RemoveAsync([folder]);

        Assert.True(results[0].Removed);
        Assert.Equal(96, results[0].FreedBytes);
        Assert.Equal([folder.Path], deleted);
        Assert.False(Directory.Exists(folder.Path));
    }

    /// <summary>
    /// 展開元が無ければ中身を復元できない。検出時にはあっても、削除の時点で消えていることはあり得る。
    /// </summary>
    [Fact]
    public async Task RefusesWhenArchiveIsGone()
    {
        var folder = CreatePair("Kipfel_1.2.0");
        File.Delete(folder.ArchivePath);
        var deleted = new List<string>();

        var results = await Recording(deleted).RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Contains("アーカイブが見つかりません", results[0].Reason);
        Assert.Empty(deleted);
        Assert.True(Directory.Exists(folder.Path));
    }

    /// <summary>フォルダ名が変わっていたら、そのzipの展開先だと言えなくなる。</summary>
    [Fact]
    public async Task RefusesWhenFolderNameNoLongerMatchesArchive()
    {
        var folder = CreatePair("Kipfel_1.2.0");
        var renamed = Path.Combine(_root, "Kipfel_old");
        Directory.Move(folder.Path, renamed);

        var moved = new UnpackedFolder
        {
            Path = renamed,
            ArchivePath = folder.ArchivePath,
            FileCount = folder.FileCount,
            TotalBytes = folder.TotalBytes,
        };

        var deleted = new List<string>();
        var results = await Recording(deleted).RemoveAsync([moved]);

        Assert.False(results[0].Removed);
        Assert.Contains("一致しません", results[0].Reason);
        Assert.Empty(deleted);
        Assert.True(Directory.Exists(renamed));
    }

    /// <summary>zipが別の場所へ移っていたら、同じ名前でも対応を確認できない。</summary>
    [Fact]
    public async Task RefusesWhenArchiveIsInAnotherDirectory()
    {
        var folder = CreatePair("Kipfel_1.2.0");
        var elsewhere = Path.Combine(_root, "sub");
        Directory.CreateDirectory(elsewhere);
        var movedArchive = Path.Combine(elsewhere, "Kipfel_1.2.0.zip");
        File.Move(folder.ArchivePath, movedArchive);

        var entry = new UnpackedFolder
        {
            Path = folder.Path,
            ArchivePath = movedArchive,
            FileCount = folder.FileCount,
            TotalBytes = folder.TotalBytes,
        };

        var deleted = new List<string>();
        var results = await Recording(deleted).RemoveAsync([entry]);

        Assert.False(results[0].Removed);
        Assert.Contains("同じ場所にありません", results[0].Reason);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task RefusesWhenFolderIsAlreadyGone()
    {
        var folder = CreatePair("Kipfel_1.2.0");
        Directory.Delete(folder.Path, recursive: true);

        var results = await Recording([]).RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        Assert.Contains("見つかりません", results[0].Reason);
    }

    /// <summary>1件が拒否されても、残りは処理する。</summary>
    [Fact]
    public async Task ContinuesAfterARefusal()
    {
        var good = CreatePair("Good_1.0");
        var bad = CreatePair("Bad_1.0");
        File.Delete(bad.ArchivePath);

        var deleted = new List<string>();
        var results = await Recording(deleted).RemoveAsync([bad, good]);

        Assert.False(results[0].Removed);
        Assert.True(results[1].Removed);
        Assert.Equal([good.Path], deleted);
    }

    /// <summary>削除中の例外は握りつぶさず、その件の理由として残す。</summary>
    [Fact]
    public async Task ReportsDeletionFailureAsReason()
    {
        var folder = CreatePair("Kipfel_1.2.0");
        var remover = new UnpackedFolderRemover(
            (_, _) => throw new IOException("使用中です"),
            _ => Task.FromResult<IReadOnlyList<string>?>([]));

        var results = await remover.RemoveAsync([folder]);

        Assert.False(results[0].Removed);
        // 画面に出る理由なので、.NET の文ではなく原因の見当（中身はログへ）
        Assert.Equal(Chmonos.Core.Services.FailureText.Cause(new IOException("使用中です")), results[0].Reason);
        Assert.True(Directory.Exists(folder.Path));
    }
}
