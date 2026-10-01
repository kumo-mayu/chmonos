using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// ナビの札の未確定の数え方（<see cref="UnresolvedUnitCounter"/>）。札は登録する回数で数えるので記録の中身が要るが、
/// 前は札の読み手と主画面がそれぞれ記録を読んでいた（未確定 8万件で1回 190〜270ms・68MB が2回）。
/// 読み手が読んだ物を使うこと・記録が変わっていない回は読まないこと・それでも数は同じになることを確かめる。
/// </summary>
public class UnresolvedUnitCounterTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = When,
        FirstSeenAt = When,
    };

    /// <summary>記録を保存先に書き、読み手に読ませる。数える側が記録を読んだ回数も数える。</summary>
    private sealed class Bench(TestApp app)
    {
        private readonly NavCountReader _reader = new(app.Store);

        public UnresolvedUnitCounter Counter { get; } = new();

        public int Loads { get; private set; }

        public Task WriteAsync(params UnresolvedFile[] files) => app.Store.Unresolved.SaveAsync([.. files]);

        public (int Count, bool Rebuilt) Count(IReadOnlyList<ItemRecord>? items = null, IReadOnlyList<string>? importFolders = null)
            => Count(_reader.ReadWithUnresolved(), items, importFolders);

        public (int Count, bool Rebuilt) Count(NavReading reading, IReadOnlyList<ItemRecord>? items = null, IReadOnlyList<string>? importFolders = null)
            => Counter.Count(
                reading,
                () =>
                {
                    Loads++;
                    return app.Store.Unresolved.Load();
                },
                items ?? [],
                importFolders ?? []);
    }

    [Fact]
    public Task 読み手が読んだ記録で数え_変わっていない回は記録を読まずに同じ数を返す() => TestApp.Run(async app =>
    {
        var bench = new Bench(app);
        await bench.WriteAsync(Unresolved(app.NewFile(@"a\one.zip")), Unresolved(app.NewFile(@"b\two.zip")));

        // 1回目：読み手が読んだ物で行を組む。数える側は読まない
        Assert.Equal((2, true), bench.Count());
        Assert.Equal(0, bench.Loads);

        // 2回目：記録の印が同じ。読まず、組まず、同じ数
        Assert.Equal((2, false), bench.Count());
        Assert.Equal((2, false), bench.Count());
        Assert.Equal(0, bench.Loads);
    });

    [Fact]
    public Task 記録が変われば数え直し_同じ一覧を書き直しただけなら行は組まない() => TestApp.Run(async app =>
    {
        var bench = new Bench(app);
        var one = Unresolved(app.NewFile(@"a\one.zip"));
        var two = Unresolved(app.NewFile(@"b\two.zip"));
        await bench.WriteAsync(one, two);
        Assert.Equal((2, true), bench.Count());

        // 同じ中身を書き直した（印は変わる）。読み手が読んだ物で中身を比べ、行は組まない
        await bench.WriteAsync(one, two);
        Assert.Equal((2, false), bench.Count());

        // 1件減った
        await bench.WriteAsync(two);
        Assert.Equal((1, true), bench.Count());
        Assert.Equal(0, bench.Loads);

        // 0件になったら、読まずに0
        await bench.WriteAsync();
        Assert.Equal((0, false), bench.Count());
        Assert.Equal(0, bench.Loads);
    });

    [Fact]
    public Task 記録が同じでも_商品の持ち物か取り込み元が変われば_記録を読んで数え直す() => TestApp.Run(async app =>
    {
        var bench = new Bench(app);
        var zip = app.NewFile(@"a\one.zip");
        await bench.WriteAsync(Unresolved(zip), Unresolved(app.NewFile(@"b\two.zip")));
        Assert.Equal((2, true), bench.Count());

        // 商品がファイルを1つ持った。読み手は記録を読み直さない（印が同じ）ので、数える側が読む
        var holder = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(app.NewFile(@"c\owned.zip")));
        Assert.Equal((2, true), bench.Count(items: [holder]));
        Assert.Equal(1, bench.Loads);

        // 材料が同じになれば、また読まない
        Assert.Equal((2, false), bench.Count(items: [holder]));
        Assert.Equal(1, bench.Loads);

        // 取り込み元が変わった
        Assert.Equal((2, true), bench.Count(items: [holder], importFolders: [System.IO.Path.GetDirectoryName(zip)!]));
        Assert.Equal(2, bench.Loads);
    });

    [Fact]
    public Task 数は_未確定の画面の行から数えた登録する回数と同じ() => TestApp.Run(async app =>
    {
        // 親は6段までたどるので、5段掘った中に置く（実マシンの一時フォルダの中身に左右されない）
        const string inside = @"1\2\3\4\5";
        var files = new[]
        {
            app.NewFile(inside + @"\loose\a.bin"),
            app.NewFile(inside + @"\loose\b.bin"),
            app.NewFile(inside + @"\outfit_v1\outfit\texture\t.png"),
            app.NewFile(inside + @"\outfit_v1\outfit\outfit.unitypackage"),
        }.Select(Unresolved).ToArray();
        var importFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(files[0].Paths[0]))!;
        var bench = new Bench(app);
        await bench.WriteAsync(files);

        var rows = ResolveViewModel.BuildRows([.. files], new HashSet<string>(StringComparer.OrdinalIgnoreCase), [importFolder]);
        var expected = Core.Scanning.UnresolvedUnits.Count(rows.Rows.Select(row => row.UnitKey));

        // ばらの2件＋展開したフォルダ1つ
        Assert.Equal(3, expected);
        Assert.Equal((expected, true), bench.Count(importFolders: [importFolder]));
    });
}
