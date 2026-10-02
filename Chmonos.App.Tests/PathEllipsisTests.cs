using Chmonos.App.Controls;

namespace Chmonos.App.Tests;

/// <summary>
/// 長いパスの間を省く決まり（公開前の点検 2026-10-01：初回の窓で、深い場所を選ぶと最後のフォルダ名が切れて見えなかった）。
/// 幅は字の数で測る（画面では字の幅で測る。決まりは同じ）
/// </summary>
public class PathEllipsisTests
{
    private static string Fit(string path, int width) => PathEllipsis.Fit(path, text => text.Length <= width);

    [Fact]
    public void 収まればそのまま()
        => Assert.Equal(@"C:\Users\a\Chmonos", Fit(@"C:\Users\a\Chmonos", 40));

    [Fact]
    public void 収まらなければ_頭と最後のフォルダ名を残して間を省く()
    {
        var shown = Fit(@"D:\とても長いフォルダ\さらに長いフォルダ\素材\Chmonos", 20);

        Assert.Equal(@"D:\とても長いフォル…\Chmonos", shown);
        Assert.Equal(20, shown.Length);
    }

    [Fact]
    public void 最後の区切りが末尾にあっても_その前の名前を残す()
        => Assert.EndsWith(@"…\Chmonos\", Fit(@"D:\とても長いフォルダ\さらに長いフォルダ\Chmonos\", 16));

    [Fact]
    public void 頭を1字も残せなければ_最後のフォルダ名だけを残す()
        => Assert.Equal(@"…\Chmonos", Fit(@"D:\とても長いフォルダ\Chmonos", 9));

    [Fact]
    public void 最後のフォルダ名も収まらなければ_その後ろを残す()
        => Assert.Equal("…onos", Fit(@"D:\とても長いフォルダ\Chmonos", 5));

    [Fact]
    public void 区切りの無い名前は後ろを省く()
        => Assert.Equal("とても長…", Fit("とても長い名前のファイル", 5));

    /// <summary>
    /// 省いたときの全文の吹き出しは、欄と同じ字体で出す（2026-10-01：吹き出しの既定の日本語の字体では「\」が「¥」に見えた）。
    /// </summary>
    [Fact]
    public Task 省いたときの吹き出しは_欄と同じ字体で全文を出す() => Support.UiThread.Run(() =>
    {
        const string full = @"C:\Users\作り物\AppData\Local\とても長いフォルダの名前\さらに長いフォルダの名前\Chmonos";
        var line = new PathLine { FontFamily = new System.Windows.Media.FontFamily("Consolas"), Path = full };

        line.Measure(new System.Windows.Size(120, 30));
        line.Arrange(new System.Windows.Rect(0, 0, 120, 30));
        line.UpdateLayout();

        Assert.Contains(PathEllipsis.Mark, line.Text);
        // 吹き出しの型は中の字に日本語の字体を書式で当てるので、吹き出しではなく中の字に字体を直に持たせる
        var tip = Assert.IsType<System.Windows.Controls.TextBlock>(line.ToolTip);
        Assert.Equal(full, tip.Text);
        Assert.Equal(line.FontFamily, tip.FontFamily);
        Assert.Equal(System.Windows.BaseValueSource.Local, System.Windows.DependencyPropertyHelper.GetValueSource(tip, System.Windows.Controls.TextBlock.FontFamilyProperty).BaseValueSource);
    });
}
