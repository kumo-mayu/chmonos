using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Tests.Support;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 左右2つの塊を1行に並べ、入り切らなければ片方を次の行へ送る帯（<see cref="SplitRowPanel"/>）の置き方。
/// 改変の詳細の上の帯は、幅 360 の欄で「この改変を削除」が右で切れていた——日付を下へ送り、ボタンは上の段の右端に残す。
/// </summary>
public class SplitRowPanelTests
{
    // 改変の詳細の上の帯に近い大きさ：左＝日付（190×14）、右＝ボタン（100×32）、間 14
    private static (SplitRowPanel Panel, FrameworkElement Left, FrameworkElement Right) Build(bool sendsLeftDown, double width)
    {
        var left = new Border { Width = 190, Height = 14, HorizontalAlignment = HorizontalAlignment.Right };
        var right = new Border { Width = 100, Height = 32 };
        var panel = new SplitRowPanel { Gap = 14, MinLeftWidth = 320, SendsLeftDown = sendsLeftDown };
        panel.Children.Add(left);
        panel.Children.Add(right);
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return (panel, left, right);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task 入り切る幅では_1行に並べ_左は右の塊のすぐ左で終わる(bool sendsLeftDown) => UiThread.Run(() =>
    {
        // 190＋14＋100＝304 ちょうど
        var (panel, left, right) = Build(sendsLeftDown, width: 304);

        Assert.False(panel.IsStacked);
        Assert.Equal(32, panel.DesiredSize.Height);
        Assert.Equal(new Rect(204, 0, 100, 32), LayoutInformation.GetLayoutSlot(right));

        // 左の塊の枠は「右の塊と間」を引いた残り。中身（右寄せ）は、その右端＝ボタンの 14 左で終わる
        Assert.Equal(190, LayoutInformation.GetLayoutSlot(left).Right);
    });

    [Fact]
    public Task 入り切らない幅で_左を送る指定なら_右は上の段の右端に残り_左は下の段へ行く() => UiThread.Run(() =>
    {
        var (panel, left, right) = Build(sendsLeftDown: true, width: 303);

        Assert.True(panel.IsStacked);

        // 上の段 32＋間の半分 7＋下の段 14
        Assert.Equal(53, panel.DesiredSize.Height);
        Assert.Equal(new Rect(203, 0, 100, 32), LayoutInformation.GetLayoutSlot(right));

        // 下の段は行の幅いっぱい。中身は右寄せなので、右端に出る
        Assert.Equal(new Rect(0, 39, 303, 14), LayoutInformation.GetLayoutSlot(left));
    });

    [Fact]
    public Task 入り切らない幅で_指定が無ければ_今までどおり右を下の段へ送る() => UiThread.Run(() =>
    {
        var (panel, left, right) = Build(sendsLeftDown: false, width: 303);

        Assert.True(panel.IsStacked);
        Assert.Equal(new Rect(0, 0, 303, 14), LayoutInformation.GetLayoutSlot(left));
        Assert.Equal(new Rect(203, 21, 100, 32), LayoutInformation.GetLayoutSlot(right));
    });

    [Fact]
    public Task 右の塊を隠しているときは_左だけを行の幅いっぱいに置く() => UiThread.Run(() =>
    {
        // 単独の画面の改変の詳細（削除のボタンを出さない）
        var left = new Border { Width = 190, Height = 14, HorizontalAlignment = HorizontalAlignment.Right };
        var right = new Border { Width = 100, Height = 32, Visibility = Visibility.Collapsed };
        var panel = new SplitRowPanel { Gap = 14, MinLeftWidth = 320, SendsLeftDown = true };
        panel.Children.Add(left);
        panel.Children.Add(right);
        panel.Measure(new Size(250, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 250, panel.DesiredSize.Height));

        Assert.False(panel.IsStacked);
        Assert.Equal(new Rect(0, 0, 250, 14), LayoutInformation.GetLayoutSlot(left));
    });
}
