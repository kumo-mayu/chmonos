using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 統計のアバターの2つの欄（アバター別の所持数・所有アバターに着られるもの）が、持っていないバリエーションの分を外すか（open.md 106）。
/// 検索の「持っているバリエーションの分だけ」と同じ式（<see cref="AvatarCompatibilityIndex.UnownedVariationAvatars"/>）と設定に従う。
/// 登録簿は <see cref="AvatarCompatibilityTests.Registry"/>（くうた・まめふれんずの2体）。
/// </summary>
public class StatsVariationTests
{
    private const long Kuuta = 1;
    private const long Mame = 2;

    private static LocalFileRecord File() => new() { Hash = Guid.NewGuid().ToString("N"), Paths = ["x"], SizeBytes = 1 };

    /// <summary>くうた用だけを買った、くうた用・まめふれんず用の2つのバリエーションの衣装。向き先は検出（または人）で付ける。</summary>
    private static ItemRecord Outfit(string id, bool manual = false)
    {
        IReadOnlyList<VariationTarget> targets =
        [
            new VariationTarget { VariationId = Kuuta, Avatars = ["kuuta"] },
            new VariationTarget { VariationId = Mame, Avatars = ["kipfel"] },
        ];

        return new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = AvatarCompatibilityTests.Declaring("kuuta", "kipfel") with
            {
                LocalFiles = [File()],
                Purchases = [new Purchase { VariationId = Kuuta, Price = 500 }],
                VariationTargets = manual ? targets : [],
                DetectedVariationTargets = manual ? [] : targets,
            },
        };
    }

    /// <summary>アバターそのものの商品を持っている（所有アバターに数えるため）。</summary>
    private static ItemRecord AvatarItem(string id) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = id, FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock { LocalFiles = [File()] },
    };

    private static StatsSnapshot Build(bool useDetected, params ItemRecord[] items)
        => StatsService.Build(items, Registry(), unresolvedCount: 0, useDetected);

    /// <summary>登録簿の全員をアバターとして扱う（カテゴリを持たないので、着られるものの欄に出すには印が要る）。</summary>
    private static AvatarRegistry Registry()
    {
        var registry = AvatarCompatibilityTests.Registry();
        return new AvatarRegistry { Entries = [.. registry.Entries.Select(entry => entry with { AvatarOverride = true })], BaseGroups = registry.BaseGroups };
    }

    [Fact]
    public void 持っていないバリエーションのアバターは所持数に数えない()
    {
        var snapshot = Build(true, Outfit("o1"), AvatarItem("kuuta"), AvatarItem("kipfel"));

        Assert.Contains(snapshot.Avatars, bar => bar.Key == "kuuta" && bar.ItemCount == 1);
        // まめふれんず用は持っていないので、キプフェルにも仲間のまめひなたにも数えない
        Assert.DoesNotContain(snapshot.Avatars, bar => bar.Key is "kipfel" or "mamehinata");

        var kipfel = Assert.Single(snapshot.Wearables, wearable => wearable.AvatarItemId == "kipfel");
        Assert.Equal(0, kipfel.Total);
        var kuuta = Assert.Single(snapshot.Wearables, wearable => wearable.AvatarItemId == "kuuta");
        Assert.Equal(1, kuuta.Total);
    }

    /// <summary>設定で検出した向き先を使わないなら、検索と同じく見分けない（両方に数える）</summary>
    [Fact]
    public void 検出した向き先を使わない設定なら_検出だけの衣装は両方に数える()
    {
        var snapshot = Build(false, Outfit("o1"), AvatarItem("kuuta"), AvatarItem("kipfel"));

        Assert.Contains(snapshot.Avatars, bar => bar.Key == "kipfel" && bar.ItemCount == 1);
        Assert.Contains(snapshot.Avatars, bar => bar.Key == "kuuta" && bar.ItemCount == 1);
    }

    [Fact]
    public void 人が決めた向き先は設定に関わらず使う()
    {
        var snapshot = Build(false, Outfit("o1", manual: true), AvatarItem("kuuta"), AvatarItem("kipfel"));

        Assert.DoesNotContain(snapshot.Avatars, bar => bar.Key == "kipfel");
    }

    /// <summary>引いて1体も残らない商品は「対応アバター未設定」ではない（対応は入れてあり、持っている分が無いだけ）</summary>
    [Fact]
    public void 持っている分が無い商品は未設定に数えない()
    {
        var onlyMame = Outfit("o1") with
        {
            Local = Outfit("o1").Local with
            {
                Avatars = [new AvatarLink { AvatarItemId = "kipfel", Source = AvatarLinkSource.SupportSection }],
                DetectedVariationTargets =
                [
                    new VariationTarget { VariationId = Kuuta, Avatars = ["kuuta"] },
                    new VariationTarget { VariationId = Mame, Avatars = ["kipfel"] },
                ],
            },
        };
        var linked = AvatarItem("a2") with { Local = AvatarItem("a2").Local with { Avatars = [new AvatarLink { AvatarItemId = "kuuta", Source = AvatarLinkSource.Manual }] } };

        var snapshot = Build(true, onlyMame, linked);

        // 未設定は「対応がまだ入っていない」商品。どちらの商品にも対応は入っている
        Assert.DoesNotContain(snapshot.Avatars, bar => bar.IsResidual);
    }
}
