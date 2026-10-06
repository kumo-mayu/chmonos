using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Chmonos.App.Tests.Support;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品のカード・行の右クリック（<c>CardMenu</c>）の並びと区切り（ユーザ判断 2026-10-06）：
/// BOOTH へ出る物 / アプリの中で開く物 / 自分の印を付ける物 / 更新 / 手元のファイル / 隠す。
/// メニューは1つを検索・ショップ・フォルダ・アバター・改変・編集画面の続く商品で使い回すので、ここで並びを見れば全部の画面で同じになる。
/// </summary>
public class CardMenuOrderTests
{
    private const string Separator = "|";

    private static List<object> Items() => [.. ((ContextMenu)new ItemCardResources()["CardMenu"]).Items.Cast<object>()];

    private static string Key(object item) => item switch
    {
        Separator => Separator,
        MenuItem menuItem => AutomationProperties.GetAutomationId(menuItem),
        _ => throw new InvalidOperationException($"区切りでも項目でもない物：{item.GetType().Name}"),
    };

    [Fact]
    public Task 項目は_BOOTH_アプリの中_印_更新_手元のファイル_隠すの群で並び_間に区切りが1本ずつ入る() => UiThread.Run(() =>
    {
        var keys = Items().Select(item => item is System.Windows.Controls.Separator ? Separator : Key(item)).ToList();

        Assert.Equal(
            [
                "CardMenu.OpenBooth", "CardMenu.CopyLink",
                Separator,
                "CardMenu.OpenItem", "CardMenu.OpenShop", "CardMenu.Edit",
                Separator,
                "CardMenu.Select", "CardMenu.Favorite", "CardMenu.AddToModification",
                Separator,
                "CardMenu.ShowUpdate", "CardMenu.MarkUpdateRead",
                Separator,
                "CardMenu.OpenParent", "CardMenu.UnityParent",
                Separator,
                "CardMenu.Hide",
            ],
            keys);
    });

    [Fact]
    public Task 項目は出し入れしないので_出し分けで群が空になって区切りが続くことは無い() => UiThread.Run(() =>
    {
        // 押せない項目も出したまま吹き出しで理由を言う（ユーザ判断 2026-10-04・メモ20-①）。出し入れの束縛が入ると、
        // 群が空になったときに区切りが2本続く・端に残るので、ここで止める（入れるなら区切りの出し入れも一緒に作る）
        foreach (var item in Items().OfType<FrameworkElement>())
        {
            Assert.Equal(Visibility.Visible, item.Visibility);
            Assert.Null(BindingOperations.GetBindingBase(item, UIElement.VisibilityProperty));
        }

        var items = Items();
        Assert.IsNotType<System.Windows.Controls.Separator>(items[0]);
        Assert.IsNotType<System.Windows.Controls.Separator>(items[^1]);
        Assert.DoesNotContain(
            Enumerable.Range(1, items.Count - 1),
            index => items[index] is System.Windows.Controls.Separator && items[index - 1] is System.Windows.Controls.Separator);
    });
}
