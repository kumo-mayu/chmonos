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
}
