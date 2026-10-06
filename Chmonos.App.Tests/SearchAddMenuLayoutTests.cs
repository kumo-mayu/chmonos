using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using K = Chmonos.App.ViewModels.SearchModuleKind;

namespace Chmonos.App.Tests;

/// <summary>
/// 「＋ 条件を追加」の見出しと、見出しの中の群の並び（ユーザ判断 2026-10-06・open.md の「条件を追加の並びの監修」）。
/// 群の間は区切り線。並びは使う人が決めた形なので、形そのものを書いて確かめる。
/// </summary>
public class SearchAddMenuLayoutTests
{
    private static IReadOnlyList<IReadOnlyList<K>> GroupsOf(string title)
        => SearchModuleCatalog.Menu.Single(layout => layout.Title == title).Groups;

    [Fact]
    public void 見出しは_BOOTHの情報_商品の情報_ファイルの情報_カレンダー_スライダー_利用状況の順()
        => Assert.Equal(
            ["BOOTHの情報", "商品の情報", "ファイルの情報", "カレンダー", "スライダー", "利用状況"],
            SearchModuleCatalog.Menu.Select(layout => layout.Title));

    [Fact]
    public void BOOTHの情報の群()
        => Assert.Equal(
            [
                [K.Category, K.BoothTag, K.Adult],
                [K.Price, K.WishList, K.FreePaid],
                [K.Shop, K.Avatar],
                [K.PublishedAt, K.EndOfSale, K.Updated],
            ],
            GroupsOf(SearchModuleCatalog.BoothInfo));

    [Fact]
    public void 商品の情報の群()
        => Assert.Equal(
            [
                [K.UserTag, K.Attribute, K.Favorite, K.Price, K.Avatar, K.Hidden, K.Adult],
                [K.Owned, K.Gift, K.NotOnBooth],
                [K.Modification, K.Recent, K.UnityProject],
                [K.AcquiredAt, K.PublishedAt],
                [K.Unedited],
            ],
            GroupsOf(SearchModuleCatalog.ItemInfo));

    [Fact]
    public void ファイルの情報の群()
        => Assert.Equal(
            [
                [K.Owned, K.MissingFile, K.BrokenZip],
                [K.Path],
            ],
            GroupsOf(SearchModuleCatalog.FileInfo));

    [Fact]
    public void カレンダー_スライダー_利用状況の群()
    {
        Assert.Equal([[K.PublishedAt, K.AcquiredAt]], GroupsOf(SearchModuleCatalog.Calendar));
        Assert.Equal([[K.Price, K.WishList, K.Attribute, K.Recent]], GroupsOf(SearchModuleCatalog.Slider));
        Assert.Equal([[K.Recent, K.Modification, K.UnityProject]], GroupsOf(SearchModuleCatalog.Usage));
    }

    [Fact]
    public void どの条件も_どこかの見出しに出る()
    {
        var shown = SearchModuleCatalog.Menu.SelectMany(layout => layout.Groups).SelectMany(group => group).ToHashSet();

        Assert.All(SearchModuleCatalog.All, info => Assert.Contains(info.Kind, shown));
    }

    [Fact]
    public Task 画面のメニューは_群の間に区切り線を入れ_名前で並ぶ() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        static string Line(object entry) => entry is SearchModuleMenuEntry menu ? menu.Label : "―";
        var headings = search.ModuleMenu.ToDictionary(heading => heading.Title, heading => heading.Entries.Select(Line).ToList());

        Assert.Equal(["所持", "見つからないファイル", "壊れたzip", "―", "ファイルの場所"], headings["ファイルの情報"]);
        Assert.Equal(
            ["カテゴリ", "BOOTHタグ", "R-18", "―", "価格", "スキ数", "有料・無料", "―", "ショップ", "対応アバター", "―", "公開日", "公開状況", "更新通知あり"],
            headings["BOOTHの情報"]);
        Assert.Equal(
            ["ユーザータグ", "属性", "お気に入り", "価格", "対応アバター", "非表示", "R-18", "―", "所持", "ギフト", "BOOTHに無い商品", "―",
                "改変", "最近", "Unityプロジェクト", "―", "入手日", "公開日", "―", "編集状況"],
            headings["商品の情報"]);
        Assert.DoesNotContain(headings.Values.SelectMany(lines => lines), line => line == "対応アバターの確認" || line == "販売終了");
    });
}
