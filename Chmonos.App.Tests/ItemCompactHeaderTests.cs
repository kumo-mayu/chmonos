using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページと編集の画面の左で、商品名の段が流れ去ったら1行の見出しを重ねる（ユーザ指示 2026-11-11。ショップの中と同じ考え）。
/// 出す境目はショップと同じ判定（<see cref="CompactHeader.Shows"/>。値は ShopOneScrollTests）。ここでは、
/// 流れに合わせた出し入れと帯の下へ流す量（部品に載せて）、部品へ流す位置（値で）、見出しに出す絵の選び方を確かめる。
/// 見た目は tools/ViewShot の item-page-back-after-scroll（流した商品ページ）と edit-scrolled（流した編集の画面）で見る
/// </summary>
public class ItemCompactHeaderTests
{
    private const double ViewportHeight = 300;

    [Theory]
    // 行き先が名前の段の近くで、流した先でも名前の段が見えている：見出しは出ないので、余白だけ空ける
    [InlineData(40, 12, 70, 28)]
    // 行き先が名前の段より下：流した先で見出しが重なるので、その高さの分も空ける
    [InlineData(500, 12, 70, 436)]
    // 0 より前へは行かない
    [InlineData(5, 12, 70, 0)]
    // 名前の段がまだ測れていない（0）間は、見出しは出ないので余白だけ
    [InlineData(500, 12, 0, 488)]
    public void 部品へ流す位置は_流した先で見出しが出るなら_その下に出す(double top, double margin, double anchorBottom, double expected)
    {
        Assert.Equal(expected, CompactHeader.RevealOffset(top, margin, anchorBottom), 3);
    }

    [Theory]
    // 商品ページ：流す部品に内側の余白は無い。帯の高さそのものの下へ流す
    [InlineData(0, 52)]
    // 編集の画面の左：上に20の余白があり、帯はもともとそこを覆っている。見える範囲から隠れるのは残りの32
    [InlineData(20, 32)]
    public Task 名前の段が流れ去ったら帯を出し_戻したら消す(double padding, double expectedInset) => WithTracker(padding, (viewer, anchor, bar, tracker) =>
    {
        // 先頭：名前の段が見えている
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        Assert.Equal(0, viewer.TopInset);

        // 名前の段（上から 100〜140）の下端が上端に着く少し手前：まだ出さない
        viewer.ScrollToVerticalOffset(139);
        Settle(viewer);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);

        // 下端が上端に着いた：出す。Tab・画面内検索で流すときは帯の下に出す
        viewer.ScrollToVerticalOffset(140);
        Settle(viewer);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.True(tracker.IsShown);
        Assert.Equal(expectedInset, viewer.TopInset, 3);

        // 戻して名前の段がまた見えたら、引っ込める（重ねているだけなので、中身は動かない）
        viewer.ScrollToVerticalOffset(60);
        Settle(viewer);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        Assert.Equal(0, viewer.TopInset);
        Assert.Equal(60, viewer.VerticalOffset, 3);
    });

    [Fact]
    public Task 位置を変えずに名前の段が伸び縮みしても_測り直す() => WithTracker(0, (viewer, anchor, bar, tracker) =>
    {
        // 商品ページは同じ画面に次の商品が差し込まれ、位置を保ったまま名前の行数だけが変わることがある（開き直し・名前の変更）
        viewer.ScrollToVerticalOffset(150);
        Settle(viewer);
        Assert.Equal(Visibility.Visible, bar.Visibility);

        // 名前が2行に伸びて、段の下端（180）が流れの位置より下へ来た
        anchor.Height = 80;
        Settle(viewer);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);

        anchor.Height = 40;
        Settle(viewer);
        Assert.Equal(Visibility.Visible, bar.Visibility);
    });

    [Fact]
    public Task 帯の上のホイールは_下の中身を流す() => WithTracker(0, (viewer, anchor, bar, tracker) =>
    {
        viewer.ScrollToVerticalOffset(200);
        Settle(viewer);

        bar.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
        });
        Settle(viewer);

        Assert.True(viewer.VerticalOffset > 200);
    });

    [Fact]
    public Task 見出しの絵は_検索のカードと同じく_指名した1枚() => TestApp.Run(async app =>
    {
        string[] urls =
        [
            "https://sample.invalid/1000001/a.png",
            "https://sample.invalid/1000001/b.png",
        ];
        var item = Make.Item("1000001", "作り物の衣装");
        item = item with
        {
            Booth = item.Booth with { Images = urls.Select(url => new BoothImage { OriginalUrl = url }).ToList() },
            Local = item.Local with { ThumbnailImage = ImagePipeline.FileNameFor(urls[1]) },
        };
        var plain = Make.Item("1000002", "作り物の髪型");
        plain = plain with
        {
            Booth = plain.Booth with { Images = urls.Select(url => new BoothImage { OriginalUrl = url.Replace("1000001", "1000002") }).ToList() },
        };
        var bare = Make.Item("1000003", "作り物の靴");
        await app.AddItemAsync(item);
        await app.AddItemAsync(plain);
        await app.AddItemAsync(bare);

        foreach (var record in new[] { item, plain })
        {
            var directory = app.Services.Paths.ItemImagesDir(record.Id);
            Directory.CreateDirectory(directory);
            foreach (var image in record.Booth.Images)
            {
                using var picture = new Image<Rgba32>(4, 4);
                picture.SaveAsWebp(Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl)));
            }
        }

        var main = await app.StartAsync();

        // 指名した2枚目。ギャラリーで別の絵へ送っても、見出しの絵は替えない（どの商品かを見分ける所）
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.Same(page.Images[1], page.ThumbnailTile);
        page.PreviousImageCommand.Execute(null);
        Assert.Same(page.Images[1], page.ThumbnailTile);

        // 指名が無ければ並びの1枚目
        var second = new ItemViewModel(plain, app.Services, main, main.Thumbnails);
        Assert.Same(second.Images[0], second.ThumbnailTile);

        // 画像が無ければ空（見出しは空の枠を出す）
        var none = new ItemViewModel(bare, app.Services, main, main.Thumbnails);
        Assert.Null(none.ThumbnailTile);
    });

    /// <summary>
    /// 上に 100 の空き、名前の段（高さ40）、下に長い中身を積んだ流す部品と、重ねる帯を、出さない窓口に載せる。
    /// </summary>
    private static Task WithTracker(
        double padding,
        Action<InsetScrollViewer, Border, Border, CompactHeaderTracker> body) => UiThread.Run(() =>
    {
        var anchor = new Border { Height = 40 };
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 100 });
        panel.Children.Add(anchor);
        panel.Children.Add(new Border { Height = 2000 });

        var viewer = new InsetScrollViewer
        {
            Height = ViewportHeight,
            Width = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Padding = new Thickness(0, padding, 0, 0),
            Content = panel,
        };
        var bar = new Border { Height = CompactHeader.Height, VerticalAlignment = VerticalAlignment.Top };
        var root = new Grid();
        root.Children.Add(viewer);
        root.Children.Add(bar);

        using var source = new HwndSource(new HwndSourceParameters("ItemCompactHeaderTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = root,
        };

        Settle(root);
        var tracker = new CompactHeaderTracker(viewer, anchor, bar);
        Settle(root);
        body(viewer, anchor, bar, tracker);
    });

    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }
}
