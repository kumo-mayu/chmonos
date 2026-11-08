using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 別の商品へ進んだとき、最初の画面に見えない欄は絵の後に作る（<see cref="DeferredSection"/>）。
/// 見える欄は同じコマで作り、見えない欄は前の商品のまま隠して、新しい商品の結び付けを後へ回す。
/// </summary>
public class DeferredSectionTests
{
    [Fact]
    public Task 先頭へ戻って進むと_見える欄はその場で_見えない欄は後から新しい商品になる() => UiThread.Run(async () =>
    {
        using var page = new Page(sections: 4, sectionHeight: 300, viewport: 400);
        await page.OpenAsync("A");
        Assert.All(page.Sections, section => Assert.Equal("A", section.Child is FrameworkElement child ? child.DataContext : null));

        page.Next("B", returnsToTop: true);

        // 先読み（300）を足して 700 まで：1つ目（0〜300）と2つ目（300〜600）はその場で。3つ目（600〜）も上端が 700 より上なのでその場で
        Assert.Equal(["B", "B", "B", "A"], page.Shown());
        Assert.True(page.Sections[3].IsHeld);
        Assert.Equal(Visibility.Collapsed, page.Sections[3].Child.Visibility);

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(["B", "B", "B", "B"], page.Shown());
        Assert.All(page.Sections, section => Assert.False(section.IsHeld));
    });

    [Fact]
    public Task 隠している欄は_足し切る処理で今すぐ出る() => UiThread.Run(async () =>
    {
        using var page = new Page(sections: 6, sectionHeight: 300, viewport: 400);
        await page.OpenAsync("A");
        page.Next("B", returnsToTop: true);
        Assert.Contains("A", page.Shown());

        Assert.True(ProgressiveItems.FeedAllNow(page.Scroller));

        Assert.All(page.Shown(), shown => Assert.Equal("B", shown));
    });

    [Fact]
    public Task 前の位置を保つ開き直しでは_分けずにその場で全部出す() => UiThread.Run(async () =>
    {
        using var page = new Page(sections: 6, sectionHeight: 300, viewport: 400);
        await page.OpenAsync("A");
        page.Scroller.ScrollToVerticalOffset(900);
        page.Layout();

        page.Next("B", returnsToTop: false);

        Assert.All(page.Shown(), shown => Assert.Equal("B", shown));
        Assert.Equal(900, page.Scroller.VerticalOffset);
    });

    /// <summary>流せるページに、決まった高さの欄を縦に並べた台（窓は出さない）。</summary>
    private sealed class Page : IDisposable
    {
        private readonly HwndSource _source;

        public Page(int sections, double sectionHeight, double viewport)
        {
            var stack = new StackPanel();
            for (var i = 0; i < sections; i++)
            {
                var section = new DeferredSection { Child = new Border { Height = sectionHeight } };
                Sections.Add(section);
                stack.Children.Add(section);
            }

            Scroller = new ScrollViewer { Height = viewport, Width = 300, Content = stack };
            _source = new HwndSource(new HwndSourceParameters("DeferredSectionTests")
            {
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3),
                Width = 1,
                Height = 1,
            })
            { RootVisual = Scroller };
        }

        public List<DeferredSection> Sections { get; } = [];

        public ScrollViewer Scroller { get; }

        /// <summary>最初の商品を開き、画面に載る（Loaded）まで待つ。</summary>
        public async Task OpenAsync(string item)
        {
            Scroller.DataContext = item;
            Layout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        /// <summary>別の商品へ進む。先頭へ戻すときは、商品ページと同じく印を付けてから流す。</summary>
        public void Next(string item, bool returnsToTop)
        {
            if (returnsToTop)
            {
                ProgressiveItems.SetReturnsToTop(Scroller, true);
                Scroller.ScrollToHome();
            }

            Scroller.DataContext = item;
            Layout();
            Scroller.ClearValue(ProgressiveItems.ReturnsToTopProperty);
        }

        public void Layout() => Scroller.UpdateLayout();

        public List<string?> Shown() => Sections.Select(section => ((FrameworkElement)section.Child).DataContext as string).ToList();

        public void Dispose() => _source.Dispose();
    }
}
