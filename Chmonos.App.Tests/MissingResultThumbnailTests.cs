using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 探した結果の行の左に出す、商品のサムネイルの決め方（メモ76）。決め方は検索のカードと同じ（★の指名が勝ち、無ければ並びの1枚目）。
/// 絵が無い商品・消えた商品は、絵の場所が無いまま頭文字が出る。
/// </summary>
public class MissingResultThumbnailTests
{
    private static readonly string[] Urls =
    [
        "https://sample.invalid/9900001/a.png",
        "https://sample.invalid/9900001/b.png",
    ];

    private static ItemRecord ItemWithImages(string? pinned)
    {
        var item = Make.Item("9900001", "作り物の衣装");
        return item with
        {
            Booth = item.Booth with { Images = [.. Urls.Select(url => new BoothImage { OriginalUrl = url })] },
            Local = item.Local with { ThumbnailImage = pinned },
        };
    }

    private static string[] FilesOf(ItemRecord item, string directory)
        => [.. item.Booth.Images.Select(image => Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl)))];

    [Fact]
    public void 指名が無ければ_並びの1枚目()
    {
        var item = ItemWithImages(null);
        var files = FilesOf(item, @"D:\作り物\画像");

        Assert.Equal(files[0], ResultThumbnail.PathOf(item, @"D:\作り物\画像", files, ThumbnailRole.Default));
    }

    [Fact]
    public void 星で指名した絵があれば_それが勝つ()
    {
        var directory = @"D:\作り物\画像";
        var files = FilesOf(ItemWithImages(null), directory);
        var item = ItemWithImages(Path.GetFileName(files[1]));

        Assert.Equal(files[1], ResultThumbnail.PathOf(item, directory, files, ThumbnailRole.Default));
    }

    [Fact]
    public void 絵が1枚も無ければ_場所は無い()
        => Assert.Null(ResultThumbnail.PathOf(ItemWithImages(null), @"D:\作り物\画像", [], ThumbnailRole.Default));

    [Fact]
    public Task 行の絵は_見えて読まれるまで決めず_星の指名で決め_商品が消えていれば頭文字だけ() => TestApp.Run(async app =>
    {
        var item = ItemWithImages(null);
        var directory = app.Services.Paths.ItemImagesDir(item.Id);
        Directory.CreateDirectory(directory);
        var files = FilesOf(item, directory);
        foreach (var file in files)
        {
            using var image = new Image<Rgba32>(4, 4);
            image.SaveAsWebp(file);
        }

        item = item with { Local = item.Local with { ThumbnailImage = Path.GetFileName(files[1]) } };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        main.Import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = 2,
            Relinked = 0,
            Hashed = 0,
            NotFoundFiles =
            [
                new MissingFileOutcome { ItemId = item.Id, ItemName = item.DisplayName, OldPaths = [@"D:\作り物\a.zip"] },
                new MissingFileOutcome { ItemId = "9900002", ItemName = "消えた商品", OldPaths = [@"D:\作り物\b.zip"] },
            ],
        });

        var rows = main.Import.NotFoundFiles;
        var pinned = rows.Single(row => row.ItemId == item.Id).Picture!;
        var gone = rows.Single(row => row.ItemId == "9900002").Picture!;

        // 画面が行を作って読むまでは、記録を読みに行かない（見えない行は何も読まない）
        Assert.Null(pinned.Path);

        _ = pinned.Image;
        _ = gone.Image;
        await UiThread.Until(() => pinned.Path is not null, "指名した絵の場所が決まる");
        await app.SettleAsync();

        Assert.Equal(files[1], pinned.Path);
        Assert.Null(gone.Path);
        Assert.Equal("消", gone.Initial);
    });
}
