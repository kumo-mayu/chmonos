using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// これまで移れなかった所から、アバターの管理・ショップへ移れる（ユーザ判断 2026-11-25「それらは移れるようにしておいてください」）：
/// 編集画面のショップ名・商品ページの共通素体の札・対応アバターの札の「アバターの管理で開く」・統計のアバターの行の「アバターの管理で開く」。
/// 押したら主画面がどの画面のどの物を選ぶかを見る
/// </summary>
public class OtherJumpToAvatarsAndShopTests
{
    private const string ItemId = "9900991";
    private const string Owned = "9800401";
    private const string NotAvatar = "9800402";
    private const string Ignored = "9800403";
    private const string Base = "作り物素体";

    private static AvatarRegistryEntry Avatar(string id, string name, string category = "3Dキャラクター", bool? avatarOverride = null, bool owned = false) => new()
    {
        ItemId = id,
        BoothName = $"作り物の「{name}」",
        DisplayName = name,
        Category = category,
        AvatarOverride = avatarOverride,
        IsOwnedManually = owned,
    };

    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            Avatar(Owned, "ミナト", owned: true),
            // アバターと判定されない物（管理の一覧に並ばない）
            Avatar(NotAvatar, "ただの商品", category: "3Dモデル（その他）"),
            // 「アバターとして扱わない」にした物（管理には並ぶ）
            Avatar(Ignored, "外したアバター", avatarOverride: false),
        ],
        BaseGroups = [new AvatarBaseGroup { Name = Base }],
    };

    private static AvatarLink Link(string id, string name) => new()
    {
        AvatarItemId = id,
        Name = name,
        Source = AvatarLinkSource.SupportSection,
        Confirmed = true,
    };

    private static async Task<(ItemViewModel Page, MainViewModel Main)> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(Registry());
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with
        {
            Local = item.Local with
            {
                Avatars = [Link(Owned, "ミナト"), Link(NotAvatar, "ただの商品"), Link(Ignored, "外したアバター")],
                // 管理に有る素体と、管理の一覧に無い素体
                AvatarBases =
                [
                    new AvatarBaseLink { BaseName = Base, Source = AvatarLinkSource.SupportSection, Confirmed = true },
                    new AvatarBaseLink { BaseName = "一覧に無い素体", Source = AvatarLinkSource.SupportSection, Confirmed = true },
                ],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return (new ItemViewModel(item, app.Services, main, main.Thumbnails), main);
    }

    [Fact]
    public Task 対応アバターの札の管理で開くは_持っているアバターでも管理でそのアバターを選ぶ() => TestApp.Run(async app =>
    {
        var (page, main) = await OpenAsync(app);
        var row = page.Avatars.Single(avatar => avatar.ItemId == Owned);

        Assert.True(row.OpenInManagerCommand!.CanExecute(null));
        row.OpenInManagerCommand.Execute(null);

        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => avatars.Selected?.ItemId == Owned, "管理でそのアバターを選ぶ");
        Assert.False(avatars.IsBaseMode);
    });

    [Fact]
    public Task 札の管理で開くは_アバターとして扱わない物は開け_管理に並ばない物は押せない() => TestApp.Run(async app =>
    {
        var (page, main) = await OpenAsync(app);

        Assert.False(page.Avatars.Single(avatar => avatar.ItemId == NotAvatar).OpenInManagerCommand!.CanExecute(null));

        var ignored = page.Avatars.Single(avatar => avatar.ItemId == Ignored);
        Assert.True(ignored.OpenInManagerCommand!.CanExecute(null));
        ignored.OpenInManagerCommand.Execute(null);

        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => avatars.Selected?.ItemId == Ignored, "扱わないにした物も選べる");
    });

    [Fact]
    public Task 共通素体の札の開くは_共通素体の見方でその素体を選び_管理に無い素体は押せない() => TestApp.Run(async app =>
    {
        var (page, main) = await OpenAsync(app);
        var known = page.AvatarBases.Single(row => row.Name == Base);
        var unknown = page.AvatarBases.Single(row => row.Name == "一覧に無い素体");

        Assert.False(unknown.OpenCommand!.CanExecute(null));
        Assert.True(known.OpenCommand!.CanExecute(null));
        known.OpenCommand.Execute(null);

        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => avatars.IsBaseMode && avatars.SelectedBase?.Name == Base, "共通素体の見方でその素体を選ぶ");

        // 戻ると商品ページ側ではなく、移る前の画面へ戻れる（履歴に積まれる）
        main.GoBack();
        Assert.IsNotType<AvatarsViewModel>(main.CurrentViewModel);
    });

    /// <summary>
    /// 商品の札の素体名は説明文から拾った書き方のままで、登録簿の素体と大文字・小文字が違いうる（押せるかは AvatarBaseNames.Same で見ている）。
    /// 開いた先が名前の完全一致で探すと、押せるのに素体が見つからず「アバター」の見方の先頭の行で開いていた
    /// </summary>
    [Fact]
    public Task 共通素体の札の名前が登録簿と大文字小文字だけ違っても_共通素体の見方でその素体を選ぶ() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = Registry().Entries,
            BaseGroups = [new AvatarBaseGroup { Name = "Alpha" }, new AvatarBaseGroup { Name = "Sotai" }],
        });
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with
        {
            Local = item.Local with
            {
                AvatarBases = [new AvatarBaseLink { BaseName = "SOTAI", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        var chip = Assert.Single(page.AvatarBases);
        Assert.True(chip.OpenCommand!.CanExecute(null));
        chip.OpenCommand.Execute(null);

        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.SelectedBase is not null, "素体の一覧ができる");
        Assert.True(avatars.IsBaseMode);
        Assert.Equal("Sotai", avatars.SelectedBase!.Name);
    });

    [Fact]
    public Task 統計のアバターの行の管理で開くは_管理でそのアバターを選び_検索へ行く今の動きは残る() => TestApp.Run(async app =>
    {
        var (_, main) = await OpenAsync(app);
        var snapshot = StatsService.Build([], new AvatarRegistry(), 0) with
        {
            Avatars =
            [
                new StatsCountBar { Key = Owned, Label = "ミナト", ItemCount = 2, DirectCount = 2 },
                new StatsCountBar { Key = NotAvatar, Label = "ただの商品", ItemCount = 1, DirectCount = 1 },
                new StatsCountBar { Key = string.Empty, Label = "対応アバター未設定", ItemCount = 2, IsResidual = true },
            ],
        };
        var searched = new List<string>();
        var inManager = new HashSet<string> { Owned, Ignored };
        var rows = StatsViewModel.AvatarRows(
            snapshot, ownedOnly: false, (id, _) => searched.Add(id), () => { }, id => main.ShowAvatarAt(id, null), inManager.Contains);

        Assert.False(rows[1].OpenInManagerCommand!.CanExecute(null));
        Assert.Null(rows[2].OpenInManagerCommand);

        rows[0].OpenCommand!.Execute(null);
        Assert.Equal([Owned], searched);

        rows[0].OpenInManagerCommand!.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => avatars.Selected?.ItemId == Owned, "管理でそのアバターを選ぶ");
    });

    [Fact]
    public Task 編集画面の商品ページの持ち主のショップ名は_ショップの画面へ移る() => TestApp.Run(async app =>
    {
        var (page, main) = await OpenAsync(app);

        Assert.True(page.OpenShopCommand.CanExecute(null));
        page.OpenShopCommand.Execute(null);

        await UiThread.Until(() => main.CurrentViewModel is ShopViewModel, "ショップの画面へ");
        Assert.Equal("sample-shop", ((ShopViewModel)main.CurrentViewModel!).Shop.Subdomain);
    });

    // ---- XAML の結び付け（名前を書き違えても組み立ては通り、押して何も起きないため） ----

    private static string CommandPath(MenuItem item)
        => BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path.Path ?? string.Empty;

    private static void AssertResolves(Type type, string path)
        => Assert.True(type.GetProperty(path, BindingFlags.Public | BindingFlags.Instance) is not null, $"{type.Name}.{path} が無い");

    [Fact]
    public Task 札の右クリックは_管理で開くを行の命令に結び_素体の札には開くを足す() => UiThread.Run(() =>
    {
        var panel = new ItemAvatarsPanel();

        var chipMenu = (ContextMenu)((DataTemplate)panel.Resources["AvatarChipMenu"]).LoadContent();
        var open = chipMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "アバターの管理で開く");
        Assert.Equal("OpenInManagerCommand", CommandPath(open));
        AssertResolves(typeof(AvatarRow), "OpenInManagerCommand");

        // 点線の2種類（素体経由・変換経由）の札と素体の札のメニューは、組み立て前のテンプレートの中なので、XAML の文面で見る
        var xaml = XDocument.Load(Path.Combine(ViewsFolder(), "ItemAvatarsPanel.xaml"));
        string? Command(string automationId, string header) => xaml.Descendants()
            .Where(e => e.Name.LocalName == "MenuItem"
                && (string?)e.Attribute("Header") == header
                && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId") == automationId)
            .Select(e => (string?)e.Attribute("Command"))
            .SingleOrDefault();

        Assert.Equal("{Binding OpenCommand}", Command("ItemAvatarBaseMenu.Open", "開く"));
        AssertResolves(typeof(AvatarBaseRow), "OpenCommand");
        // 記録した札・素体経由・変換経由の3種類
        Assert.Equal(
            3,
            xaml.Descendants().Count(e => e.Name.LocalName == "MenuItem"
                && (string?)e.Attribute("Header") == "アバターの管理で開く"
                && (string?)e.Attribute("Command") == "{Binding OpenInManagerCommand}"));
        AssertResolves(typeof(ViaBaseAvatarRow), "OpenInManagerCommand");
        AssertResolves(typeof(ViaConversionAvatarRow), "OpenInManagerCommand");
        return Task.CompletedTask;
    });

    private static string ViewsFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");

    [Fact]
    public Task 編集画面のショップ名は_リンクで商品ページの持ち主のショップの命令に結ぶ() => UiThread.Run(() =>
    {
        var view = new EditView();
        var link = Descendants(view).OfType<SelectableLink>()
            .Single(control => System.Windows.Automation.AutomationProperties.GetAutomationId(control) == "EditShopLink");

        Assert.Equal("ItemPage.OpenShopCommand", BindingOperations.GetBinding(link, SelectableLink.CommandProperty)?.Path.Path);
        Assert.Equal("ItemPage.OpenShopInBoothCommand", BindingOperations.GetBinding(link, SelectableLink.MiddleCommandProperty)?.Path.Path);
        AssertResolves(typeof(ItemViewModel), "OpenShopCommand");
        AssertResolves(typeof(ItemViewModel), "OpenShopInBoothCommand");

        var bar = Descendants(view).OfType<ItemCompactBar>().Single(control => control.Name == "LeftCompactBar");
        Assert.Equal("ItemPage.OpenShopCommand", BindingOperations.GetBinding(bar, ItemCompactBar.ShopCommandProperty)?.Path.Path);
        Assert.Equal("ItemPage.OpenShopInBoothCommand", BindingOperations.GetBinding(bar, ItemCompactBar.ShopBoothCommandProperty)?.Path.Path);
        return Task.CompletedTask;
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>([root]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            {
                stack.Push(child);
            }

            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    stack.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }
    }
}
