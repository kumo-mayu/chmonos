using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 絵を出す候補の欄には全部、乗せると大きな絵を出す（ユーザ判断 2026-11-20「候補の欄の絵：アバターの管理の外の欄にも」）。
/// 部品の動き（開く前に読まない・届いたら知らせる）は <see cref="SuggestBoxHoverImageTests"/>。ここは各画面の VM が口を持ち、
/// 頭の絵と同じ場所の絵を返す事を見る（口を配り忘れると、束縛は黙って null になり吹き出しが出ない）。名前は作り物
/// </summary>
public sealed class SuggestHoverIconWiringTests
{
    private const string WithImage = "作り物の素体";
    private const string NoImage = "画像の無い素体";

    /// <summary>1×1 の PNG。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task SeedAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900811", "作り物の衣装"));
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9900801", BoothName = "作り物のアバター「アオイ」", AvatarOverride = true, Category = "3Dキャラクター", BaseName = WithImage },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = WithImage, IsManual = true }, new AvatarBaseGroup { Name = NoImage, IsManual = true }],
        });
        await app.Services.Avatars.SetBaseImageAsync(WithImage, TinyPng);
    }

    /// <summary>
    /// 絵のある候補は大きな絵が届き（手元に無ければ null を返して裏で読み、読み終えたら知らせる）、絵の無い候補は何も読まず null。
    /// 頭の絵（<paramref name="icon"/>）と同じ候補で絵の有無が揃う事も見る（場所の決め方が1つである事）
    /// </summary>
    private static async Task AssertHoverAsync(Func<string, ImageSource?> icon, Func<string, Action, ImageSource?>? hover, string withImage, string noImage)
    {
        Assert.NotNull(hover);
        Assert.NotNull(icon(withImage));
        Assert.Null(icon(noImage));

        var loaded = false;
        if (hover!(withImage, () => loaded = true) is null)
        {
            await UiThread.Until(() => loaded, "大きな絵が届いた知らせ");
            Assert.NotNull(hover(withImage, () => { }));
        }

        Assert.Null(hover(noImage, () => Assert.Fail("絵の無い候補で読みに行った")));
    }

    [Fact]
    public Task 検索の対応アバターとプロファイルの欄は_大きな絵の口を持ち_頭の絵と同じ絵を返す() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        var search = (await app.StartAsync()).Search;

        var avatar = (ListModule)SearchModules.Add(search, SearchModuleKind.Avatar);
        await AssertHoverAsync(avatar.IconSelector!, avatar.HoverIconSelector, $"{WithImage}（共通素体）", $"{NoImage}（共通素体）");

        var profile = (ProfileModule)SearchModules.Add(search, SearchModuleKind.Profile);
        Assert.NotNull(profile.Source.HoverIconSelector);
        Assert.NotNull(profile.Destination.HoverIconSelector);

        // 改変の条件：1段目（アバターと改変）。2段目（枠ごとの改変）も同じ口を持ち主から受け取る
        var modification = (ModificationModule)SearchModules.Add(search, SearchModuleKind.Modification);
        Assert.NotNull(modification.HoverIconSelector);
        Assert.Null(modification.HoverIconSelector!("存在しない候補", () => Assert.Fail("候補に無い語で読みに行った")));
    });

    [Fact]
    public Task 検索の絵の無い欄には_大きな絵の口を付けない() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        var search = (await app.StartAsync()).Search;

        var shop = (ListModule)SearchModules.Add(search, SearchModuleKind.Shop);
        Assert.Null(shop.IconSelector);
        Assert.Null(shop.HoverIconSelector);
    });

    [Fact]
    public Task 商品ページの対応アバターを追加する欄は_大きな絵の口を持ち_頭の絵と同じ絵を返す() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        var item = (await app.Store.Items.LoadAsync("9900811"))!;
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        await AssertHoverAsync(page.AvatarIconSelector, page.AvatarHoverIconSelector, $"{WithImage}（共通素体）", $"{NoImage}（共通素体）");
    });

    [Fact]
    public Task 改変を選ぶ窓のアバターの欄は_大きな絵の口を持つ() => TestApp.Run(async app =>
    {
        await SeedAsync(app);
        await app.StartAsync();

        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [], "既存", "決定", "空");

        Assert.NotNull(model.AvatarIconSelector);
        Assert.NotNull(model.AvatarHoverIconSelector);
        // 絵を取っていないアバターは頭の字。大きな絵も読まない
        Assert.Null(model.AvatarIconSelector!("作り物のアバター「アオイ」"));
        Assert.Null(model.AvatarHoverIconSelector!("作り物のアバター「アオイ」", () => Assert.Fail("絵の無い候補で読みに行った")));
    });
}
