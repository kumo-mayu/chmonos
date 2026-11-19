using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 選択の帯の「お気に入り ▾」「タグ・属性 ▾」「その他 ▾」（ユーザ判断 2026-11-20）。帯は検索・ショップ・フォルダの右で同じ型で、
/// 中身は <see cref="SelectionBulkActions"/>。書くのは商品ごとに錠の中で今の値に当てる命令で、カードの写しを書き戻さない。
/// </summary>
public class SelectionBarBulkTests
{
    private const string First = "9900701";
    private const string Second = "9900702";
    private const string Third = "9900703";

    private static async Task<SearchViewModel> SearchWithAsync(TestApp app, params ItemRecord[] items)
    {
        foreach (var item in items)
        {
            await app.AddItemAsync(item);
        }

        var search = (await app.StartAsync()).Search;
        await app.SettleAsync();
        return search;
    }

    private static void Select(SearchViewModel search, params string[] ids)
    {
        foreach (var card in search.ListItems.Where(card => ids.Contains(card.Item.Id)))
        {
            search.SelectByClick(card, control: true, shift: false);
        }

        Assert.Equal(ids.Length, search.SelectedCount);
    }

    private static async Task<LocalBlock> LocalOfAsync(TestApp app, string id) => (await app.Store.Items.LoadAsync(id))!.Local;

    private static ItemRecord WithTags(ItemRecord item, params UserTagAssignment[] tags) => item with { Local = item.Local with { UserTags = tags } };

    [Fact]
    public Task タグの追加は今の記録に足し_カードの写しが古くてもほかのタグを消さない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"), WithTags(Make.Item(Second, "作り物の髪"), new UserTagAssignment { Top = "髪型" }));

        // 画面が読んだ後に、別の所（取り込み・別の画面）が1件目にタグを付けた。カードはそれを知らない
        await app.Store.Items.ChangeLocalAsync(First, local => local with { UserTags = [new UserTagAssignment { Top = "衣装" }] }, LocalOwners.UserTags);
        Select(search, First, Second);
        app.AnswerBulkTag = model =>
        {
            model.Type(top: "髪型");
            return true;
        };

        search.Bulk.AddTagCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["衣装", "髪型"], (await LocalOfAsync(app, First)).UserTags.Select(tag => tag.Top));
        Assert.Equal(["髪型"], (await LocalOfAsync(app, Second)).UserTags.Select(tag => tag.Top));
        Assert.Equal("1 件に追加しました（1 件は既に付いていました）。", search.Bulk.StatusText);

        // 追加は確かめない（ユーザ判断 2026-11-20）。選んだままにする（続けて別の操作をするため）
        Assert.Empty(app.Notices);
        Assert.Equal(2, search.SelectedCount);
    });

    [Fact]
    public Task 候補に無いタグはタグの一覧へも足す() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"));
        Select(search, First);
        app.AnswerBulkTag = model =>
        {
            model.Type(top: "新しい大分類", sub: "新しい小分類");
            Assert.True(model.IsNewTop && model.IsNewSub);
            return true;
        };

        search.Bulk.AddTagCommand.Execute(null);
        await app.SettleAsync();

        var tag = Assert.Single((await LocalOfAsync(app, First)).UserTags);
        Assert.Equal("新しい大分類", tag.Top);
        Assert.Equal(["新しい小分類"], tag.Subs);
        var master = Assert.Single((await app.Store.UserTags.LoadAsync()).Tops, top => top.Name == "新しい大分類");
        Assert.Equal(["新しい小分類"], master.Subs.Select(sub => sub.Name));
    });

    [Fact]
    public Task タグの削除は付いている件数を出して1回だけ確かめ_ほかのタグは残す() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app,
            WithTags(Make.Item(First, "作り物の衣装"), new UserTagAssignment { Top = "衣装", Subs = ["ワンピース"] }, new UserTagAssignment { Top = "髪型" }),
            WithTags(Make.Item(Second, "作り物の髪"), new UserTagAssignment { Top = "髪型" }),
            Make.Item(Third, "作り物の小物"));
        Select(search, First, Second, Third);
        app.AnswerBulkTag = model =>
        {
            // 候補は選んだ商品に付いている物だけ。付いている件数の多い順
            Assert.Equal(["髪型", "衣装", "衣装／ワンピース"], model.RemovalCandidates);
            model.Type(removal: "髪型");
            return true;
        };

        app.Answer = _ => MessageBoxResult.OK;
        search.Bulk.RemoveTagCommand.Execute(null);
        await app.SettleAsync();

        var confirm = Assert.Single(app.Notices);
        Assert.Equal("2 件からタグ「髪型」を外します。", confirm.Text);
        Assert.Equal(["衣装"], (await LocalOfAsync(app, First)).UserTags.Select(tag => tag.Top));
        Assert.Empty((await LocalOfAsync(app, Second)).UserTags);
        Assert.Equal("2 件から外しました（1 件には付いていませんでした）。", search.Bulk.StatusText);
    });

    [Fact]
    public Task 削除の確かめでやめたら何も書かない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, WithTags(Make.Item(First, "作り物の衣装"), new UserTagAssignment { Top = "衣装" }));
        Select(search, First);
        app.AnswerBulkTag = model =>
        {
            model.Type(removal: "衣装");
            return true;
        };
        app.Answer = _ => MessageBoxResult.Cancel;

        search.Bulk.RemoveTagCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["衣装"], (await LocalOfAsync(app, First)).UserTags.Select(tag => tag.Top));
        Assert.Equal(string.Empty, search.Bulk.StatusText);
    });

    /// <summary>属性を外すとキーを消して未評価に戻す（0 にしない。未評価と 0 は別）。値は戻せないので、確かめの窓でそう言う。</summary>
    [Fact]
    public Task 属性の削除は未評価に戻し_値は戻せないと確かめる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app,
            Make.Item(First, "作り物の衣装") with { Local = Make.Item(First, "作り物の衣装").Local with { Attributes = new Dictionary<string, int> { ["かわいさ"] = 0, ["露出"] = 40 } } });
        Select(search, First);
        app.AnswerBulkTag = model =>
        {
            model.RemovesAttribute = true;
            Assert.Equal(["かわいさ", "露出"], model.RemovalCandidates);
            model.Type(removal: "かわいさ");
            return true;
        };

        app.Answer = _ => MessageBoxResult.OK;
        search.Bulk.RemoveTagCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("1 件から属性「かわいさ」を外します。外した属性の値は元に戻せません。", Assert.Single(app.Notices).Text);
        var attributes = (await LocalOfAsync(app, First)).Attributes;
        Assert.False(attributes.ContainsKey("かわいさ"));
        Assert.Equal(40, attributes["露出"]);
    });

    /// <summary>
    /// 属性の追加は値を付けずに「値はまだ」として足す（ユーザ判断 2026-11-20 の (a)）。値がある（0% も）・値はまだの商品は何もせず数える。
    /// カードの写しが古くても、今の記録の値を潰さない
    /// </summary>
    [Fact]
    public Task 属性の追加は値はまだとして足し_付いている商品は数えるだけ() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "かわいさ" }] });
        var search = await SearchWithAsync(app,
            Make.Item(First, "作り物の衣装"),
            Make.Item(Second, "作り物の髪") with { Local = Make.Item(Second, "作り物の髪").Local with { PendingAttributes = ["かわいさ"] } },
            Make.Item(Third, "作り物の小物"));

        // 画面が読んだ後に、別の所が3件目に値を付けた。カードはそれを知らない
        await app.Store.Items.ChangeLocalAsync(Third, local => ItemAttributes.WithValue(local, "かわいさ", 0), LocalOwners.Attributes);
        Select(search, First, Second, Third);
        app.AnswerBulkTag = model =>
        {
            Assert.True(model.IsAddingAttribute);
            Assert.Equal(["かわいさ"], model.AttributeCandidates);
            model.Type(attribute: "かわいさ");
            Assert.False(model.IsNewAttribute);
            return true;
        };

        search.Bulk.AddAttributeCommand.Execute(null);
        await app.SettleAsync();

        var added = await LocalOfAsync(app, First);
        Assert.Equal(["かわいさ"], added.PendingAttributes!);
        Assert.Empty(added.Attributes);
        Assert.Equal(["かわいさ"], (await LocalOfAsync(app, Second)).PendingAttributes!);
        var valued = await LocalOfAsync(app, Third);
        Assert.Equal(0, valued.Attributes["かわいさ"]);
        Assert.Null(valued.PendingAttributes);
        Assert.Equal("1 件に追加しました（2 件は既に付いていました）。", search.Bulk.StatusText);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 候補に無い属性は属性の一覧へも足す() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"));
        Select(search, First);
        app.AnswerBulkTag = model =>
        {
            model.Type(attribute: "新しい属性");
            Assert.True(model.IsNewAttribute);
            return true;
        };

        search.Bulk.AddAttributeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["新しい属性"], (await LocalOfAsync(app, First)).PendingAttributes!);
        Assert.Contains((await app.Store.Attributes.LoadAsync()).Attributes, attribute => attribute.Name == "新しい属性");
    });

    /// <summary>値はまだの属性も外す候補に出て、外すと付いていない状態に戻る</summary>
    [Fact]
    public Task 値はまだの属性も外せる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app,
            Make.Item(First, "作り物の衣装") with { Local = Make.Item(First, "作り物の衣装").Local with { PendingAttributes = ["かわいさ"] } });
        Select(search, First);
        Assert.True(search.Bulk.RemoveTagCommand.CanExecute(null));
        app.AnswerBulkTag = model =>
        {
            model.RemovesAttribute = true;
            Assert.Equal(["かわいさ"], model.RemovalCandidates);
            model.Type(removal: "かわいさ");
            Assert.Equal(1, model.CountHaving());
            return true;
        };

        app.Answer = _ => MessageBoxResult.OK;
        search.Bulk.RemoveTagCommand.Execute(null);
        await app.SettleAsync();

        Assert.Null((await LocalOfAsync(app, First)).PendingAttributes);
        Assert.Equal("1 件から外しました。", search.Bulk.StatusText);
    });

    [Fact]
    public Task お気に入りは足す_外すを分け_既にそうなっている物は数えるだけ() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app,
            Make.Item(First, "作り物の衣装"),
            Make.Item(Second, "作り物の髪") with { Local = Make.Item(Second, "作り物の髪").Local with { IsFavorite = true } });
        Select(search, First, Second);

        Assert.True(search.Bulk.AddFavoritesCommand.CanExecute(null));
        await search.Bulk.ChangeAsync(new Core.Services.BulkItemChange.SetFavorite(true));
        Assert.Equal("1 件をお気に入りに追加しました（1 件は既に入っていました）。", search.Bulk.StatusText);
        Assert.True((await LocalOfAsync(app, First)).IsFavorite);
        Assert.All(search.ListItems.Where(card => card.IsSelected), card => Assert.True(card.IsFavorite));
        Assert.False(search.Bulk.AddFavoritesCommand.CanExecute(null));

        search.Bulk.RemoveFavoritesCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("2 件をお気に入りから外しました。", search.Bulk.StatusText);
        Assert.False((await LocalOfAsync(app, Second)).IsFavorite);
    });

    [Fact]
    public Task 非表示にすると記録に書き_一覧と選択から外す_表示に戻すは押せない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"), Make.Item(Second, "作り物の髪"), Make.Item(Third, "作り物の小物"));
        Select(search, First, Second);
        Assert.False(search.Bulk.UnhideCommand.CanExecute(null));

        search.Bulk.HideCommand.Execute(null);
        await app.SettleAsync();

        Assert.True((await LocalOfAsync(app, First)).IsHidden);
        Assert.True((await LocalOfAsync(app, Second)).IsHidden);
        Assert.False((await LocalOfAsync(app, Third)).IsHidden);
        Assert.Equal([Third], search.ListItems.Select(card => card.Item.Id));
        Assert.Equal(0, search.SelectedCount);
    });

    [Fact]
    public Task 更新の通知を入れる_切るは名指しの欄だけを書く() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"), Make.Item(Second, "作り物の髪"));
        await app.Store.Items.ChangeLocalAsync(First, local => local with { Memo = "画面が読んだ後のメモ" }, [LocalField.Memo]);
        Select(search, First, Second);

        // 通知は既定で入っている
        Assert.False(search.Bulk.NotifyOnCommand.CanExecute(null));
        search.Bulk.NotifyOffCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("2 件の更新の通知を切りました。", search.Bulk.StatusText);
        var first = await LocalOfAsync(app, First);
        Assert.Equal((false, "画面が読んだ後のメモ"), (first.NotifyOnUpdate, first.Memo));
        Assert.False(search.Bulk.NotifyOffCommand.CanExecute(null));

        search.Bulk.NotifyOnCommand.Execute(null);
        await app.SettleAsync();
        Assert.True((await LocalOfAsync(app, Second)).NotifyOnUpdate);
    });

    [Fact]
    public Task 対応アバターを確認済みにするは確認待ちのある商品だけで押せる() => TestApp.Run(async app =>
    {
        var waiting = Make.Item(First, "作り物の衣装");
        waiting = waiting with { Local = waiting.Local with { Avatars = [new AvatarLink { AvatarItemId = "9900799", Source = AvatarLinkSource.Tag }] } };
        var search = await SearchWithAsync(app, waiting, Make.Item(Second, "作り物の髪"));
        Select(search, Second);
        Assert.False(search.Bulk.ConfirmAvatarsCommand.CanExecute(null));
        search.SelectByClick(search.ListItems.Single(card => card.Item.Id == First), control: true, shift: false);

        search.Bulk.ConfirmAvatarsCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(Assert.Single((await LocalOfAsync(app, First)).Avatars).Confirmed);
        Assert.Equal("1 件の対応アバターを確認済みにしました（1 件には確認待ちがありませんでした）。", search.Bulk.StatusText);
    });

    [Fact]
    public Task IDとリンクは改行でつないで写し_BOOTHに無い商品のリンクは飛ばす() => TestApp.Run(async app =>
    {
        var local = Make.Item("local-0001", "作り物の手作り") with { Booth = new BoothBlock { Name = "作り物の手作り" } };
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"), local);
        var localId = search.ListItems.Single(card => card.Item.IsLocalOnly).Item.Id;
        Select(search, First, localId);

        search.Bulk.CopyIdsCommand.Execute(null);
        search.Bulk.CopyLinksCommand.Execute(null);

        var ids = search.ListItems.Where(card => card.IsSelected).Select(card => card.Item.Id);
        Assert.Equal([string.Join(Environment.NewLine, ids), Core.Booth.BoothLinks.ItemPage(First)!], app.Copied);
        Assert.Equal("1 件をコピーしました。", search.Bulk.StatusText);
    });

    [Fact]
    public Task BOOTHの取得し直しは件数と目安を確かめ_1件ずつ頼む() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(First, "作り物の衣装");
        app.Booth.HasItem(Second, "作り物の髪");
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"), Make.Item(Second, "作り物の髪"));
        Select(search, First, Second);
        var before = app.Booth.Requests.Count;

        app.Answer = _ => MessageBoxResult.OK;
        await search.Bulk.RefreshFromBoothAsync();
        await app.SettleAsync();

        Assert.Equal("選んだ 2 件の情報をBOOTHから取得し直します。1件ずつ取得するので、約6秒以上かかります。", Assert.Single(app.Notices).Text);
        Assert.Equal("2 件の情報を取得し直しました。", search.Bulk.StatusText);

        // 1件ずつ：1件目の問い合わせが全部済んでから2件目へ（並べて投げない）
        var requests = app.Booth.Requests.Skip(before).ToList();
        var lastOfFirst = requests.FindLastIndex(url => url.Contains(First, StringComparison.Ordinal));
        var firstOfSecond = requests.FindIndex(url => url.Contains(Second, StringComparison.Ordinal));
        Assert.True(lastOfFirst >= 0 && firstOfSecond > lastOfFirst, string.Join(" | ", requests));
        Assert.False(search.Bulk.IsRefreshing);
    });

    [Fact]
    public Task BOOTHの取得し直しは確かめでやめたら問い合わせない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, Make.Item(First, "作り物の衣装"));
        Select(search, First);
        var before = app.Booth.Requests.Count;
        app.Answer = _ => MessageBoxResult.Cancel;

        search.Bulk.RefreshFromBoothCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(before, app.Booth.Requests.Count);
    });

    [Theory]
    [InlineData(1, "約3秒")]
    [InlineData(19, "約57秒")]
    [InlineData(20, "約1分")]
    [InlineData(100, "約5分")]
    public void 取得の目安は1件3秒で数える(int count, string expected)
        => Assert.Equal(expected, SelectionBulkActions.EstimateText(count));

    // 幅 1280 で絞り込みの欄を開くと、右から「Unityへ送る…」だけを「その他」へ畳めば入る（撮った幅の比）
    [Theory]
    [InlineData(800, 0)]
    [InlineData(700, 1)]
    [InlineData(620, 2)]
    [InlineData(300, 2)]
    public void 帯に入り切らない分だけ右から畳む(double available, int expected)
        => Assert.Equal(expected, SelectionBarFold.HowMany(available, fixedWidth: 550, foldWidths: [110, 100]));

    [Fact]
    public Task 見つからないファイルを探すは_選んだ商品のうち見つからない物の分だけを探す() => TestApp.Run(async app =>
    {
        var watched = System.IO.Path.Combine(app.Root, "files", "watched");
        System.IO.Directory.CreateDirectory(watched);
        var search = await SearchWithAsync(app,
            Make.Item(First, "作り物の衣装").WithFiles(Make.File(System.IO.Path.Combine(watched, "gone.zip")) with { MissingSince = DateTimeOffset.UnixEpoch }),
            Make.Item(Second, "作り物の髪").WithFiles(Make.File(app.NewFile(@"watched\here.zip"))));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        await app.Main.ReloadLibraryAsync();
        Select(search, First, Second);

        Assert.Equal(1, search.Bulk.MissingCount);
        Assert.Equal("見つからないファイルを探す…（1 件）", search.Bulk.FindMissingText);
        search.Bulk.FindMissingCommand.Execute(null);
        await app.SettleAsync();

        Assert.Single(app.SearchScopes);
        Assert.Equal("見つからないファイルを探す", Assert.Single(app.Notices).Caption);
    });

    // ---- 同じ帯を使うほかの画面 ----

    [Fact]
    public Task ショップの中でも同じ中身で_非表示にした物はこの一覧からも外す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(First, "作り物の衣装"));
        await app.AddItemAsync(Make.Item(Second, "作り物の髪"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shop.ListItems.OfType<ItemCardViewModel>().Count() == 2, "ショップの商品が並ぶ");
        var cards = shop.ListItems.OfType<ItemCardViewModel>().ToList();
        shop.SelectByClick(cards[0], control: true, shift: false);

        shop.Bulk.AddFavoritesCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(cards[0].IsFavorite);
        Assert.True((await LocalOfAsync(app, cards[0].Item.Id)).IsFavorite);

        shop.Bulk.HideCommand.Execute(null);
        await app.SettleAsync();
        Assert.True((await LocalOfAsync(app, cards[0].Item.Id)).IsHidden);
        Assert.Equal([cards[1].Item.Id], shop.ListItems.OfType<ItemCardViewModel>().Select(card => card.Item.Id));
        Assert.False(shop.HasSelection);
    });

    [Fact]
    public Task フォルダの右の欄でも同じ中身で_タグを足せる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(First, "作り物の衣装").WithFiles(Make.File(app.NewFile(@"shop\作り物1.zip"))));
        await app.AddItemAsync(Make.Item(Second, "作り物の髪").WithFiles(Make.File(app.NewFile(@"shop\作り物2.zip"))));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.Rows.Count > 0, "フォルダビューの読み込みが済む");
        var shopFolder = System.IO.Path.Combine(app.Root, "files", "shop");
        folders.Selected = folders.Rows.First(row => string.Equals(row.Path.TrimEnd('\\'), shopFolder, StringComparison.OrdinalIgnoreCase));
        await UiThread.Until(
            () => folders.Detail is FolderViewDetail detail && detail.ListItems.OfType<ItemCardViewModel>().Count() == 2,
            "フォルダの右の欄に商品が並ぶ");
        var detail = (FolderViewDetail)folders.Detail!;
        foreach (var card in detail.ListItems.OfType<ItemCardViewModel>())
        {
            detail.SelectByClick(card, control: true, shift: false);
        }

        app.AnswerBulkTag = model =>
        {
            model.Type(top: "衣装");
            return true;
        };
        detail.Bulk.AddTagCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["衣装"], (await LocalOfAsync(app, First)).UserTags.Select(tag => tag.Top));
        Assert.Equal(["衣装"], (await LocalOfAsync(app, Second)).UserTags.Select(tag => tag.Top));
        Assert.Equal("2 件に追加しました。", detail.Bulk.StatusText);
        Assert.Equal(2, detail.SelectedCount);
    });

    /// <summary>
    /// 帯のメニューの結び付けの名前が、3つの画面のどれにもある（名前を違えると結び付けは黙って外れ、押しても何も起きない）。
    /// メニューは画面の木に入らないので、帯の型から結び付けの道を読み、入れ物の型でたどる
    /// </summary>
    [Theory]
    [InlineData(typeof(SearchViewModel))]
    [InlineData(typeof(ShopViewModel))]
    [InlineData(typeof(FolderViewDetail))]
    public Task 帯のメニューの結び付けは3つの画面のどれでもたどれる(Type host) => UiThread.Run(() =>
    {
        var template = (DataTemplate)new ItemCardResources()["ItemSelectionBar"];
        var bar = (FrameworkElement)template.LoadContent();
        var paths = Descendants(bar).OfType<Button>()
            .Where(button => button.ContextMenu is not null)
            .SelectMany(button => button.ContextMenu.Items.OfType<MenuItem>())
            .SelectMany(item => new[] { MenuItem.CommandProperty, MenuItem.HeaderProperty, UIElement.IsEnabledProperty, UIElement.VisibilityProperty }
                .Select(property => BindingOperations.GetBinding(item, property)?.Path?.Path))
            .OfType<string>()
            .ToList();

        Assert.Contains("Bulk.AddFavoritesCommand", paths);
        Assert.Contains("Bulk.RefreshFromBoothCommand", paths);
        foreach (var path in paths)
        {
            var type = host;
            foreach (var name in path.Split('.'))
            {
                type = type.GetProperty(name)?.PropertyType ?? throw new Xunit.Sdk.XunitException($"{host.Name} に {path} がありません");
            }
        }

        return Task.CompletedTask;
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
