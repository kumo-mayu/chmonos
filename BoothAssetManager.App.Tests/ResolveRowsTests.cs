using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 未確定の一覧の行を組む所（<see cref="ResolveViewModel.BuildRows"/>）。ナビの札の数え直しも同じ関数で組む。
///
/// フォルダの中を見る所（展開した中身の見分け）は、1回の組み立ての間だけ列挙を覚えるようにした（2026-09-30。
/// 前はフォルダごとに親を6段たどって列挙し直していた）。覚えても、どの行が展開した中身か・束ねる根がどこかは変わらないことを確かめる。
/// </summary>
public class ResolveRowsTests
{
    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public Task 行を組むと_目印のあるフォルダの下だけが展開した中身になり_根は1段外側になる() => TestApp.Run(app =>
    {
        // 親は6段までたどる。5段掘った中に置けば、試験の置き場より上（実マシンの一時フォルダ）の中身に答えが左右されない
        const string inside = @"1\2\3\4\5";
        var looseA = app.NewFile(inside + @"\loose\000\a.bin");
        var looseB = app.NewFile(inside + @"\loose\001\b.bin");
        var looseC = app.NewFile(inside + @"\loose\001\c.bin");
        var texture = app.NewFile(inside + @"\outfit_v1\outfit\texture\t.png");
        var mask = app.NewFile(inside + @"\outfit_v1\outfit\texture\mask\m.png");
        var package = app.NewFile(inside + @"\outfit_v1\outfit\outfit.unitypackage");
        var importFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(looseA)))!;
        var unpacked = System.IO.Path.Combine(importFolder, "outfit_v1");

        List<UnresolvedFile> files = [.. new[] { looseA, looseB, looseC, texture, mask, package }.Select(Unresolved)];
        var built = ResolveViewModel.BuildRows(files, new HashSet<string>(StringComparer.OrdinalIgnoreCase), [importFolder]);

        var rows = built.Rows.ToDictionary(row => row.File.Paths[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(6, rows.Count);
        Assert.Equal(0, built.HiddenByRegisteredZip);

        foreach (var loose in new[] { looseA, looseB, looseC })
        {
            Assert.False(rows[loose].IsArchiveContent);
            Assert.Null(rows[loose].ProductFolder);
            Assert.Null(rows[loose].UnpackRoot);
        }

        foreach (var content in new[] { texture, mask, package })
        {
            Assert.True(rows[content].IsArchiveContent);
            Assert.Equal(System.IO.Path.Combine(unpacked, "outfit"), rows[content].ProductFolder);
            Assert.Equal(unpacked, rows[content].UnpackRoot);
            Assert.Contains("outfit.unitypackage", rows[content].ContentReason);
        }

        // 見分けはフォルダごとに1つ控える（この後の操作が使う）。ファイルのあるフォルダは5つ
        Assert.Equal(5, built.Judgements.Count);

        // 登録する回数：ばらの3件＋展開したフォルダ1つ
        Assert.Equal(4, Core.Scanning.UnresolvedUnits.Count(built.Rows.Select(row => row.UnitKey)));
        return Task.CompletedTask;
    });

    [Fact]
    public Task 行を組み直すたびに_今のフォルダの中身で見分ける() => TestApp.Run(app =>
    {
        const string inside = @"1\2\3\4\5";
        var texture = app.NewFile(inside + @"\outfit\texture\t.png");
        var importFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(texture)))!;
        List<UnresolvedFile> files = [Unresolved(texture)];
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.False(Assert.Single(ResolveViewModel.BuildRows(files, owned, [importFolder]).Rows).IsArchiveContent);

        // 目印が後から置かれた（zip を展開し直した等）。前の組み立ての見分けを持ち越さない
        app.NewFile(inside + @"\outfit\outfit.unitypackage");

        Assert.True(Assert.Single(ResolveViewModel.BuildRows(files, owned, [importFolder]).Rows).IsArchiveContent);
        return Task.CompletedTask;
    });
}
