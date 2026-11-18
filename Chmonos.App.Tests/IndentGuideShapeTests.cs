using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダの木の字下げの縦線（open.md 99 行）。線の形は、今見えている平らな並びの深さだけから決まる。
/// 段 k の線は、その下に深さ k+1 の行が、深さ k 以下の行より前にまだある間だけ続き、最後の子で止まる。
/// </summary>
public class IndentGuideShapeTests
{
    // ドライブ(0) ─ 根(1) ─┬ A(2) ─ a1(3)
    //                       │       └ a2(3)
    //                       └ B(2)
    // ドライブ2(0)
    private static readonly int[] Depths = [0, 1, 2, 3, 3, 2, 0];

    [Fact]
    public void 最後の子では親からの線が止まり_その下の段の線も引かない()
    {
        var shapes = IndentGuideShape.Compute(Depths);

        // a1：段0（ドライブの下）は続かない（ドライブの子は根だけ）。段1（根の下）は B がまだあるので続く。段2（A の下）は a2 がまだある
        Assert.Equal([false, true, true], shapes[3].Continues);

        // a2：A の最後の子。段2 は止まる。段1 は B があるので続く
        Assert.Equal([false, true, false], shapes[4].Continues);

        // B：根の最後の子
        Assert.Equal([false, false], shapes[5].Continues);
    }

    [Fact]
    public void 開いたフォルダは_次の行が子のときだけ下へつなぐ()
    {
        var shapes = IndentGuideShape.Compute(Depths);

        Assert.True(shapes[0].OpensBelow);   // ドライブ → 根
        Assert.True(shapes[2].OpensBelow);   // A → a1
        Assert.False(shapes[4].OpensBelow);  // a2 → B（浅くなる）
        Assert.False(shapes[5].OpensBelow);  // B → ドライブ2
        Assert.False(shapes[6].OpensBelow);  // 最後の行
    }

    [Fact]
    public void 深さ0の行と空の並びは線を持たない()
    {
        var shapes = IndentGuideShape.Compute(Depths);
        Assert.Empty(shapes[0].Continues);
        Assert.Empty(IndentGuideShape.Compute([]));
    }

    [Fact]
    public Task フォルダの木の行は_見えている並びから線の形を持つ() => TestApp.Run(async app =>
    {
        // lib の下に a と d（d が lib の最後の子）。全部開いて並べる
        await app.AddItemAsync(Make.Item("9900801", "作り物のドレス").WithFiles(Make.File(app.NewFile(@"lib\a\dress.zip"))));
        await app.AddItemAsync(Make.Item("9900802", "作り物の靴").WithFiles(Make.File(app.NewFile(@"lib\d\shoes.zip"))));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        folders.ExpandAllCommand.Execute(folders.Rows.First(row => row.Kind == FolderViewRowKind.Root));

        var expected = IndentGuideShape.Compute(folders.Rows.Select(row => row.Depth).ToList());
        Assert.Equal(expected.Select(shape => shape.Depth), folders.Rows.Select(row => row.Guides.Depth));
        Assert.Equal(expected.Select(shape => string.Join(",", shape.Continues)), folders.Rows.Select(row => string.Join(",", row.Guides.Continues)));

        // 最後の行（d の下の shoes.zip）は、親 d の線をこの行で止める
        Assert.False(folders.Rows[^1].Guides.Continues[^1]);
    });
}
