using System.Windows.Controls;
using System.Windows.Documents;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページで窓が受けた素の ← → を、絵を送るのに使うか（メモ27-③）。
/// 前はショップ名・ID のリンクに Tab で止まって ← → を押すと、離れた所のギャラリーの絵が変わり、止まっている所は動かなかった。
/// </summary>
public class GalleryArrowsTests
{
    [Fact]
    public Task Tabで止まるリンクの上では_絵を送らず_ほかへも渡さない() => UiThread.Run(() =>
    {
        var link = new Hyperlink(new Run("作り物ショップ"));
        _ = new TextBlock(link);

        Assert.Equal(GalleryArrow.Swallow, GalleryArrows.For(link));
    });

    /// <summary>説明の本文は Tab で止まらず、カーソルも見えない。本文に渡すと、本文を押した後に絵を送れなくなるだけなので、今まで通り送る。</summary>
    [Fact]
    public Task 説明の本文と_その中のリンクでは_今まで通り絵を送る() => UiThread.Run(() =>
    {
        var link = new Hyperlink(new Run("https://sample.invalid/guide"));
        var body = new RichTextBox(new FlowDocument(new Paragraph(link)));

        Assert.Equal(GalleryArrow.Gallery, GalleryArrows.For(link));
        Assert.Equal(GalleryArrow.Gallery, GalleryArrows.For(body));
    });

    [Fact]
    public Task ボタンや何にも止まっていないときは絵を送り_入力欄とスライダーでは部品に渡す() => UiThread.Run(() =>
    {
        Assert.Equal(GalleryArrow.Gallery, GalleryArrows.For(new Button()));
        Assert.Equal(GalleryArrow.Gallery, GalleryArrows.For(null));
        Assert.Equal(GalleryArrow.Yield, GalleryArrows.For(new TextBox()));
        Assert.Equal(GalleryArrow.Yield, GalleryArrows.For(new ComboBox()));
        Assert.Equal(GalleryArrow.Yield, GalleryArrows.For(new Slider()));
    });
}
