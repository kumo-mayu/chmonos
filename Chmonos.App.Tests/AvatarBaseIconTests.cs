using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 共通素体の絵を、素体を選ぶ・探す欄の候補と商品ページの素体の札にも出す（152 の続き・open.md 73 行）。
/// 決め方は <see cref="AvatarBaseIcons"/>（付けた画像 → 素体の商品の1枚目 → 頭の字）1つ。絵が出るか（null か）と、引く場所を見る
/// </summary>
public sealed class AvatarBaseIconTests
{
    private const string WithImage = "作り物の素体";
    private const string NoImage = "画像の無い素体";
    private const string ItemId = "9900811";

    /// <summary>1×1 の PNG。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>色だけ違う 2×2 の PNG（中身が違えば別の名前で置かれる）。</summary>
    private static byte[] OtherPng()
    {
        var pixels = Enumerable.Repeat((byte)0x80, 2 * 2 * 4).ToArray();
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 2 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static async Task SeedAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9900801", BoothName = "作り物のアバター「アオイ」", Category = "3Dキャラクター", BaseName = WithImage },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = WithImage, IsManual = true }, new AvatarBaseGroup { Name = NoImage, IsManual = true }],
        });
        await app.Services.Avatars.SetBaseImageAsync(WithImage, TinyPng);
    }

    private static async Task<ItemViewModel> OpenItemAsync(TestApp app)
    {
        await SeedAsync(app);
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with
        {
            Local = item.Local with
            {
                AvatarBases =
                [
                    new AvatarBaseLink { BaseName = WithImage, Source = AvatarLinkSource.Manual },
                    new AvatarBaseLink { BaseName = NoImage, Source = AvatarLinkSource.Manual },
                ],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    [Fact]
    public Task 商品ページの素体の札は_付けた画像を乗せた時に出し_無い素体は出さない() => TestApp.Run(async app =>
    {
        var page = await OpenItemAsync(app);

        Assert.NotNull(page.AvatarBases.Single(row => row.Name == WithImage).Icon);
        Assert.Null(page.AvatarBases.Single(row => row.Name == NoImage).Icon);
    });

    [Fact]
    public Task 商品ページの対応アバターを追加する欄で_素体の候補に素体の絵を出す() => TestApp.Run(async app =>
    {
        // 付けた素体は札にあるので候補に出ない。付けていない商品で見る
        await SeedAsync(app);
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Contains($"{WithImage}（共通素体）", page.SupportSuggestions);
        Assert.NotNull(page.AvatarIconSelector($"{WithImage}（共通素体）"));
        Assert.Null(page.AvatarIconSelector($"{NoImage}（共通素体）"));
    });

    [Fact]
    public Task アバターの詳細の共通素体の欄で_候補に素体の絵を出す() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.BaseNames.Count == 2, "素体の候補が並ぶ");

        Assert.NotNull(avatars.BaseIconSelector(WithImage));
        Assert.Null(avatars.BaseIconSelector(NoImage));
    });

    /// <summary>候補を組んだ後に画像を差し替えても、絵を引く時の登録簿で新しい画像を指す（古いファイルは片付けられて絵が消える）</summary>
    [Fact]
    public Task 画像を差し替えると_名前から引く絵の場所も新しい画像になる() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        await app.StartAsync();
        var before = AvatarBaseIcons.PathOf(app.Services, WithImage, _ => null);
        Assert.NotNull(before);

        await app.Services.Avatars.SetBaseImageAsync(WithImage, OtherPng());

        var after = AvatarBaseIcons.PathOf(app.Services, WithImage, _ => null);
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
        Assert.True(File.Exists(after));
    });
}
