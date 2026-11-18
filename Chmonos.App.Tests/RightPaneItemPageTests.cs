using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 左右に分かれた画面で、右の欄から商品ページを開くと右の欄の中に重ねる（open.md 95 行・ユーザ判断 2026-11-18・11-19）。
/// 左の選びは残り、「← 一覧に戻る」（Alt+← も）で前の右の中身へ戻る。1段だけで、Alt+← の2回目は画面の履歴へ回る。
/// </summary>
public class RightPaneItemPageTests
{
    private static async Task<(MainViewModel Main, FolderViewModel Folders, FolderViewDetail Detail)> OpenFolderAsync(TestApp app)
    {
        for (var index = 1; index <= 3; index++)
        {
            var file = app.NewFile($@"shop\作り物{index}.zip");
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}").WithFiles(Make.File(file)));
        }

        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.Rows.Count > 0, "フォルダビューの読み込みが済む");
        var shopFolder = System.IO.Path.Combine(app.Root, "files", "shop");
        folders.Selected = folders.Rows.First(row => string.Equals(row.Path.TrimEnd('\\'), shopFolder, StringComparison.OrdinalIgnoreCase));
        await UiThread.Until(
            () => folders.Detail is FolderViewDetail detail && detail.ListItems.OfType<ItemCardViewModel>().Count() == 3,
            "フォルダの右の欄に商品が並ぶ");
        return (main, folders, (FolderViewDetail)folders.Detail!);
    }

    [Fact]
    public Task フォルダの右の一覧から開くと右の欄に重ね_一覧に戻ると前の一覧と左の選びが残っている() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        var selected = folders.Selected;
        var card = detail.ListItems.OfType<ItemCardViewModel>().First();

        detail.OpenItem(card);

        Assert.Same(folders, main.CurrentViewModel);
        Assert.Equal(card.Item.Id, folders.RightItem.Page?.Item.Id);
        Assert.True(folders.RightItem.Page!.IsEmbedded);
        Assert.Same(selected, folders.Selected);
        Assert.Same(detail, folders.Detail);

        folders.RightItem.BackCommand.Execute(null);

        Assert.False(folders.RightItem.IsOpen);
        Assert.Same(detail, folders.Detail);
    });

    [Fact]
    public Task AltLeftの1回目は右の中を閉じ_2回目は画面の履歴へ戻る() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.Same(folders, main.CurrentViewModel);
        Assert.False(folders.RightItem.IsOpen);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        await app.SettleAsync();
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 左で別の物を選ぶと_重ねた商品ページは閉じる() => TestApp.Run(async app =>
    {
        var (_, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        folders.Selected = folders.Rows.First(row => !ReferenceEquals(row, folders.Selected));

        Assert.False(folders.RightItem.IsOpen);
    });

    [Fact]
    public Task 窓が手前に戻って木を読み直しても_重ねた商品ページは閉じない() => TestApp.Run(async app =>
    {
        var (_, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        folders.NoteWindowActivated();
        await app.SettleAsync();

        Assert.True(folders.RightItem.IsOpen);
    });

    [Fact]
    public Task 重ねたページでファイルを外して商品が消えたら_右の一覧も読み直して消えたカードを残さない() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        var card = detail.ListItems.OfType<ItemCardViewModel>().First();
        detail.OpenItem(card);
        var page = folders.RightItem.Page!;

        // 商品ページの「ファイルを外す」が済んだ後と同じ形：最後のファイルを外して商品を消し、ページは持ち主へ差し替えを頼む
        var hash = card.Item.Local.OwnedFiles.Single().Hash;
        await app.Services.Commands.ExecuteAsync(new UiCommand.DetachFile(card.Item.Id, hash, DeleteItemWhenEmpty: true));
        Assert.Null(await app.Store.Items.LoadAsync(card.Item.Id));
        await main.ReloadLibraryAsync();
        page.Replaced!(null);

        await UiThread.Until(
            () => folders.Detail is FolderViewDetail now && now.ListItems.OfType<ItemCardViewModel>().All(other => other.Item.Id != card.Item.Id),
            "右の一覧から消えた商品のカードが無くなる");
        Assert.False(folders.RightItem.IsOpen);
    });

    [Fact]
    public Task 別の画面へ移って戻ると_同じフォルダの上に同じ商品を重ね直す() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        var card = detail.ListItems.OfType<ItemCardViewModel>().Last();
        detail.OpenItem(card);

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();
        await app.SettleAsync();

        var reopened = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        Assert.NotSame(folders, reopened);
        await UiThread.Until(() => reopened.RightItem.IsOpen, "戻ると商品ページを重ね直す");
        Assert.Equal(card.Item.Id, reopened.RightItem.Page!.Item.Id);
        Assert.IsType<FolderViewDetail>(reopened.Detail);
    });

    [Fact]
    public Task 重ねた商品ページに落としたzipは_その商品へ紐付けるかを聞く() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());
        Assert.Same(folders.RightItem.Page, main.CurrentItemPage);

        var zip = app.NewFile("作り物_v2.zip");
        app.Choose = _ => ChoiceDialogResult.Cancel;
        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();

        Assert.Equal("この商品に紐付ける", Assert.Single(app.Choices).First);
    });

    [Fact]
    public Task 改変の画面の右の商品ページを開くも右の欄に重ねる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var main = await app.StartAsync();
        var record = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "作り物の改変"))).Record;
        main.ShowModifications(ModificationHubLevel.Modification);
        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => hub.Groups.OfType<HubModificationRow>().Any(), "改変の画面の読み込みが済む");
        hub.ShowModificationCommand.Execute(record);
        var modification = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.NotNull(modification.OpenItemInPane);

        hub.OpenItemPageCommand.Execute("9900001");
        await UiThread.Until(() => hub.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        Assert.Same(hub, main.CurrentViewModel);
        Assert.Same(modification, hub.Detail);
        Assert.Same(hub.RightItem.Page, main.CurrentItemPage);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.False(hub.RightItem.IsOpen);
        Assert.Same(modification, hub.Detail);
    });

    // ---- アバター・タグ・属性の管理（open.md 95 行の残り） ----

    private const string AvatarA = "2000001";
    private const string AvatarB = "2000002";

    private static async Task<(MainViewModel Main, AvatarsViewModel Avatars)> OpenAvatarsAsync(TestApp app)
    {
        // 商品ページを開けるのは商品として取り込んであるアバターだけ
        await app.AddItemAsync(Make.Item(AvatarA, "作り物のアバター甲"));
        await app.AddItemAsync(Make.Item(AvatarB, "作り物のアバター乙"));
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = AvatarA, BoothName = "作り物のアバター甲", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = AvatarB, BoothName = "作り物のアバター乙", AvatarOverride = true },
            ],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count == 2 && avatars.CanOpenItem, "アバターの読み込みが済む");
        return (main, avatars);
    }

    [Fact]
    public Task アバターの管理の商品ページを開くは右の欄に重ね_戻ると閉じ_左で別のアバターを選ぶと閉じる() => TestApp.Run(async app =>
    {
        var (main, avatars) = await OpenAvatarsAsync(app);
        var selected = avatars.Selected!;

        avatars.OpenItemCommand.Execute(null);
        await UiThread.Until(() => avatars.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        Assert.Same(avatars, main.CurrentViewModel);
        Assert.Equal(selected.ItemId, avatars.RightItem.Page!.Item.Id);
        Assert.Same(selected, avatars.Selected);
        Assert.Same(avatars.RightItem.Page, main.CurrentItemPage);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.False(avatars.RightItem.IsOpen);
        Assert.Same(avatars, main.CurrentViewModel);
        Assert.Same(selected, avatars.Selected);

        avatars.OpenItemCommand.Execute(null);
        await UiThread.Until(() => avatars.RightItem.IsOpen, "もう一度重ねる");
        avatars.Selected = avatars.Rows.First(row => row.ItemId != selected.ItemId);
        Assert.False(avatars.RightItem.IsOpen);
    });

    [Fact]
    public Task アバターの管理は_書き込みの後の読み直しでは重ねた商品ページを閉じない() => TestApp.Run(async app =>
    {
        var (_, avatars) = await OpenAvatarsAsync(app);
        var selectedId = avatars.Selected!.ItemId;
        avatars.OpenItemCommand.Execute(null);
        await UiThread.Until(() => avatars.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        // 素体を決めると書き込んで読み直し、同じアバターの新しい行へ選び直す
        avatars.PickBaseCommand.Execute("作り物の素体");
        await UiThread.Until(() => avatars.Selected?.Summary.Entry.BaseName == "作り物の素体", "読み直して素体が付く");

        Assert.Equal(selectedId, avatars.Selected!.ItemId);
        Assert.True(avatars.RightItem.IsOpen);
    });

    [Fact]
    public Task アバターの管理は_別の画面へ移って戻ると_同じアバターの上に同じ商品を重ね直す() => TestApp.Run(async app =>
    {
        var (main, avatars) = await OpenAvatarsAsync(app);
        avatars.Selected = avatars.Rows.First(row => row.ItemId == AvatarB);
        avatars.OpenItemCommand.Execute(null);
        await UiThread.Until(() => avatars.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();
        await app.SettleAsync();

        var reopened = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        Assert.NotSame(avatars, reopened);
        await UiThread.Until(() => reopened.RightItem.IsOpen, "戻ると商品ページを重ね直す");
        Assert.Equal(AvatarB, reopened.RightItem.Page!.Item.Id);
        Assert.Equal(AvatarB, reopened.Selected?.ItemId);
    });

    [Fact]
    public Task アバターの管理の見方を替えると_重ねた商品ページは閉じる() => TestApp.Run(async app =>
    {
        var (main, avatars) = await OpenAvatarsAsync(app);
        avatars.OpenItemCommand.Execute(null);
        await UiThread.Until(() => avatars.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        avatars.ShowConversionModeCommand.Execute(null);

        Assert.False(avatars.RightItem.IsOpen);
        Assert.Same(avatars, main.CurrentViewModel);
    });

    [Fact]
    public Task 変換の商品ページを開くも右の欄に重ね_重ねている間はCtrlEnterの確認済みにして次へが効かない() => TestApp.Run(async app =>
    {
        // 変換ツールのプロファイルを持つ商品（AvatarsConversionTests と同じ作り）。つなぎ先は確認待ちなので Ctrl+Enter が押せる
        const string hash = "1111111111111111111111111111111111111111111111111111111111111111";
        new Chmonos.Core.Storage.UnityPackagePathStore(app.Services.Paths).Add(hash, "Profile.unitypackage",
        [
            new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2fakea.json"),
        ]);
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター", DisplayName = "作り物", Category = "3Dキャラクター" }],
        });
        var profile = Make.Item("9900861", "作り物のプロファイル");
        await app.AddItemAsync(profile with
        {
            Local = profile.Local with
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [@"D:\fake\9900861.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
                Conversion = new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakea" }] },
            },
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().Single(row => row.ItemId == "9900861");
        Assert.True(avatars.DecideSuggestedCommand.CanExecute(null));

        avatars.OpenConversionItemCommand.Execute(null);

        Assert.Same(avatars, app.Main.CurrentViewModel);
        Assert.Equal("9900861", avatars.RightItem.Page?.Item.Id);
        Assert.Equal("9900861", avatars.SelectedConversion?.ItemId);
        // 画面全体のキーなので、重ねている間に押すと隠れた変換の欄に書いて次へ進んでしまう
        Assert.False(avatars.DecideSuggestedCommand.CanExecute(null));

        Assert.True(app.Main.RunShortcut(ShortcutAction.Back));
        Assert.False(avatars.RightItem.IsOpen);
        Assert.True(avatars.DecideSuggestedCommand.CanExecute(null));
    });

    private static UserTagMaster TwoTops() => new()
    {
        Tops = [new UserTagTop { Name = "作り物の甲" }, new UserTagTop { Name = "作り物の乙" }],
    };

    private static async Task<(MainViewModel Main, TagManageViewModel Tags)> OpenTagsAsync(TestApp app)
    {
        // ファイルを1つ持たせる（重ねたページでファイルを外して商品が消える試験に使う）
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(app.NewFile(@"shop\作り物の衣装.zip")));
        await app.AddItemAsync(item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = "作り物の甲" }] } });
        await app.Store.UserTags.SaveAsync(TwoTops());
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount == 2 && tags.Selected is not null, "タグの管理の読み込みが済む");
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の甲");
        return (main, tags);
    }

    [Fact]
    public Task タグの管理の右の商品を開くと右の欄に重ね_読み直しでは閉じず_左で別の大分類を選ぶと閉じる() => TestApp.Run(async app =>
    {
        var (main, tags) = await OpenTagsAsync(app);
        var selected = tags.Selected;

        tags.OpenItem(main.Search.CardFor("1000001")!);

        Assert.Same(tags, main.CurrentViewModel);
        Assert.Equal("1000001", tags.RightItem.Page?.Item.Id);
        Assert.True(tags.RightItem.Page!.IsEmbedded);
        Assert.Same(selected, tags.Selected);
        Assert.Same(tags.RightItem.Page, main.CurrentItemPage);

        // 読み直しは同じ名前の新しい行へ選び直すだけなので閉じない（重ねたページでの書き込みのたびに閉じないように）
        await tags.ReloadAsync();
        await app.SettleAsync();
        Assert.NotSame(selected, tags.Selected);
        Assert.True(tags.RightItem.IsOpen);

        tags.Selected = tags.Tops.First(row => row.Name == "作り物の乙");
        Assert.False(tags.RightItem.IsOpen);
    });

    [Fact]
    public Task タグの管理は_別の画面へ移って戻ると_同じ大分類の上に同じ商品を重ね直す() => TestApp.Run(async app =>
    {
        var (main, tags) = await OpenTagsAsync(app);
        tags.OpenItem(main.Search.CardFor("1000001")!);

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();
        await app.SettleAsync();

        var reopened = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        Assert.NotSame(tags, reopened);
        await UiThread.Until(() => reopened.RightItem.IsOpen, "戻ると商品ページを重ね直す");
        Assert.Equal("1000001", reopened.RightItem.Page!.Item.Id);
        Assert.Equal("作り物の甲", reopened.Selected?.Name);
    });

    [Fact]
    public Task タグの管理は_重ねたページで書いてから同じカードで開き直しても_書いた後の商品で開く() => TestApp.Run(async app =>
    {
        var (main, tags) = await OpenTagsAsync(app);
        // 画面のカードは作った時の商品を持ったまま残る（閉じても画面を作り直さない）
        var card = main.Search.CardFor("1000001")!;
        tags.OpenItem(card);
        tags.RightItem.Page!.ToggleFavoriteCommand.Execute(null);
        await UiThread.Until(() => main.Search.FindItem("1000001")?.Local.IsFavorite == true, "お気に入りが書かれる");
        tags.RightItem.BackCommand.Execute(null);

        tags.OpenItem(card);

        Assert.True(tags.RightItem.Page!.IsFavorite);
    });

    [Fact]
    public Task タグの管理は_重ねたページでファイルを外して商品が消えたら_読み直して件数から外す() => TestApp.Run(async app =>
    {
        var (main, tags) = await OpenTagsAsync(app);
        Assert.Equal(1, tags.Selected!.ItemCount);
        tags.OpenItem(main.Search.CardFor("1000001")!);
        var page = tags.RightItem.Page!;

        // 商品ページの「ファイルを外す」が済んだ後と同じ形：最後のファイルを外して商品を消し、ページは持ち主へ差し替えを頼む
        var hash = page.Item.Local.OwnedFiles.Single().Hash;
        await app.Services.Commands.ExecuteAsync(new UiCommand.DetachFile("1000001", hash, DeleteItemWhenEmpty: true));
        await main.ReloadLibraryAsync();
        page.Replaced!(null);

        await UiThread.Until(() => tags.Selected?.ItemCount == 0, "読み直して、消えた商品を件数から外す");
        Assert.False(tags.RightItem.IsOpen);
        Assert.Equal("作り物の甲", tags.Selected!.Name);
    });

    [Fact]
    public Task 属性の管理の右の商品の行を押すと右の欄に重ね_AltLeftの1回目は閉じ_2回目は画面の履歴へ戻る() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item with { Local = item.Local with { Attributes = new Dictionary<string, int> { ["作り物の質感"] = 40 } } });
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の質感" }] });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0 && attributes.Selected is not null, "属性の管理の読み込みが済む");
        attributes.IsItemsExpanded = true;
        await UiThread.Until(() => attributes.Items.Count == 1, "右に商品が並ぶ");
        var selected = attributes.Selected;

        attributes.Items[0].OpenCommand!.Execute(null);

        Assert.Same(attributes, main.CurrentViewModel);
        Assert.Equal("1000001", attributes.RightItem.Page?.Item.Id);
        Assert.Same(selected, attributes.Selected);
        Assert.Same(attributes.RightItem.Page, main.CurrentItemPage);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.Same(attributes, main.CurrentViewModel);
        Assert.False(attributes.RightItem.IsOpen);
        Assert.Same(selected, attributes.Selected);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        await app.SettleAsync();
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
    });
}
