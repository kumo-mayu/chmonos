using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 画面の条件と、保存した値から Core で作った判定（<see cref="SearchModuleConditions.From"/>）が、同じ商品に当たるか
/// （2026-10-12・外から使う API は保存した値の形で条件を受けるので、画面と答えが分かれないことを確かめる。`docs/spec/local-api.md`）。
/// 条件の種類ごとに値をいくつか変え、除く・除かないの両方で、作り物の商品の全部を照らし比べる。
/// </summary>
public class SearchModuleConditionParityTests
{
    private static DateTimeOffset Jst2025(int month, int day) => new(2025, month, day, 12, 0, 0, TimeSpan.FromHours(9));

    private static IEnumerable<ItemRecord> Items()
    {
        var plain = Make.Item("9800001", "作り物の衣装", "shop-a");
        yield return plain with
        {
            Booth = plain.Booth with
            {
                Category = new BoothCategory { Id = 1, Name = "3D衣装", ParentName = "3Dモデル" },
                Tags = ["夏", "水着"],
                WishListsCount = 50,
                PublishedAt = Jst2025(3, 1),
                Variations = [new BoothVariation { Id = 1, Price = 0 }, new BoothVariation { Id = 2, Price = 1500 }],
            },
            Local = plain.Local with
            {
                Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 1500, PurchasedAt = new DateOnly(2025, 4, 1) }],
                Attributes = new Dictionary<string, int> { ["かわいい"] = 4 },
                Avatars = [new AvatarLink { AvatarItemId = "7000001", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                IsFavorite = true,
            },
        };

        var other = Make.Item("9800002", "作り物の小物", "shop-b");
        yield return other with
        {
            Booth = other.Booth with
            {
                Category = new BoothCategory { Id = 2, Name = "3Dアクセサリ", ParentName = "3Dモデル" },
                Tags = ["冬"],
                WishListsCount = 5000,
                PublishedAt = Jst2025(11, 20),
                Variations = [new BoothVariation { Id = 3, Price = 500 }, new BoothVariation { Id = 4, Price = 990000 }],
                IsAdult = true,
            },
            Local = other.Local with
            {
                Purchases =
                [
                    new Purchase { Kind = PurchaseKind.Received, Price = 0, PurchasedAt = new DateOnly(2024, 12, 24) },
                    new Purchase { Kind = PurchaseKind.ForSelf, Price = 500, PurchasedAt = new DateOnly(2025, 6, 1) },
                ],
                Attributes = new Dictionary<string, int> { ["かわいい"] = 1, ["かっこいい"] = 5 },
                Avatars = [new AvatarLink { AvatarItemId = "7000002", Source = AvatarLinkSource.Tag, Confirmed = false }],
                IsHidden = true,
            },
        };

        // 対応の指定が無い・購入記録が無い・日付が無い・自分で入れたカテゴリ・ファイルが見つからない
        var bare = Make.Item("9800003", "作り物の素材", "shop-a");
        yield return bare with
        {
            Booth = bare.Booth with { WishListsCount = 0, Variations = [new BoothVariation { Id = 5, Price = 0 }] },
            Local = bare.Local with
            {
                Category = "テクスチャ",
                LocalFiles = [Make.File(@"D:\files\9800003.zip") with { Paths = [], MissingSince = Jst2025(5, 1) }],
            },
        };

        // BOOTH の情報が無い（バリエーションも公開日も無い）・ファイルを持たない
        yield return Make.Item("9800004", "作り物の非公開商品").WithFiles() with
        {
            Local = new LocalBlock { AcquiredAt = new DateOnly(2025, 2, 2), IsDelisted = true },
        };

        // BOOTHに無い商品（仮のID）・壊れた zip
        yield return Make.Item(LocalItemId.For("0123456789abcdef"), "作り物の自作").WithFiles(Make.File(@"E:\own\pack.zip", archiveBroken: true));
    }

    /// <summary>種類ごとに試す値（状態の形）。積む値は作り物の商品にある物と無い物を混ぜる。</summary>
    private static IEnumerable<SearchModuleState> States(SearchModuleKind kind)
    {
        var name = kind.ToString();
        if (SearchModuleConditions.ChoiceOf(kind) is { } choice)
        {
            foreach (var key in choice.Keys)
            {
                yield return new SearchModuleState { Kind = name, Choice = key };
                if (choice.Flag is { } flag)
                {
                    yield return new SearchModuleState
                    {
                        Kind = name,
                        Choice = key,
                        Toggles = new Dictionary<string, bool> { [flag.StateKey] = !flag.Default },
                    };
                }
            }

            yield break;
        }

        IEnumerable<SearchModuleState> Lists(params string[][] sets)
        {
            foreach (var set in sets)
            {
                foreach (var matchAll in new[] { false, true })
                {
                    foreach (var exclude in new[] { false, true })
                    {
                        yield return new SearchModuleState { Kind = name, Items = set, MatchAll = matchAll, Exclude = exclude };
                    }
                }
            }
        }

        var states = kind switch
        {
            SearchModuleKind.Category => Lists(["3D衣装"], ["3Dモデル"], ["テクスチャ", "3Dアクセサリ"], ["無いカテゴリ"]),
            SearchModuleKind.BoothTag => Lists(["夏"], ["夏", "冬"], ["水着", "夏"]),
            SearchModuleKind.Shop => Lists(["shop-a"], ["shop-a", "shop-b"])
                .Concat([new SearchModuleState { Kind = name, IncludeFavorites = true }]),
            SearchModuleKind.Path => Lists([@"D:\files"], [@"E:\own", @"D:\files"]),
            SearchModuleKind.Avatar => Lists(["avatar:7000001"], ["avatar:7000001", "avatar:7000002"])
                .Concat(
                [
                    new SearchModuleState { Kind = name, Items = ["avatar:7000001"], ShowUnspecified = true },
                    new SearchModuleState { Kind = name, ShowMatched = false, ShowUnspecified = true },
                    new SearchModuleState { Kind = name, Items = ["avatar:7000002"], Flag = false, Exclude = true },
                ]),
            SearchModuleKind.WishList => Ranges(name, null),
            SearchModuleKind.Price => Ranges(name, SearchModuleConditions.PaidSource).Concat(Ranges(name, SearchModuleConditions.BoothSource)),
            SearchModuleKind.PublishedAt or SearchModuleKind.AcquiredAt => Dates(name),
            SearchModuleKind.Attribute =>
            [
                new() { Kind = name, Ranges = [new AttributeRange("かわいい", 3, 5)] },
                new() { Kind = name, Ranges = [new AttributeRange("かわいい", 3, 5)], Exclude = true },
                new() { Kind = name, Ranges = [new AttributeRange("かわいい", 0, 2), new AttributeRange("かっこいい", 4, 5)], MatchAll = true },
                new() { Kind = name, Ranges = [new AttributeRange("かわいい", 0, 2), new AttributeRange("かっこいい", 4, 5)], MatchAll = false, Exclude = true },
            ],
            SearchModuleKind.Unedited =>
            [
                new() { Kind = name, Choice = EditStatusCondition.MissingKey, Fields = ["userTags"] },
                new() { Kind = name, Choice = EditStatusCondition.MissingKey, Fields = ["attributes", "purchases"], MatchAll = true },
                new() { Kind = name, Choice = EditStatusCondition.FilledKey, Fields = ["attributes", "purchases"] },
            ],
            SearchModuleKind.Updated =>
            [
                new() { Kind = name, Choice = UpdateNoticeCondition.UpdatedKey },
                new() { Kind = name, Choice = UpdateNoticeCondition.OtherKey, Fields = ["price"] },
            ],
            SearchModuleKind.Recent =>
            [
                new() { Kind = name, Choice = "viewed", Min = "0", Max = "7" },
                new() { Kind = name, Choice = "added", Min = "3", Max = string.Empty, Exclude = true },
            ],
            _ => [],
        };

        foreach (var state in states)
        {
            yield return state;
        }
    }

    private static IEnumerable<SearchModuleState> Ranges(string kind, string? source)
    {
        foreach (var exclude in new[] { false, true })
        {
            yield return new SearchModuleState { Kind = kind, Choice = source, Min = "100", Max = "2000", Exclude = exclude };
            yield return new SearchModuleState { Kind = kind, Choice = source, Min = "100", Max = string.Empty, Exclude = exclude };
            yield return new SearchModuleState { Kind = kind, Choice = source, Min = "０", Max = "１,０００", MinEnabled = false, Exclude = exclude };
            yield return new SearchModuleState { Kind = kind, Choice = source, Min = "0", Max = "600", MatchAll = true, Flag = true, Exclude = exclude };
            yield return new SearchModuleState { Kind = kind, Choice = source, Min = "0", Max = "999999", IgnoreOutliers = false, Exclude = exclude };
        }
    }

    private static IEnumerable<SearchModuleState> Dates(string kind)
    {
        foreach (var exclude in new[] { false, true })
        {
            foreach (var scope in new[] { SearchModuleConditions.FirstPurchase, SearchModuleConditions.AllPurchases })
            {
                yield return new SearchModuleState { Kind = kind, Choice = scope, Min = "2025-01-01", Max = "2025-05-31", Exclude = exclude };
                yield return new SearchModuleState { Kind = kind, Choice = scope, Min = "2025-04-01", Max = string.Empty, Exclude = exclude };
                yield return new SearchModuleState { Kind = kind, Choice = scope, Min = string.Empty, Max = "2025-03-01", MinEnabled = false, Exclude = exclude };
            }
        }
    }

    public static TheoryData<SearchModuleKind> Kinds()
    {
        var data = new TheoryData<SearchModuleKind>();
        foreach (var kind in Enum.GetValues<SearchModuleKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task 画面の条件と_保存した値から作った判定は_同じ商品に当たる(SearchModuleKind kind) => TestApp.Run(async app =>
    {
        var items = Items().ToList();
        foreach (var item in items)
        {
            await app.AddItemAsync(item);
        }

        var search = (await app.StartAsync()).Search;
        var module = SearchModuleMenuTests.Add(search, kind);
        var context = search.CreateModuleContext();

        var states = States(kind).ToList();
        foreach (var state in states.Count == 0 ? [module.Save()] : states)
        {
            module.Load(state);
            module.Prepare(context);

            // 画面の条件が読んだ値（知らない値を捨てた後）を保存して、Core で作る
            var saved = module.Save();
            var condition = SearchModuleConditions.From(saved);
            Assert.NotNull(condition);
            condition.Prepare(context);

            if (module.IsEnabled && !module.HasDisabledReason)
            {
                Assert.Equal(module.IsActive, condition.IsActive);
            }

            foreach (var item in items)
            {
                var expected = module.Passes(item, context);
                var actual = module.IsExcluded ? condition.MatchesExcluded(item, context) : condition.Matches(item, context);
                Assert.True(expected == actual, $"{kind} {saved.Fingerprint} 除く={module.IsExcluded} {item.Id}: 画面={expected} Core={actual}");
            }
        }
    });

    [Fact]
    public Task 選ぶ条件の補助の切り替えの既定と名前は_画面とCoreで同じ() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;
        foreach (var kind in Enum.GetValues<SearchModuleKind>())
        {
            if (SearchModuleConditions.ChoiceOf(kind) is not { } spec)
            {
                continue;
            }

            var module = (ChoiceModule)SearchModuleMenuTests.Add(search, kind);
            Assert.Equal(spec.Keys, module.Options.Select(option => option.Key));

            // 足したときの補助の切り替えは Core の既定と同じ値で状態に書かれる
            var toggles = module.Save().Toggles;
            if (spec.Flag is { } flag)
            {
                Assert.Equal(flag.Default, toggles![flag.StateKey]);
            }
            else
            {
                Assert.Null(toggles);
            }
        }
    });
}
