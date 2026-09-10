using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 自分で入れる分類と、同梱したBOOTHのカテゴリ表。
///
/// BOOTHから取れない商品には分類が無く、統計にも絞り込みにも出てこない。
/// **入れられるのに絞り込みに出ないなら、入れる意味が半分無くなる。**
/// </summary>
public class LocalCategoryTests
{
    private static CategoryTable Bundled
        => new(Path.Combine(AppContext.BaseDirectory, "assets", "booth-categories.json"));

    /// <summary>同梱した表が読めること。読めないと候補が出ない。</summary>
    [Fact]
    public void ReadsTheBundledTable()
    {
        var table = Bundled;

        Assert.True(table.IsAvailable);
        Assert.Equal(17, table.Parents.Count);
        Assert.Equal(139, table.Parents.Sum(parent => parent.Children.Count));
    }

    /// <summary>
    /// **3Dモデルの子を先に出す。**このツールはVRChatのアセットを持つ人が使うもので、
    /// 3Dモデルの子12件でほぼ足りる。
    /// </summary>
    [Fact]
    public void PutsTheThreeDChildrenFirst()
    {
        var suggestions = Bundled.Suggestions();

        Assert.Equal("3Dキャラクター", suggestions[0]);
        Assert.Equal(12, suggestions.Take(12).Count());
        Assert.Contains("VRoid", suggestions.Take(12));
        Assert.Contains("3D衣装", suggestions.Take(12));
    }

    /// <summary>**残りも全部並べる。**選べる範囲を狭めると、BOOTHにある分類が入れられなくなる。</summary>
    [Fact]
    public void StillOffersEverythingElse()
    {
        var suggestions = Bundled.Suggestions();

        Assert.Equal(139, suggestions.Count);
        Assert.Contains("フォント・書体", suggestions);
        Assert.Contains("コスプレ衣装", suggestions);
    }

    /// <summary>子から親を引ける。ユーザには子しか入れさせないので、親はこちらで補う。</summary>
    [Fact]
    public void FindsTheParentOfAChild()
    {
        Assert.Equal("3Dモデル", Bundled.ParentOf("3Dキャラクター"));
        Assert.Equal("素材データ", Bundled.ParentOf("フォント・書体"));
        Assert.Null(Bundled.ParentOf("存在しない分類"));
        Assert.Null(Bundled.ParentOf(null));
    }

    /// <summary>表が無くても落ちない。候補が出ないだけで、手で打てば入る。</summary>
    [Fact]
    public void SurvivesAMissingTable()
    {
        var table = new CategoryTable(Path.Combine(Path.GetTempPath(), "no-such-categories.json"));

        Assert.False(table.IsAvailable);
        Assert.Empty(table.Suggestions());
        Assert.Null(table.ParentOf("3Dキャラクター"));
    }

    /// <summary>ユーザが入れた分類を優先する。名前やショップと同じ扱い。</summary>
    [Fact]
    public void PrefersTheCategoryTheUserEntered()
    {
        var item = new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock
            {
                Category = new BoothCategory { Id = 209, Name = "3D衣装", ParentName = "3Dモデル" },
            },
            Local = new LocalBlock { Category = "3Dキャラクター" },
        };

        Assert.Equal("3Dキャラクター", item.CategoryName);
        Assert.True(item.HasUserCategory);
    }

    /// <summary>入れていなければBOOTHの観測がそのまま出る。</summary>
    [Fact]
    public void FallsBackToWhatBoothSaid()
    {
        var item = new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock
            {
                Category = new BoothCategory { Id = 209, Name = "3D衣装", ParentName = "3Dモデル" },
            },
            Local = new LocalBlock(),
        };

        Assert.Equal("3D衣装", item.CategoryName);
        Assert.False(item.HasUserCategory);
    }

    /// <summary>入れた分類も検索対象。入れたのに探せないなら意味が無い。</summary>
    [Fact]
    public void SearchesTheCategoryTheUserEntered()
    {
        var item = new ItemRecord
        {
            Id = "local-abcd1234",
            Booth = new BoothBlock(),
            Local = new LocalBlock { Category = "3Dキャラクター" },
        };

        Assert.Contains("3dキャラクター", SearchText.Build(item).Primary, StringComparison.OrdinalIgnoreCase);
    }
}
