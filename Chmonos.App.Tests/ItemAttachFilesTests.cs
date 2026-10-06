using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページからファイルを直にこの商品へ紐付ける（ユーザ指示 2026-10-06）：ファイルの欄の「追加…」と、商品ページに zip を落としたときの3択。
/// 作者が同じ物を新しいIDで出し直すと、ファイルの手掛かりは古いIDを指すので、取り込みでは結べない。
/// </summary>
public class ItemAttachFilesTests
{
    private const string ItemId = "9900811";
    private const string OtherId = "9900812";

    /// <summary>ファイルを持たない商品を置き、その商品ページを開く。</summary>
    private static async Task<(MainViewModel Main, ItemViewModel Page)> OpenEmptyItemAsync(TestApp app, params LocalFileRecord[] files)
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の新しい衣装").WithFiles(files));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync(ItemId))!);
        await app.SettleAsync();
        return (main, Assert.IsType<ItemViewModel>(main.CurrentViewModel));
    }

    private static async Task<string> HashOfAsync(string path) => await Core.Scanning.FileHasher.ComputeSha256Async(path);

    private static async Task<LocalBlock> LocalOfAsync(TestApp app, string id) => (await app.Store.Items.LoadAsync(id))!.Local;

    private static async Task PressAddAsync(TestApp app, ItemViewModel page)
    {
        page.AttachFilesCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => !page.FilesNotice.Text.EndsWith('…'), "読み終える");
    }

    [Fact]
    public Task 追加で選んだzipがこの商品に紐付き_行がその場に出る() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var (main, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];

        await PressAddAsync(app, page);

        var file = Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Equal([zip], file.Paths);

        // ページは組み直さず、同じ画面の行だけが替わる
        Assert.Same(page, main.CurrentViewModel);
        Assert.Equal("作り物_v2.zip", Assert.Single(page.LocalFiles).FileName);
        Assert.StartsWith("1 件 / ", page.FileSummary);
        Assert.Equal(string.Empty, page.FilesNotice.Text);

        // 検索の写しにも届いている（カード・所持の絞り込みが古いまま残らない）
        Assert.Equal(ItemId, main.Search.FindFileOwner(file.Hash, "0")?.Id);
    });

    [Fact]
    public Task 追加で何も選ばなければ何も変えない() => TestApp.Run(async app =>
    {
        var (_, page) = await OpenEmptyItemAsync(app);

        await PressAddAsync(app, page);

        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Empty(page.LocalFiles);
    });

    [Fact]
    public Task 前に外していたファイルは印を下ろして紐付く() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var hash = await HashOfAsync(zip);
        var (_, page) = await OpenEmptyItemAsync(app, new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = 3, Detached = true });
        Assert.True(Assert.Single(page.LocalFiles).IsDetached);
        app.PickAttachFiles = () => [zip];

        await PressAddAsync(app, page);

        Assert.False(Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles).Detached);
        Assert.False(Assert.Single(page.LocalFiles).IsDetached);
    });

    [Fact]
    public Task 同じファイルを2回選ぶと_もう紐付いていると言う() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var (_, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];
        await PressAddAsync(app, page);

        await PressAddAsync(app, page);

        Assert.Equal("「作り物_v2.zip」は、もうこの商品に紐付いています。", page.FilesNotice.Text);
        Assert.True(page.FilesNotice.IsWarning);
        Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles);
    });

    [Fact]
    public Task 除外したファイルは窓で聞き_やめれば何も変えない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        await app.Store.Excluded.SaveAsync([new ExcludedEntry { Hash = await HashOfAsync(zip), Paths = [zip], ExcludedAt = DateTimeOffset.Now }]);
        var (_, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];

        await PressAddAsync(app, page);

        var asked = Assert.Single(app.Notices);
        Assert.Contains("除外を解除して、この商品に紐付けますか？", asked.Text);
        Assert.Equal(MessageBoxResult.Cancel, asked.DefaultResult);
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Single(app.Store.Excluded.Load());
        Assert.Equal(string.Empty, page.FilesNotice.Text);
    });

    [Fact]
    public Task 除外したファイルは_OKなら除外を解いて紐付ける() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        await app.Store.Excluded.SaveAsync([new ExcludedEntry { Hash = await HashOfAsync(zip), Paths = [zip], ExcludedAt = DateTimeOffset.Now }]);
        var (_, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];
        app.Answer = _ => MessageBoxResult.OK;

        await PressAddAsync(app, page);

        Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Empty(app.Store.Excluded.Load());
        Assert.Single(page.LocalFiles);
    });

    [Fact]
    public Task ほかの商品が持つファイルは選ばせ_付け直すとそちらから外してこちらに付ける() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v1.zip");
        await app.AddItemAsync(Make.Item(OtherId, "作り物の前の衣装").WithFiles(
            new LocalFileRecord { Hash = await HashOfAsync(zip), Paths = [zip], SizeBytes = 3 }));
        var (main, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];
        app.Choose = _ => ChoiceDialogResult.Second;

        await PressAddAsync(app, page);

        var asked = Assert.Single(app.Choices);
        Assert.Equal("ファイルを紐付ける", asked.Title);
        Assert.Equal("この商品に付け直す", asked.Second);
        Assert.Contains("「作り物の前の衣装」にはファイルが残りません。", asked.Detail);
        Assert.False(Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles).Detached);
        Assert.True(Assert.Single((await LocalOfAsync(app, OtherId)).LocalFiles).Detached);
        Assert.Same(page, main.CurrentViewModel);

        // 相手の商品も検索の写しで外れている（容量・所持が古いまま残らない）
        Assert.True(main.Search.FindItem(OtherId)!.Local.LocalFiles[0].Detached);
    });

    [Fact]
    public Task ほかの商品が持つファイルで_その商品を開くを選ぶと何も変えずにそちらへ移る() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v1.zip");
        await app.AddItemAsync(Make.Item(OtherId, "作り物の前の衣装").WithFiles(
            new LocalFileRecord { Hash = await HashOfAsync(zip), Paths = [zip], SizeBytes = 3 }));
        var (main, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];
        app.Choose = _ => ChoiceDialogResult.First;

        await PressAddAsync(app, page);

        Assert.Equal(OtherId, Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id);
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.False(Assert.Single((await LocalOfAsync(app, OtherId)).LocalFiles).Detached);
    });

    [Fact]
    public Task 未確定にあったファイルは紐付けたら未確定から消える() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        await app.Store.Unresolved.SaveAsync(
        [
            new UnresolvedFile { Hash = await HashOfAsync(zip), Paths = [zip], SizeBytes = 3, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now },
        ]);
        var (_, page) = await OpenEmptyItemAsync(app);
        app.PickAttachFiles = () => [zip];

        await PressAddAsync(app, page);

        Assert.Empty(app.Store.Unresolved.Load());
        Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles);
    });

    // ---- 商品ページに落としたとき ----

    [Fact]
    public Task 商品ページにzipを落とすと3択で聞き_紐付けるを選べばこの商品に紐付く() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var (main, page) = await OpenEmptyItemAsync(app);
        app.Choose = _ => ChoiceDialogResult.First;

        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();

        var asked = Assert.Single(app.Choices);
        Assert.Equal("「作り物_v2.zip」をどうしますか？", asked.Question);
        Assert.Equal("この商品に紐付ける", asked.First);
        Assert.Equal("取り込む", asked.Second);
        Assert.Null(asked.Third);
        Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Single(page.LocalFiles);
        Assert.Empty(main.Import.Folders);
    });

    [Fact]
    public Task 商品ページにzipを落として取り込むを選べば_いつも通り取り込みに積む() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        await app.ChangeSettingsAsync(settings => settings with { StartImportOnDrop = false });
        var (main, _) = await OpenEmptyItemAsync(app);
        app.Choose = _ => ChoiceDialogResult.Second;

        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();
        await UiThread.Until(() => main.Import.Folders.Count == 1, "取り込みに積む");

        Assert.Equal(zip, Assert.Single(main.Import.Folders));
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
    });

    [Fact]
    public Task 商品ページにzipを落としてキャンセルすれば何もしない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var (main, _) = await OpenEmptyItemAsync(app);

        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();

        Assert.Single(app.Choices);
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Empty(main.Import.Folders);
    });

    // ---- 画像と zip を混ぜて落としたとき（ユーザ判断 2026-10-06・L80 の 14） ----

    private static byte[] Png()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8);
        using var stream = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return stream.ToArray();
    }

    /// <summary>1つめの問い（紐付けるか取り込むか）には「この商品に紐付ける」、画像の問いには <paramref name="forImages"/> と答える。</summary>
    private static Func<ChoiceRequest, ChoiceDialogResult> Answer(ChoiceDialogResult forImages)
        => request => request.First == "この商品に紐付ける" ? ChoiceDialogResult.First : forImages;

    [Fact]
    public Task 画像とzipを混ぜて紐付けると画像は1回だけ聞き_画像として追加ならzipだけ紐付けて画像は商品の画像になる() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var red = app.NewFile(@"pics\red.png", Png());
        var blue = app.NewFile(@"pics\blue.png", Png());
        var (main, page) = await OpenEmptyItemAsync(app);
        app.Choose = Answer(ChoiceDialogResult.First);

        await main.HandleDropAsync([red, zip, blue], null);
        await app.SettleAsync();

        Assert.Equal(2, app.Choices.Count);
        var images = app.Choices[1];
        Assert.Equal("画像 2 件をどうしますか？", images.Question);
        Assert.Equal("画像として追加", images.First);
        Assert.Equal("ファイルとして紐付ける", images.Second);

        var local = await LocalOfAsync(app, ItemId);
        Assert.Equal([zip], Assert.Single(local.LocalFiles).Paths);
        Assert.Single(local.UserImages);
        Assert.Single(page.LocalFiles);
    });

    [Fact]
    public Task 画像とzipを混ぜてファイルとして紐付けるなら_画像もこの商品のファイルになる() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var red = app.NewFile(@"pics\red.png", Png());
        var (main, _) = await OpenEmptyItemAsync(app);
        app.Choose = Answer(ChoiceDialogResult.Second);

        await main.HandleDropAsync([red, zip], null);
        await app.SettleAsync();

        Assert.Equal("画像「red.png」をどうしますか？", app.Choices[1].Question);
        var local = await LocalOfAsync(app, ItemId);
        Assert.Equal(2, local.LocalFiles.Count);
        Assert.Empty(local.UserImages);
    });

    /// <summary>画像の問いをキャンセルしたら何もしない（zip だけ紐付けて画像を黙って捨てない）。</summary>
    [Fact]
    public Task 画像の問いをキャンセルすれば何もしない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var red = app.NewFile(@"pics\red.png", Png());
        var (main, _) = await OpenEmptyItemAsync(app);
        app.Choose = Answer(ChoiceDialogResult.Cancel);

        await main.HandleDropAsync([red, zip], null);
        await app.SettleAsync();

        Assert.Equal(2, app.Choices.Count);
        var local = await LocalOfAsync(app, ItemId);
        Assert.Empty(local.LocalFiles);
        Assert.Empty(local.UserImages);
    });

    /// <summary>画像が混ざっていなければ、画像の問いは出さない（今までどおり1回）。</summary>
    [Fact]
    public Task zipだけなら画像の問いは出ない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_v2.zip");
        var (main, _) = await OpenEmptyItemAsync(app);
        app.Choose = Answer(ChoiceDialogResult.First);

        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();

        Assert.Single(app.Choices);
    });

    [Fact]
    public void 落としたファイルが複数なら件数で聞く()
        => Assert.Equal(
            "2 件のファイルをどうしますか？",
            MainViewModel.AttachOrImportQuestion("作り物", [@"C:\a.zip", @"C:\b.zip"]).Question);
}
