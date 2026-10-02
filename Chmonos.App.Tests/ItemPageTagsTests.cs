using System.IO;
using System.Runtime.CompilerServices;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページのユーザータグの欄（メモ3-③・ユーザ判断 2026-10-02）：小分類まで全部出す・BOOTHのタグと同じく畳める・商品名の下に置く。
/// 前は大分類だけを出し、小分類は持っていても出していなかった。
/// </summary>
public class ItemPageTagsTests
{
    private static UserTagAssignment Tag(string top, params string[] subs) => new() { Top = top, Subs = subs };

    [Fact]
    public void 札は_大分類と小分類を1枚にし_小分類の無い大分類は大分類だけ()
    {
        var labels = ItemViewModel.UserTagLabels(
        [
            Tag("作り物の衣装", "夏", "冬"),
            Tag("作り物の髪型"),
            Tag("作り物の小物", "帽子"),
        ]);

        // 付けた順のまま。大分類の中の小分類も付けた順
        Assert.Equal(["作り物の衣装 / 夏", "作り物の衣装 / 冬", "作り物の髪型", "作り物の小物 / 帽子"], labels);
    }

    [Fact]
    public void 手で直したJSONで小分類が空でも_大分類を出す()
    {
        // 読むときに欠けた配列を空として受ける決め事（CLAUDE.md）。null の小分類で落ちない
        var labels = ItemViewModel.UserTagLabels([new UserTagAssignment { Top = "作り物の衣装", Subs = null! }]);

        Assert.Equal(["作り物の衣装"], labels);
    }

    [Fact]
    public Task 商品ページは_小分類まで札にし_見出しに枚数を出す() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        item = item with { Local = item.Local with { UserTags = [Tag("衣装", "夏", "冬"), Tag("髪型")] } };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.True(page.HasUserTags);
        Assert.Equal(["衣装 / 夏", "衣装 / 冬", "髪型"], page.UserTagChips);
        Assert.Equal("（3）", page.UserTagsCountText);
    });

    [Fact]
    public Task ユーザータグの欄は_既定で開き_畳んだら別の商品へ移っても畳んだまま() => TestApp.Run(async app =>
    {
        var first = Make.Item("1000001", "作り物の衣装");
        var second = Make.Item("1000002", "作り物の髪型");
        await app.AddItemAsync(first);
        await app.AddItemAsync(second);
        var main = await app.StartAsync();

        // 開閉はアプリを閉じるまで保つ値なので、試験の後へ持ち越さない
        var before = SectionFolds.UserTagsExpanded;
        try
        {
            SectionFolds.UserTagsExpanded = true;
            var page = new ItemViewModel(first, app.Services, main, main.Thumbnails);
            Assert.True(page.IsUserTagsExpanded);

            var changed = new List<string?>();
            page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            page.IsUserTagsExpanded = false;
            Assert.Contains(nameof(ItemViewModel.IsUserTagsExpanded), changed);

            // BOOTHのタグと同じく、商品を移っても同じ開き方（開閉の覚え方を揃える）
            var next = new ItemViewModel(second, app.Services, main, main.Thumbnails);
            Assert.False(next.IsUserTagsExpanded);

            // BOOTHのタグの開閉とは別に覚える
            Assert.Equal(SectionFolds.BoothTagsExpanded, next.IsBoothTagsExpanded);
        }
        finally
        {
            SectionFolds.UserTagsExpanded = before;
        }
    });

    [Fact]
    public void 右の列の枠の中は_商品名_ユーザータグ_BOOTHのタグの順()
    {
        // 並びは XAML の書いた順で決まる（縦に積む1つの StackPanel の中）。部品を組んで測るにはアプリの見た目の資源が要り、
        // 試験の中の素の Application に足すとほかの試験の見た目まで変わるので、原文の順を読む。見た目は ViewShot の絵で見る
        var xaml = File.ReadAllText(ItemViewPath());

        var name = xaml.IndexOf("AutomationProperties.AutomationId=\"ItemFavorite\"", StringComparison.Ordinal);
        var userTags = xaml.IndexOf("AutomationProperties.AutomationId=\"ItemUserTagsExpander\"", StringComparison.Ordinal);
        var boothTags = xaml.IndexOf("AutomationProperties.AutomationId=\"ItemBoothTagsExpander\"", StringComparison.Ordinal);

        Assert.True(name >= 0 && userTags >= 0 && boothTags >= 0, "3つの部品が商品ページにある");
        Assert.True(name < userTags, "商品名（星）がユーザータグより先");
        Assert.True(userTags < boothTags, "ユーザータグが BOOTHのタグより先");
    }

    private static string ItemViewPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views", "ItemView.xaml");
}
