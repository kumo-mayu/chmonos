using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの管理の画面の小さな絵に乗せると、大きな絵を出す（ユーザ判断 2026-11-20・①）。
/// 絵の無い（頭の字の）時は吹き出しを出さない。大きな絵は吹き出しが開いた時に初めて読む
/// （行を作る・流す時に読むと、③で軽くした右の欄・一覧が重くなる）。名前は作り物
/// </summary>
public sealed class AvatarsHoverImageTests
{
    /// <summary>1×1 の PNG。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static AvatarRowViewModel AvatarRow(ThumbnailLoader loader, string? path) => new()
    {
        Summary = new AvatarSummary
        {
            Entry = new AvatarRegistryEntry { ItemId = "9900951", BoothName = "作り物のアバター" },
            Name = "作り物のアバター",
            IsAvatar = true,
            IsOwned = true,
            DirectCount = 0,
            ViaBaseCount = 0,
        },
        Thumbnails = loader,
        IconPathFactory = () => path,
    };

    private static AvatarBaseRowViewModel BaseRow(ThumbnailLoader loader, string? path) => new()
    {
        Summary = new AvatarBaseSummary
        {
            Group = new AvatarBaseGroup { Name = "作り物の素体", IsManual = true },
            MemberCount = 0,
            OwnedMemberCount = 0,
            ItemCount = 0,
        },
        Thumbnails = loader,
        IconPathFactory = () => path,
    };

    [Fact]
    public Task 絵が無ければ_どの絵も吹き出しを出さない() => UiThread.Run(() =>
    {
        var loader = new ThumbnailLoader();
        var end = new ConversionEndIcon("作り物の素体", static () => null, loader);
        var avatar = AvatarRow(loader, null);
        var baseRow = BaseRow(loader, null);

        Assert.False(end.HasHoverImage);
        Assert.Null(end.HoverImage);
        Assert.False(avatar.HasHoverImage);
        Assert.Null(avatar.HoverImage);
        Assert.False(baseRow.HasHoverImage);
        Assert.Null(baseRow.HoverImage);
    });

    [Fact]
    public Task 組の端の絵は_小さな絵を読んでも大きな絵を読まず_乗せた時に読む() => UiThread.Run(async () =>
    {
        var file = WriteImage();
        var loader = new ThumbnailLoader();
        var end = new ConversionEndIcon("作り物の素体", () => file, loader);
        await AssertReadsCardOnlyOnHover(loader, end.HasHoverImage, () => end.Icon, () => end.HoverImage);
    });

    [Fact]
    public Task アバターの行の絵は_小さな絵を読んでも大きな絵を読まず_乗せた時に読む() => UiThread.Run(async () =>
    {
        var file = WriteImage();
        var loader = new ThumbnailLoader();
        var row = AvatarRow(loader, file);
        await AssertReadsCardOnlyOnHover(loader, row.HasHoverImage, () => row.Icon, () => row.HoverImage);
    });

    [Fact]
    public Task 素体の行の絵は_小さな絵を読んでも大きな絵を読まず_乗せた時に読む() => UiThread.Run(async () =>
    {
        var file = WriteImage();
        var loader = new ThumbnailLoader();
        var row = BaseRow(loader, file);
        await AssertReadsCardOnlyOnHover(loader, row.HasHoverImage, () => row.Icon, () => row.HoverImage);
    });

    /// <summary>
    /// 行が描く小さな絵（タイル）を読み終えても、控えは1枚（カードの大きさを読んでいない）。乗せて HoverImage を引いた時に初めて2枚目を読む
    /// </summary>
    private static async Task AssertReadsCardOnlyOnHover(ThumbnailLoader loader, bool hasHoverImage, Func<object?> icon, Func<object?> hover)
    {
        // 控えの枚数で見分けるので、タイルとカードの大きさが違う事が前提（同じなら1枚を分け合う）
        Assert.NotEqual(ThumbnailLoader.TileEdgeDip, ThumbnailLoader.CardEdgeDip);
        Assert.True(hasHoverImage);

        _ = icon();
        await UiThread.Until(() => icon() is not null, "小さな絵が届く");
        await UiThread.Settle();
        Assert.Equal(1, loader.CachedImageCount);

        _ = hover();
        await UiThread.Until(() => hover() is not null, "乗せた時の大きな絵が届く");
        Assert.Equal(2, loader.CachedImageCount);
    }

    private static string WriteImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chmonos-hover-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "a.png");
        File.WriteAllBytes(file, TinyPng);
        return file;
    }

    /// <summary>
    /// 画面のどこに吹き出しを付けたか。小さな絵の升（アバター・素体・組の端・変換の商品の行・アバターと素体の詳細の頭）は全部付け、
    /// 絵の無い時に止める。既に大きな絵の所（カードの見方・変換の右の 200 の絵）には付けない
    /// </summary>
    [Fact]
    public void 小さな絵の升には吹き出しを付け_絵の無い時は止め_大きな絵には付けない()
    {
        var xaml = File.ReadAllText(ViewPath("AvatarsView.xaml"));
        var tips = Regex.Matches(xaml, @"ToolTip=""\{DynamicResource HoverImageTip\}""\s+ToolTipService\.IsEnabled=""\{Binding HasHoverImage, FallbackValue=False\}""");

        // 組の端の型（組の一覧・見出し・右の組の欄で共有）・アバターの行・素体の行・変換の商品の行・アバターの詳細の頭・素体の詳細の絵
        Assert.Equal(6, Regex.Matches(xaml, @"\{DynamicResource HoverImageTip\}").Count);
        Assert.Equal(6, tips.Count);

        var conversionImage = Regex.Match(xaml, @"<Border Width=""200"" Height=""200""[^>]*>").Value;
        Assert.NotEmpty(conversionImage);
        Assert.DoesNotContain("HoverImageTip", conversionImage);

        // 型はアプリ全体の資源に1つ（候補の欄を取り込みの無い窓にも置くため。ユーザ判断 2026-11-20「候補の欄の絵：アバターの管理の外の欄にも」）
        var resources = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ViewPath("App.xaml"))!, "..", "App.xaml"));
        var tip = Regex.Match(resources, @"<ToolTip x:Key=""HoverImageTip""[^>]*>").Value;
        Assert.Contains(@"x:Shared=""False""", tip);
    }

    private static string ViewPath(string name, [CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views", name);
}
