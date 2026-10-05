using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// ナビのキーでの移り方（メモ47・ユーザ判断 2026-10-05。<see cref="NavKeys"/>）。
/// 矢印の行き先は位置から計算で決まるので <see cref="NavKeys.Move"/> を直に確かめる。外から Tab で入ったときに止まる所は、
/// 本物の部品を並べて、WPF の Tab が選んだ行き先から何を代わりに止まらせるかを確かめる。
/// フォーカスを実際に移す動きは窓がフォーカスを取れないと確かめられないので、実機で見る
/// </summary>
public class NavKeysTests
{
    // 戻る・進む（横に2つ）、畳む、検索、改変、取り込み、設定。上端の位置
    private static readonly double[] Tops = [16, 16, 50, 90, 130, 200, 400];

    [Theory]
    // 上下は行の先頭へ（戻る・進むは1行）。畳むから上へは、進むではなく戻る
    [InlineData(2, Key.Up, 0)]
    [InlineData(3, Key.Up, 2)]
    [InlineData(2, Key.Down, 3)]
    [InlineData(0, Key.Down, 2)]
    // 進むからも、下は畳む
    [InlineData(1, Key.Down, 2)]
    // 下は設定まで届く
    [InlineData(5, Key.Down, 6)]
    [InlineData(6, Key.Up, 5)]
    public void 上下は1つずつ移り_戻る進むは1行として扱う(int current, Key key, int expected)
        => Assert.Equal(expected, NavKeys.Move(Tops, current, key));

    [Theory]
    [InlineData(0, Key.Right, 1)]
    [InlineData(1, Key.Left, 0)]
    // 行の端・縦に並ぶだけの項目の左右は動かない（ナビの外へ飛ばさず、受けて止める）
    [InlineData(0, Key.Left, null)]
    [InlineData(1, Key.Right, null)]
    [InlineData(3, Key.Left, null)]
    [InlineData(3, Key.Right, null)]
    public void 左右は横に並ぶ戻る進むの間だけ移る(int current, Key key, int? expected)
        => Assert.Equal(expected, NavKeys.Move(Tops, current, key));

    [Fact]
    public void 端では止まり_HomeとEndは端へ移る()
    {
        Assert.Null(NavKeys.Move(Tops, 0, Key.Up));
        Assert.Null(NavKeys.Move(Tops, 1, Key.Up));
        Assert.Null(NavKeys.Move(Tops, 6, Key.Down));
        Assert.Equal(0, NavKeys.Move(Tops, 4, Key.Home));
        Assert.Equal(6, NavKeys.Move(Tops, 4, Key.End));
        Assert.Null(NavKeys.Move(Tops, 6, Key.End));
    }

    [Fact]
    public void 履歴が無く戻るが押せないときは_先頭は畳む()
    {
        var tops = new double[] { 50, 90 };
        Assert.Null(NavKeys.Move(tops, 0, Key.Up));
        Assert.Equal(1, NavKeys.Move(tops, 0, Key.Down));
    }

    [Fact]
    public Task 外からTabで入ると_今開いている画面の項目に止まる() => UiThread.Run(() =>
    {
        using var stage = Stage.Build(activeIndex: 3);

        // 前の部品から Tab：戻る（ナビの先頭）ではなく、開いている画面の項目
        Assert.Same(stage.Items[3], NavKeys.EntryTarget(stage.Frame, stage.Before, stage.Items[0]));
        // 後ろの部品から Shift+Tab：ナビの最後（設定）ではなく、開いている画面の項目
        Assert.Same(stage.Items[3], NavKeys.EntryTarget(stage.Frame, stage.After, stage.Items[^1]));
    });

    [Fact]
    public Task 入る所が今の画面の項目なら_そのまま() => UiThread.Run(() =>
    {
        // 開いている画面が先頭（戻る）なら、WPF の既定で足りる
        using var stage = Stage.Build(activeIndex: 0);
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Before, stage.Items[0]));
    });

    [Fact]
    public Task ナビの中のTabは横取りせず_端から外へ出る() => UiThread.Run(() =>
    {
        using var stage = Stage.Build(activeIndex: 3);

        // ナビの中からは横取りしない（WPF の既定の1つずつ）
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Items[3], stage.Items[4]));
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Items[2], stage.Items[1]));
        // 項目は全部 Tab で止まる（並びの「1回だけ止まる」にしない）
        Assert.All(stage.Items, item => Assert.True(KeyboardNavigation.GetIsTabStop(item)));
        // ナビの外の部品へ向かう移り（設定から Tab・一番上から Shift+Tab）も触らない
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Items[^1], stage.After));
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Items[0], stage.Before));
        // 何も止まっていない所から（窓を開いた直後）も、今の画面の項目
        Assert.Same(stage.Items[3], NavKeys.EntryTarget(stage.Frame, null, stage.Items[0]));
    });

    [Fact]
    public Task 今の画面がナビに無いときは_既定の止まり先のまま() => UiThread.Run(() =>
    {
        using var stage = Stage.Build(activeIndex: -1);
        Assert.Null(NavKeys.EntryTarget(stage.Frame, stage.Before, stage.Items[0]));
    });

    [Fact]
    public Task 止まり先は見えていて押せる項目だけで_並びの順() => UiThread.Run(() =>
    {
        using var stage = Stage.Build(activeIndex: 2);
        stage.Items[1].IsEnabled = false;
        stage.Items[5].Visibility = Visibility.Collapsed;
        stage.Layout();

        Assert.Equal(
            [stage.Items[0], stage.Items[2], stage.Items[3], stage.Items[4], stage.Items[6]],
            NavKeys.Members(stage.Frame));
    });

    [Fact]
    public Task 窓の左右の絵送りは_ナビの項目の上では渡す() => UiThread.Run(() =>
    {
        using var stage = Stage.Build(activeIndex: 2);

        Assert.Equal(GalleryArrow.Yield, GalleryArrows.For(stage.Items[2]));
        Assert.Equal(GalleryArrow.Gallery, GalleryArrows.For(stage.Before));
    });

    /// <summary>前の部品・ナビ（戻る進むの行・畳む・項目・設定）・後ろの部品を並べた台。窓口は親がメッセージ専用。</summary>
    private sealed class Stage : IDisposable
    {
        private readonly HwndSource _source;
        private readonly StackPanel _root = new() { Width = 300, Height = 600 };

        private Stage(int activeIndex)
        {
            Before = new Button { Content = "前", Height = 20 };
            After = new Button { Content = "後", Height = 20 };
            var column = new StackPanel();
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            Items = [.. Enumerable.Range(0, 7).Select(index => new Button { Content = index.ToString(CultureInfo.InvariantCulture), Height = 24, Width = 60 })];
            pair.Children.Add(Items[0]);
            pair.Children.Add(Items[1]);
            column.Children.Add(pair);
            foreach (var item in Items.Skip(2))
            {
                column.Children.Add(item);
            }

            if (activeIndex >= 0)
            {
                Nav.SetIsActive(Items[activeIndex], true);
            }

            Frame = new Border { Child = column };
            NavKeys.SetIsEnabled(Frame, true);
            _root.Children.Add(Before);
            _root.Children.Add(Frame);
            _root.Children.Add(After);
            _source = new HwndSource(new HwndSourceParameters("NavKeysTests")
            {
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3),
                Width = 1,
                Height = 1,
            })
            {
                RootVisual = _root,
            };
            Layout();
        }

        public Button Before { get; }

        public Button After { get; }

        public List<Button> Items { get; }

        public Border Frame { get; }

        public static Stage Build(int activeIndex) => new(activeIndex);

        public void Layout() => _root.UpdateLayout();

        public void Dispose() => _source.Dispose();
    }
}
