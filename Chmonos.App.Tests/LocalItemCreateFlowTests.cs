using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の件数の帯の「BOOTHに無い商品を登録…」（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 名前を窓で聞き、ファイルを持たない仮IDの商品を作って、その商品ページを開く。BOOTH へは問い合わせない。
/// </summary>
public class LocalItemCreateFlowTests
{
    [Fact]
    public Task 名前を入れると_ファイルの無い商品ができて商品ページが開き_検索にも並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "BOOTHの商品"));
        var main = await app.StartAsync();
        app.NameLocalItem = model =>
        {
            model.Name = "  手で作った衣装  ";
            return true;
        };

        Assert.True(main.CreateLocalItemCommand.CanExecute(null));
        await main.CreateLocalItemAsync();

        Assert.Single(app.LocalItemNameDialogs);
        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        Assert.True(page.Item.IsLocalOnly);
        Assert.False(page.Item.IsOwned);
        Assert.Equal("手で作った衣装", page.Item.Local.DisplayName);

        // 作った商品はディスクにもある（命令を通って書かれた）
        Assert.NotNull(await app.Store.Items.LoadAsync(page.Item.Id));

        // 全件を読み直さず、検索の写しへ1件足している
        Assert.Contains(main.Search.ListItems, card => card.Item.Id == page.Item.Id);

        // 仮IDの商品なので、BOOTH へは一度も問い合わせない
        Assert.Empty(app.Booth.Requests);

        // 後から付けられるよう、ファイルの欄の「追加…」は押せる
        Assert.True(page.AttachFilesCommand.CanExecute(null));
    });

    /// <summary>未確定の「BOOTHに無い商品として登録する」と同じに、画像を添えて作ると、商品の「自分で足す画像」に入る。</summary>
    [Fact]
    public Task 画像を添えて作ると_商品の自分の画像に入る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var red = app.NewFile(@"pics\red.png", Png(200));
        var blue = app.NewFile(@"pics\blue.png", Png(20));
        var text = app.NewFile(@"pics\memo.txt", [1, 2, 3]);
        app.NameLocalItem = model =>
        {
            model.Name = "手で作った衣装";
            model.AddLocalImages([red, blue, red, text]);

            // 同じ画像と画像でない物は添えない。外すと枚数の文も変わる
            Assert.Equal(2, model.LocalImages.Count);
            Assert.Equal("画像 2 枚を一緒に追加します。", model.LocalImagesText);
            model.RemoveLocalImageCommand.Execute(model.LocalImages[1]);
            Assert.Single(model.LocalImages);
            model.AddLocalImages([blue]);
            return true;
        };

        await main.CreateLocalItemAsync();

        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        var saved = await app.Store.Items.LoadAsync(page.Item.Id);
        Assert.Equal(2, saved!.Local.UserImages.Count);
        Assert.Empty(app.Notices);
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task 画像が読めなくても商品は作り_読めなかった枚数を知らせる() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var good = app.NewFile(@"pics\red.png", Png(200));
        // 拡張子は画像でも中身は画像でない
        var broken = app.NewFile(@"pics\broken.png", [1, 2, 3, 4]);
        app.NameLocalItem = model =>
        {
            model.Name = "手で作った衣装";
            model.AddLocalImages([good, broken]);
            return true;
        };

        await main.CreateLocalItemAsync();

        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        Assert.Single((await app.Store.Items.LoadAsync(page.Item.Id))!.Local.UserImages);
        var notice = Assert.Single(app.Notices);
        Assert.Contains("画像 1 枚を追加できませんでした。商品ページの「＋」から追加してください。", notice.Text);
    });

    private static byte[] Png(byte red)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8, new SixLabors.ImageSharp.PixelFormats.Rgba32(red, 100, 100));
        using var stream = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return stream.ToArray();
    }

    [Fact]
    public Task 窓を閉じたら何も作らない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var before = main.CurrentViewModel;

        await main.CreateLocalItemAsync();

        Assert.Single(app.LocalItemNameDialogs);
        Assert.Empty(app.Store.Items.EnumerateItemIds());
        Assert.Same(before, main.CurrentViewModel);
    });

    /// <summary>
    /// 作った商品へ、未確定の「商品IDで登録」に仮ID（乱数12桁）を入れて、後からファイルを付けられる（フォルダもこの道で付ける）。
    /// 欄が受けるのは手元に在る商品の仮IDだけなので、作った後に開いた未確定がその商品を知っているかを確かめる。BOOTH へは問い合わせない
    /// </summary>
    [Fact]
    public Task 作った商品の仮IDを未確定の欄に入れると_そのファイルを付けられる() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"own\手作り.zip");
        await app.Store.Unresolved.SaveAsync(
        [
            new Core.Models.UnresolvedFile
            {
                Hash = Make.HashOf(path),
                Paths = [path],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            },
        ]);
        var main = await app.StartAsync();
        app.NameLocalItem = model =>
        {
            model.Name = "手で作った衣装";
            return true;
        };
        await main.CreateLocalItemAsync();
        var itemId = Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id;

        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        resolve.ItemIdInput = itemId;
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(resolve.AssignCommand.CanExecute(null));
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(itemId);
        Assert.True(item!.IsOwned);
        Assert.Equal("手で作った衣装", item.Local.DisplayName);
        Assert.Empty(app.Booth.Requests);
    });

    /// <summary>空白だけの名前では窓の「登録」を押せない（作ると画面に local-… しか出ない）。</summary>
    [Fact]
    public void 空白だけの名前では登録を押せない()
    {
        var model = new LocalItemNameDialogViewModel();
        Assert.False(model.CanCommit);

        model.Name = "   ";
        Assert.False(model.CanCommit);

        model.Name = "衣装";
        Assert.True(model.CanCommit);
    }
}
