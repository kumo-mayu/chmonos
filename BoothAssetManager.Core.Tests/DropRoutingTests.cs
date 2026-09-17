using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ウィンドウに落とされた／貼られたものの行き先。
///
/// 落ちてくるものは2種類しかない（ファイルかBOOTHのURL）ので、規則も短い。
/// 画面ごとに受けると同じものを落としたのに結果が変わるので、規則は1つに保つ。
/// </summary>
public class DropRoutingTests
{
    private static readonly Func<string, bool> NothingKnown = _ => false;
    private static readonly Func<string, bool> EverythingKnown = _ => true;

    [Fact]
    public void SendsFilesToTheImportScreen()
    {
        var decision = DropRouting.Decide([@"D:\storage\a.zip"], null, NothingKnown);

        Assert.Equal(DropAction.Import, decision.Action);
    }

    /// <summary>
    /// ファイルとURLが同時に来たらファイルを採る。
    /// ファイルは実体で、URLは参照。実体の方が意図がはっきりしている。
    /// </summary>
    [Fact]
    public void PrefersFilesWhenBothArrive()
    {
        var decision = DropRouting.Decide(
            [@"D:\storage\a.zip"],
            "https://booth.pm/ja/items/5813187",
            EverythingKnown);

        Assert.Equal(DropAction.Import, decision.Action);
    }

    /// <summary>手元にある商品はそのまま開く。通信は要らない。</summary>
    [Fact]
    public void OpensAnItemTheLibraryAlreadyHas()
    {
        var decision = DropRouting.Decide(null, "https://booth.pm/ja/items/5813187", EverythingKnown);

        Assert.Equal(DropAction.OpenItem, decision.Action);
        Assert.Equal("5813187", decision.ItemId);
    }

    /// <summary>
    /// 手元に無ければ尋ねる。**勝手に取りに行かない。**
    /// この経路が、贈答品や気になっている未購入品を登録する道にもなる。
    /// </summary>
    [Fact]
    public void AsksBeforeFetchingAnItemTheLibraryDoesNotHave()
    {
        var decision = DropRouting.Decide(null, "https://booth.pm/ja/items/9999999", NothingKnown);

        Assert.Equal(DropAction.OfferToRegister, decision.Action);
        Assert.Equal("9999999", decision.ItemId);
    }

    /// <summary>数字だけでも受ける。ID欄と同じ規則。</summary>
    [Fact]
    public void AcceptsABareItemId()
    {
        var decision = DropRouting.Decide(null, " 5813187 ", EverythingKnown);

        Assert.Equal(DropAction.OpenItem, decision.Action);
        Assert.Equal("5813187", decision.ItemId);
    }

    /// <summary>
    /// 実際にBOOTHのページから落ちてくる形。2026-09-10 に実サイトで確かめたもの。
    ///
    /// 検索結果とショップページで商品リンクの形が違い、絵をドラッグすると
    /// リンクではなく画像URLが落ちてくる。**どれも商品IDを持っている。**
    /// </summary>
    [Theory]
    // 検索結果・商品ページのリンク
    [InlineData("https://booth.pm/ja/items/4897493")]
    [InlineData("https://booth.pm/en/items/4897493")]
    [InlineData("https://booth.pm/items/4897493")]
    // ショップページのリンク（サブドメイン形式）
    [InlineData("https://hotogiya.booth.pm/items/4897493")]
    // 絵をドラッグしたとき（商品ページのギャラリー）
    [InlineData("https://booth.pximg.net/b9f5a983-e991-4261-b325-fbeb9a9ee89e/i/4897493/9b0f6f1e-e318-4a01-adb7-c25d2a405da5_base_resized.jpg")]
    // 絵をドラッグしたとき（検索結果・ショップページのサムネイル）
    [InlineData("https://booth.pximg.net/c/300x300_a2_g5/b9f5a983-e991-4261-b325-fbeb9a9ee89e/i/4897493/9b0f6f1e_base_resized.jpg")]
    [InlineData("https://booth.pximg.net/c/72x72_a2_g5/b9f5a983-e991-4261-b325-fbeb9a9ee89e/i/4897493/262396a9_base_resized.jpg")]
    // 追跡パラメータが付いた形
    [InlineData("https://booth.pm/ja/items/4897493?utm_source=twitter&utm_medium=social")]
    // 配布ファイル（ダウンロード履歴から）
    [InlineData("https://s6.booth.pm/b9f5a983-e991-4261-b325-fbeb9a9ee89e/f/4897493/7905648/Kuuta_1.0.zip?x=1")]
    public void ReadsEveryShapeBoothActuallyHandsOver(string url)
    {
        var decision = DropRouting.Decide(null, url, EverythingKnown);

        Assert.Equal(DropAction.OpenItem, decision.Action);
        Assert.Equal("4897493", decision.ItemId);
    }

    /// <summary>
    /// 文章の選択をドラッグすると、URLは文の中に混じって落ちてくる。
    /// HTMLの断片で来ることもある。**URLだけを渡してもらう前提にしない。**
    /// </summary>
    [Theory]
    [InlineData("この商品どうですか https://booth.pm/ja/items/4897493 かわいい")]
    [InlineData("<a href=\"https://booth.pm/ja/items/4897493\">くうた</a>")]
    [InlineData("https://booth.pm/ja/items/4897493\r\n")]
    public void FindsTheUrlInsideWhateverArrives(string text)
    {
        var decision = DropRouting.Decide(null, text, EverythingKnown);

        Assert.Equal(DropAction.OpenItem, decision.Action);
        Assert.Equal("4897493", decision.ItemId);
    }

    /// <summary>
    /// ショップのURLを落とされたら、そのショップの画面へ送る。
    /// 外のBOOTHへ飛ばすより、手持ちが見える方が役に立つ。
    /// </summary>
    [Theory]
    [InlineData("https://hotogiya.booth.pm/")]
    [InlineData("https://hotogiya.booth.pm")]
    [InlineData("https://hotogiya.booth.pm/item_lists/abc")]
    public void SendsAShopUrlToTheShopScreen(string url)
    {
        var decision = DropRouting.Decide(null, url, NothingKnown);

        Assert.Equal(DropAction.OpenShop, decision.Action);
        Assert.Equal("hotogiya", decision.Shop);
    }

    /// <summary>
    /// BOOTHの中でもショップではないサブドメインは開かない。
    /// 開いても手持ちは出てこないので、送り先として意味が無い。
    /// </summary>
    [Theory]
    [InlineData("https://accounts.booth.pm/library")]
    [InlineData("https://manage.booth.pm/items")]
    public void IgnoresBoothSubdomainsThatAreNotShops(string url)
    {
        Assert.Equal(DropAction.Ignore, DropRouting.Decide(null, url, NothingKnown).Action);
    }

    /// <summary>ショップのアイコンには商品IDが無い。商品として読んではいけない。</summary>
    [Fact]
    public void DoesNotReadAShopIconAsAnItem()
    {
        var icon = "https://booth.pximg.net/c/48x48/users/13254227/icon_image/7e9dd921_base_resized.jpg";

        Assert.Equal(DropAction.Ignore, DropRouting.Decide(null, icon, EverythingKnown).Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ただの文字列")]
    [InlineData("https://example.com/items/123")]
    public void IgnoresAnythingThatIsNotAnItem(string? text)
    {
        Assert.Equal(DropAction.Ignore, DropRouting.Decide(null, text, EverythingKnown).Action);
    }

    /// <summary>空の配列はファイルが来ていないのと同じ。</summary>
    [Fact]
    public void TreatsAnEmptyFileListAsNothing()
    {
        Assert.Equal(DropAction.Ignore, DropRouting.Decide([], null, EverythingKnown).Action);
    }

    // ---- 商品ページを開いているとき ----
    //
    // 足す先が決まっているので、画像を「この商品の画像に足す」へ回せる。
    // 商品ページ以外では聞かない——足す先が無い場所で聞いても答えられない。

    /// <summary>ただの画像ファイル。BOOTHとは関係が無いので、そのまま足す。</summary>
    [Fact]
    public void AddsAPlainImageFileToTheOpenItem()
    {
        var decision = DropRouting.DecideOnItemPage(
            [@"C:\pics\mine.png"], text: null, hasBitmap: false, _ => true);

        Assert.Equal(DropAction.AddImageToItem, decision.Action);
    }

    /// <summary>スクリーンショットの貼り付け。ファイルではなく絵そのものが来る。</summary>
    [Fact]
    public void AddsAPastedScreenshotToTheOpenItem()
    {
        var decision = DropRouting.DecideOnItemPage(
            paths: null, text: null, hasBitmap: true, _ => true);

        Assert.Equal(DropAction.AddImageToItem, decision.Action);
    }

    /// <summary>
    /// **これが要。**BOOTHの商品ページから絵をドラッグすると、
    /// その絵のURLに商品IDが入っている。どちらの意図かは決まらないので聞く。
    /// </summary>
    [Fact]
    public void AsksWhenTheImageCameFromBooth()
    {
        var decision = DropRouting.DecideOnItemPage(
            paths: null,
            text: "https://booth.pximg.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/i/3565798/x.jpg",
            hasBitmap: true,
            _ => true);

        Assert.Equal(DropAction.AskImageOrItem, decision.Action);
        Assert.Equal("3565798", decision.ItemId);
    }

    /// <summary>画像ファイルと一緒にBOOTHのURLが来た場合も同じ。</summary>
    [Fact]
    public void AsksWhenAnImageFileArrivesWithABoothUrl()
    {
        var decision = DropRouting.DecideOnItemPage(
            [@"C:\pics\mine.png"],
            "https://siguna.booth.pm/items/3565798",
            hasBitmap: false,
            _ => true);

        Assert.Equal(DropAction.AskImageOrItem, decision.Action);
        Assert.Equal("3565798", decision.ItemId);
    }

    /// <summary>
    /// zipが混ざっていたら取り込みを採る。
    /// **画像そのものが配布物のこともある**（BOOTHのダウンロード形式に画像が含まれる）ので、
    /// 画像だから取り込みではない、とは言えない。混ざっているなら取り込みたい意図の方が強い、
    /// という判断だけをする。
    /// </summary>
    [Fact]
    public void PrefersImportWhenSomethingElseIsMixedIn()
    {
        var decision = DropRouting.DecideOnItemPage(
            [@"C:\pics\mine.png", @"C:\dl\outfit.zip"], text: null, hasBitmap: false, _ => true);

        Assert.Equal(DropAction.Import, decision.Action);
    }

    /// <summary>画像が来ていなければ、今まで通りの規則で決める。</summary>
    [Fact]
    public void FallsBackToTheUsualRuleWithoutAnImage()
    {
        Assert.Equal(
            DropAction.OpenItem,
            DropRouting.DecideOnItemPage(
                paths: null, text: "https://booth.pm/ja/items/3565798", hasBitmap: false, _ => true).Action);

        Assert.Equal(
            DropAction.Import,
            DropRouting.DecideOnItemPage(
                [@"C:\dl\outfit.zip"], text: null, hasBitmap: false, _ => true).Action);
    }

    [Theory]
    [InlineData(@"C:\a\b.png", true)]
    [InlineData(@"C:\a\b.JPG", true)]
    [InlineData(@"C:\a\b.webp", true)]
    [InlineData(@"C:\a\b.zip", false)]
    [InlineData(@"C:\a\b.unitypackage", false)]
    public void KnowsWhichFilesLookLikeImages(string path, bool expected)
        => Assert.Equal(expected, DropRouting.LooksLikeImage(path));

    /// <summary>
    /// ブラウザから絵をドラッグするとファイルにならず、URLだけが落ちてくる。
    /// **BOOTHの画像URLだと分からないと「その商品を開く」になってしまう。**
    /// </summary>
    [Fact]
    public void AsksWhenOnlyABoothImageUrlArrives()
    {
        var decision = DropRouting.DecideOnItemPage(
            paths: null,
            text: "https://booth.pximg.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/i/3565798/x.jpg",
            hasBitmap: false,
            _ => true);

        Assert.Equal(DropAction.AskImageOrItem, decision.Action);
        Assert.Equal("3565798", decision.ItemId);
        Assert.EndsWith("x.jpg", decision.ImageUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// **BOOTHの画像置き場だけを見る。**このツールがBOOTH以外へ
    /// 問い合わせる道を作らないため。
    /// </summary>
    [Theory]
    [InlineData("https://booth.pximg.net/x/i/1/a.jpg", true)]
    [InlineData("https://example.com/a.jpg", false)]
    [InlineData("https://booth.pm/ja/items/3565798", false)]
    [InlineData(null, false)]
    public void OnlyTreatsBoothsOwnImageHostAsAnImageUrl(string? text, bool expected)
        => Assert.Equal(expected, DropRouting.IsBoothImageUrl(text));

    [Fact]
    public void OnTheResolveScreenAnItemPageBecomesTheItemIdEvenWhenOwned()
    {
        // 持っている商品でも開かない。「このファイルはこの商品」と言うために落としている
        var decision = DropRouting.DecideOnResolve(null, "https://booth.pm/ja/items/5813187", EverythingKnown);

        Assert.Equal(DropAction.UseAsItemId, decision.Action);
        Assert.Equal("5813187", decision.ItemId);
    }

    [Fact]
    public void OnTheResolveScreenFilesAndShopsKeepTheUsualRoute()
    {
        Assert.Equal(DropAction.Import, DropRouting.DecideOnResolve([@"D:\storage\a.zip"], null, NothingKnown).Action);
        Assert.Equal(DropAction.OpenShop, DropRouting.DecideOnResolve(null, "https://someshop.booth.pm/", NothingKnown).Action);
    }
}
