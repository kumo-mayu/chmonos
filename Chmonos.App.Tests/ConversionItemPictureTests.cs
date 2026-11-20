using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」のプロファイルの商品の行の頭の絵（ユーザ判断 2026-11-20・③）。決め方は検索のカードと同じ（★の指名が勝つ）で、
/// 見えて読まれるまで場所を決めない。群で分けた一覧は見える行だけ作らせる（作った行の分だけ絵を読むので、流しても重くしない）。
/// 名前・ID は作り物。
/// </summary>
public class ConversionItemPictureTests
{
    [Fact]
    public Task 行の絵は_見えて読まれるまで決めず_星の指名した絵を出す() => TestApp.Run(async app =>
    {
        const string id = "9900891";
        var directory = app.Services.Paths.ItemImagesDir(id);
        Directory.CreateDirectory(directory);
        string[] urls = ["https://sample.invalid/9900891/a.png", "https://sample.invalid/9900891/b.png"];
        var files = urls.Select(url => Path.Combine(directory, ImagePipeline.FileNameFor(url))).ToArray();
        foreach (var file in files)
        {
            using var image = new Image<Rgba32>(4, 4);
            image.SaveAsWebp(file);
        }

        await ConversionItemGroupTests.SaveProfilesAsync(app, id, "作り物のプロファイル", null, item => item with
        {
            Booth = item.Booth with { Images = [.. urls.Select(url => new BoothImage { OriginalUrl = url })] },
            Local = item.Local with { ThumbnailImage = Path.GetFileName(files[1]) },
        }, "fakea");

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");

        var picture = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().Single().Picture!;
        Assert.Null(picture.Path);
        Assert.Equal("作", picture.Initial);

        _ = picture.Image;
        await UiThread.Until(() => picture.Path is not null, "指名した絵の場所が決まる");
        await app.SettleAsync();

        Assert.Equal(files[1], picture.Path);
    });

    [Fact]
    public void 群で分けた一覧は_見える行だけ作る()
    {
        var xaml = File.ReadAllText(AvatarsViewPath());
        var list = Regex.Match(xaml, @"<ListBox ItemsSource=""\{Binding ConversionItems\}""[^>]*>").Value;

        Assert.Contains(@"VirtualizingPanel.IsVirtualizingWhenGrouping=""True""", list);
        Assert.Contains(@"VirtualizingPanel.VirtualizationMode=""Recycling""", list);
    }

    private static string AvatarsViewPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views", "AvatarsView.xaml");
}
