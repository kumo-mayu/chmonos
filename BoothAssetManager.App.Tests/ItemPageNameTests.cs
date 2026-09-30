using System.IO;
using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 商品ページの部品の、読み上げ・自動操作での名前。名前は ViewModel が作って XAML が結ぶだけなので、ここで文を確かめる
/// （前は、どの絵も「この画像を表示」、欄の三角は開いていても「バリエーションを開く」だった）。
/// </summary>
public class ItemPageNameTests
{
    [Fact]
    public Task ギャラリーの小さな絵は_何枚目かを名前に持つ() => TestApp.Run(async app =>
    {
        string[] urls =
        [
            "https://sample.invalid/1000001/a.png",
            "https://sample.invalid/1000001/b.png",
            "https://sample.invalid/1000001/c.png",
        ];
        var item = Make.Item("1000001", "作り物の衣装");
        item = item with
        {
            Booth = item.Booth with { Images = urls.Select(url => new BoothImage { OriginalUrl = url }).ToList() },
        };
        await app.AddItemAsync(item);

        var directory = app.Services.Paths.ItemImagesDir(item.Id);
        Directory.CreateDirectory(directory);
        foreach (var url in urls)
        {
            using var image = new Image<Rgba32>(4, 4);
            image.SaveAsWebp(Path.Combine(directory, ImagePipeline.FileNameFor(url)));
        }

        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        // 末尾の「足す」枠は画像ではないので、番号を持たない
        Assert.Equal(
            ["1 枚目の画像を表示", "2 枚目の画像を表示", "3 枚目の画像を表示"],
            page.GalleryTiles.Where(tile => tile.IsImage).Select(tile => tile.ShowName));
        Assert.Equal(string.Empty, Assert.Single(page.GalleryTiles, tile => tile.IsAddTile).ShowName);
    });

    [Fact]
    public Task バリエーションの欄の三角は_押すと起きることを名前にする() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        // 開閉はアプリを閉じるまで保つ値なので、試験の後へ持ち越さない
        var before = page.IsVariationsExpanded;
        try
        {
            var changed = new List<string?>();
            page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            page.IsVariationsExpanded = true;
            Assert.Equal("バリエーションを折りたたむ", page.VariationsToggleName);

            changed.Clear();
            page.IsVariationsExpanded = false;
            Assert.Equal("バリエーションを開く", page.VariationsToggleName);

            // 名前が変わったことを知らせないと、読み上げは押す前の名前を読み続ける
            Assert.Contains(nameof(ItemViewModel.VariationsToggleName), changed);
        }
        finally
        {
            page.IsVariationsExpanded = before;
        }
    });

    [Theory]
    [InlineData("バリエーション", true, "バリエーションを折りたたむ")]
    [InlineData("バリエーション", false, "バリエーションを開く")]
    [InlineData("ローカルファイル", true, "ローカルファイルを折りたたむ")]
    [InlineData("ローカルファイル", false, "ローカルファイルを開く")]
    public void 欄の三角の名前は_どの欄でも同じ形(string section, bool expanded, string expected)
        => Assert.Equal(expected, ItemViewModel.ToggleName(section, expanded));
}
