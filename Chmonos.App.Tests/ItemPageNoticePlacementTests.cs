using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの操作の結果が、押した欄の下の知らせ（<see cref="AreaNotice"/>）に出ること。
/// 上の段の1行（<see cref="ItemViewModel.RefreshStatus"/>）は「取り直す」の結果だけ（2026-10-03 のユーザの方針）。
/// どの欄の下かは XAML が結ぶので、ここでは「どの入れ物に出たか」を確かめる。
/// </summary>
public class ItemPageNoticePlacementTests
{
    private const string ItemId = "1000001";

    private static async Task<ItemViewModel> OpenAsync(TestApp app)
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    [Fact]
    public Task IDをコピーすると_IDの下に出て_上の段には出ない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);

        page.CopyIdCommand.Execute(null);

        Assert.Equal($"{ItemId} をコピーしました。", page.IdNotice.Text);
        Assert.False(page.IdNotice.IsWarning);
        Assert.Equal(string.Empty, page.RefreshStatus);
    });

    [Fact]
    public Task 画像ファイルを読めなかったら_ギャラリーの下に注意として出る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);
        var missing = Path.Combine(app.Root, "files", "無いファイル.png");

        await page.AddImageFilesAsync([missing]);

        Assert.Equal("無いファイル.png を読めませんでした。", page.GalleryNotice.Text);
        Assert.True(page.GalleryNotice.IsWarning);
        Assert.Equal(string.Empty, page.RefreshStatus);
    });

    [Fact]
    public Task 画像でないファイルを足したら_ギャラリーの下に注意として出る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);
        var notImage = app.NewFile("作り物の文章.png", [1, 2, 3]);

        await page.AddImageFilesAsync([notImage]);

        Assert.Equal("作り物の文章.png は画像として読めませんでした。", page.GalleryNotice.Text);
        Assert.True(page.GalleryNotice.IsWarning);
        Assert.Equal(string.Empty, page.RefreshStatus);
    });

    [Fact]
    public Task 画像を足せたら_ギャラリーの下に済んだこととして出る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);
        using var image = new Image<Rgba32>(4, 4);
        var path = Path.Combine(app.Root, "files", "作り物の絵.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await image.SaveAsPngAsync(path);

        await page.AddImageFilesAsync([path]);

        Assert.Equal("画像を 1 枚追加しました。", page.GalleryNotice.Text);
        Assert.False(page.GalleryNotice.IsWarning);
        Assert.Equal(string.Empty, page.RefreshStatus);
    });

    [Fact]
    public Task 外したファイルを戻せなかったら_ファイルの欄の下に出る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);
        var row = new LocalFileRow
        {
            Hash = "ffff",
            FileName = "作り物.zip",
            SizeText = "1 KB",
            Paths = [],
        };

        page.ReattachFileCommand.Execute(row);
        await app.SettleAsync();

        Assert.NotEqual(string.Empty, page.FilesNotice.Text);
        Assert.True(page.FilesNotice.IsWarning);
        Assert.Equal(string.Empty, page.RefreshStatus);
        Assert.Equal(string.Empty, page.GalleryNotice.Text);
    });

    [Fact]
    public Task 取り直しの失敗は_今までどおり上の段に出る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);

        // 作り物の BOOTH は何も教えなければ 404 を返す
        page.RefreshCommand.Execute(null);
        await UiThread.Until(() => page.HasRefreshStatus, "取り直しの結果が出る");

        Assert.NotEqual(string.Empty, page.RefreshStatus);
        Assert.Equal(string.Empty, page.IdNotice.Text);
        Assert.Equal(string.Empty, page.GalleryNotice.Text);
    });
}
