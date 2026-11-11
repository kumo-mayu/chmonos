using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 何行かを選んでコピーした物から商品を全部拾う（計画 26）。HTML はどれも作り物で、BOOTH のページの形を真似ただけ
/// （本物のページは取りに行かない）。番号・名前・ショップも作り物。
/// </summary>
public class PastedItemIdsTests
{
    private static readonly Func<string, bool> NothingKnown = _ => false;

    /// <summary>ブラウザが置く CF_HTML の形。見出しの SourceURL はコピーした場所のページ。</summary>
    private static string CfHtml(string fragment, string sourceUrl = "https://booth.pm/ja/items/1009999")
        => "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\nStartFragment:0000000141\r\nEndFragment:0000000900\r\n"
            + $"SourceURL:{sourceUrl}\r\n"
            + "<html><body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body></html>";

    // ---- 説明文：セットの中身を URL で並べた形。素の文にも URL がある ----

    private const string DescriptionText =
        "セットの中身\n"
        + "・作り物のワンピース https://booth.pm/ja/items/1000001\n"
        + "・作り物の靴 https://sample-shop.booth.pm/items/1000002\n"
        + "・作り物の帽子 https://booth.pm/ja/items/1000003\n"
        + "ショップ https://sample-shop.booth.pm/\n";

    private static readonly string DescriptionHtml = CfHtml(
        "<p>セットの中身</p>"
        + "<p>・作り物のワンピース <a href=\"https://booth.pm/ja/items/1000001\" target=\"_blank\">https://booth.pm/ja/items/1000001</a></p>"
        + "<p>・作り物の靴 <a href=\"https://sample-shop.booth.pm/items/1000002\">https://sample-shop.booth.pm/items/1000002</a></p>"
        + "<p>・作り物の帽子 <a href=\"https://booth.pm/ja/items/1000003\">https://booth.pm/ja/items/1000003</a></p>"
        + "<p>ショップ <a href=\"https://sample-shop.booth.pm/\">https://sample-shop.booth.pm/</a></p>");

    [Fact]
    public void 説明文の_URLを並べた中身を_出てきた順に拾う()
        => Assert.Equal(["1000001", "1000002", "1000003"], PastedItemIds.Collect(DescriptionText, DescriptionHtml));

    [Fact]
    public void 説明文は_HTMLが無くても_素の文から拾う()
        => Assert.Equal(["1000001", "1000002", "1000003"], PastedItemIds.Collect(DescriptionText, null));

    // ---- ショップの一覧：素の文は商品名と値段だけ。商品はリンク先にしか無い ----

    private const string ShopListText = "作り物の上着\nsample-shop\n¥ 500\n作り物のスカート\nsample-shop\n¥ 800\n作り物のリボン\nsample-shop\n¥ 300\n";

    private static readonly string ShopListHtml = CfHtml(
        "<ul class=\"item-list\">"
        + "<li><a href=\"https://sample-shop.booth.pm/items/1000011\"><img src=\"https://booth.pximg.net/c/300x300_a2_g5/00000000-1111-2222-3333-444444444444/i/1000011/aaaa_base_resized.jpg\"></a>"
        + "<a href=\"https://sample-shop.booth.pm/items/1000011\">作り物の上着</a>"
        + "<a href=\"https://sample-shop.booth.pm/\">sample-shop</a><span>¥ 500</span></li>"
        + "<li><a href='https://sample-shop.booth.pm/items/1000012'><img src='https://booth.pximg.net/00000000-1111-2222-3333-444444444444/i/1000099/bbbb.jpg'></a>"
        + "<a href='https://booth.pm/ja/items/1000012'>作り物のスカート</a><span>¥ 800</span></li>"
        + "<li><A HREF=\"https://sample-shop.booth.pm/items/1000013?utm_source=a&amp;utm_medium=b\">作り物のリボン</A><span>¥ 300</span></li>"
        + "</ul>",
        sourceUrl: "https://sample-shop.booth.pm/items/1009998");

    [Fact]
    public void ショップの一覧は_リンク先から拾い_絵とショップと見出しのページは数えない()
        => Assert.Equal(["1000011", "1000012", "1000013"], PastedItemIds.Collect(ShopListText, ShopListHtml));

    [Fact]
    public void 素の文だけを読むと_一覧の商品は拾えない()
        => Assert.Empty(PastedItemIds.Collect(ShopListText, null));

    // ---- 購入履歴：注文の行・ダウンロードの行・アカウントの頁へのリンクが混ざる ----

    private const string LibraryText = "作り物の髪型セット\nsample-shop\nダウンロード\n作り物のアクセサリー\nother-shop\nダウンロード\n";

    private static readonly string LibraryHtml = CfHtml(
        "<div class=\"order\"><a href=\"https://accounts.booth.pm/orders/12345\">注文詳細</a>"
        + "<a href=\"https://booth.pm/ja/items/1000021\"><img src=\"https://booth.pximg.net/c/72x72/00000000-1111-2222-3333-444444444444/i/1000021/cccc.jpg\"></a>"
        + "<a href=\"https://booth.pm/ja/items/1000021\">作り物の髪型セット</a>"
        + "<a href=\"https://sample-shop.booth.pm/\">sample-shop</a>"
        + "<a href=\"https://booth.pm/downloadables/7654321\">ダウンロード</a></div>"
        + "<div class=\"order\"><a href=\"https://accounts.booth.pm/items/1000098\">別の頁</a>"
        + "<a href=\"https://booth.pm/zh-cn/items/1000022\">作り物のアクセサリー</a>"
        + "<a href=\"https://other-shop.booth.pm/\">other-shop</a>"
        + "<a href=\"https://s9.booth.pm/00000000-1111-2222-3333-444444444444/f/1000097/7654322/a.zip\">ダウンロード</a></div>",
        sourceUrl: "https://accounts.booth.pm/library");

    [Fact]
    public void 購入履歴は_商品のリンクだけを拾い_注文とダウンロードとアカウントの頁は数えない()
        => Assert.Equal(["1000021", "1000022"], PastedItemIds.Collect(LibraryText, LibraryHtml));

    // ---- 並べ方・紛れ ----

    [Fact]
    public void HTMLのリンクを先に並べ_素の文にだけある物を後ろに足す()
    {
        var html = CfHtml("<a href=\"https://booth.pm/ja/items/1000031\">作り物の上</a> <a href=\"https://booth.pm/ja/items/1000032\">作り物の下</a>");
        var text = "作り物の靴下 https://booth.pm/ja/items/1000033 作り物の上 作り物の下";

        Assert.Equal(["1000031", "1000032", "1000033"], PastedItemIds.Collect(text, html));
    }

    [Fact]
    public void 見出しのSourceURLは読まない()
    {
        var html = CfHtml("<a href=\"https://booth.pm/ja/items/1000041\">作り物</a>", sourceUrl: "https://booth.pm/ja/items/1000042");

        Assert.Equal(["https://booth.pm/ja/items/1000041"], PastedItemIds.LinkTargets(html));
        Assert.Equal(["1000041"], PastedItemIds.Collect(null, html));
    }

    [Fact]
    public void 相対のリンクとdata_hrefは拾わない()
    {
        var html = CfHtml("<a href=\"/ja/items/1000051\">作り物</a><a data-href=\"https://booth.pm/ja/items/1000052\" href=\"#\">作り物</a>");

        Assert.Empty(PastedItemIds.Collect(null, html));
    }

    [Fact]
    public void 長いIDの頭を別の商品として拾わない()
        => Assert.Equal(["10000611", "1000061"], PastedItemIds.Collect("https://booth.pm/ja/items/10000611 https://booth.pm/ja/items/1000061です", null));

    // ---- 振り分け ----

    [Fact]
    public void 二件以上なら_選ばせる一覧へ回す()
    {
        var decision = DropRouting.Decide(null, ShopListText, NothingKnown, ShopListHtml);

        Assert.Equal(DropAction.ChooseItemsToRegister, decision.Action);
        Assert.Equal(["1000011", "1000012", "1000013"], decision.ItemIds);
    }

    [Fact]
    public void 手元にある商品が混ざっていても_一覧へ回す()
    {
        var decision = DropRouting.Decide(null, DescriptionText, id => id == "1000002", DescriptionHtml);

        Assert.Equal(DropAction.ChooseItemsToRegister, decision.Action);
        Assert.Equal(3, decision.ItemIds!.Count);
    }

    /// <summary>1件だけのときは今まで通り（素の文で決める）。リンクの字だけをコピーして HTML にだけ1件あっても、動きを変えない。</summary>
    [Fact]
    public void 一件だけなら今まで通り()
    {
        Assert.Equal(DropAction.OfferToRegister, DropRouting.Decide(null, "https://booth.pm/ja/items/1000071", NothingKnown,
            CfHtml("<a href=\"https://booth.pm/ja/items/1000071\">https://booth.pm/ja/items/1000071</a>")).Action);

        Assert.Equal(DropAction.Ignore, DropRouting.Decide(null, "作り物の上着", NothingKnown,
            CfHtml("<a href=\"https://booth.pm/ja/items/1000072\">作り物の上着</a>")).Action);

        // 絵とショップが混ざっていても、商品が1件なら今まで通り
        Assert.Equal(DropAction.OfferToRegister, DropRouting.Decide(null,
            "https://booth.pm/ja/items/1000073 https://sample-shop.booth.pm/ https://booth.pximg.net/00000000-1111-2222-3333-444444444444/i/1000074/a.jpg",
            NothingKnown).Action);
    }

    [Fact]
    public void ファイルがあれば_商品が何件あっても取り込み()
        => Assert.Equal(DropAction.Import, DropRouting.Decide([@"D:\storage\a.zip"], DescriptionText, NothingKnown, DescriptionHtml).Action);

    /// <summary>未確定でファイルを選んでいるときは、今まで通り素の文の最初の1件をそのファイルの商品IDにする。</summary>
    [Fact]
    public void 未確定でファイルを選んでいれば_最初の1件を商品IDとして入れる()
    {
        var decision = DropRouting.DecideOnResolve(null, DescriptionText, NothingKnown, DescriptionHtml);

        Assert.Equal(DropAction.UseAsItemId, decision.Action);
        Assert.Equal("1000001", decision.ItemId);
    }

    [Fact]
    public void 商品ページと改変の詳細でも_画像でなければ一覧へ回す()
    {
        Assert.Equal(DropAction.ChooseItemsToRegister, DropRouting.DecideOnItemPage(null, ShopListText, false, NothingKnown, html: ShopListHtml).Action);
        Assert.Equal(DropAction.ChooseItemsToRegister, DropRouting.DecideOnModification(null, ShopListText, false, NothingKnown, ShopListHtml).Action);

        // 絵そのもの（スクリーンショット）が来ていれば、今まで通り画像の扱い
        Assert.Equal(DropAction.AddImageToItem, DropRouting.DecideOnItemPage(null, ShopListText, true, NothingKnown, html: ShopListHtml).Action);
        Assert.Equal(DropAction.AddPhotoToModification, DropRouting.DecideOnModification(null, ShopListText, true, NothingKnown, ShopListHtml).Action);
    }
}
