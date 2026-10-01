using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 絞り込みの1回（<see cref="SearchFilterPass"/>・照らす重さの案b）と、照らす前の準備（案c）。
///
/// 案b は選択肢の件数を「外れた条件を2つ目まで数える」1回の走査で数える。数える相手が、前の作り
/// （条件ごとに、その条件を除いた他の全部で全商品を照らし直す）と同じになることを、作り物の商品で突き合わせる。
/// </summary>
public class SearchFilterPassTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)));

    /// <summary>タグ・スキ数・お気に入り・入手日を散らした作り物（seed 固定）。</summary>
    private static List<ItemRecord> Items(int count)
    {
        var random = new Random(20261001);
        var tags = new[] { "A", "B", "C", "D" };
        return Enumerable.Range(0, count).Select(index =>
        {
            var item = Make.Item((1000000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture), "作り物");
            return item with
            {
                Booth = item.Booth with
                {
                    Tags = tags.Where(_ => random.Next(3) == 0).ToArray(),
                    WishListsCount = random.Next(0, 400),
                },
                Local = item.Local with
                {
                    IsFavorite = random.Next(4) == 0,
                    AcquiredAt = random.Next(5) == 0 ? null : new DateOnly(2025, 1, 1).AddDays(random.Next(600)),
                },
            };
        }).ToList();
    }

    private static ListModule Tags(bool matchAll, bool exclude, params string[] keys)
    {
        var module = new ListModule(SearchModuleKind.BoothTag, allowsAnd: true, "BOOTHタグで絞り込む", "なし",
            (item, _, key, _) => item.Booth.Tags.Contains(key));
        module.Load(new SearchModuleState { Kind = "BoothTag", Items = keys, MatchAll = matchAll, Exclude = exclude });
        return module;
    }

    private static List<SearchModule> Modules()
    {
        var favorite = new ChoiceModule(SearchModuleKind.Favorite,
            [new ChoiceOption("favorite", "お気に入りのみ"), new ChoiceOption("other", "お気に入り以外のみ"), new ChoiceOption("both", "両方")], "both",
            (item, key, _) => key switch { "favorite" => item.Local.IsFavorite, "other" => !item.Local.IsFavorite, _ => true });
        favorite.Load(new SearchModuleState { Kind = "Favorite", Choice = "other" });

        var unused = new ChoiceModule(SearchModuleKind.Owned,
            [new ChoiceOption("owned", "所持している"), new ChoiceOption("both", "両方")], "both", (_, _, _) => true);
        unused.Load(new SearchModuleState { Kind = "Owned", Choice = "both" });

        var likes = new RangeModule(SearchModuleKind.WishList, (item, _) => [item.Booth.WishListsCount], string.Empty);
        likes.Load(new SearchModuleState { Kind = "WishList", Min = "50", Max = "350" });

        var acquired = new DateModule(SearchModuleKind.AcquiredAt, item => item.Local.AcquiredAt);
        acquired.Load(new SearchModuleState { Kind = "AcquiredAt", Min = "2025-03-01", Max = "2026-03-01", Exclude = true });

        return [Tags(false, false, "A", "B"), Tags(true, true, "C", "D"), favorite, unused, likes, acquired];
    }

    /// <summary>前の作りの数え方：その条件を除いた他の全部で照らし直す。</summary>
    private static List<string> Naive(IEnumerable<ItemRecord> items, IReadOnlyList<SearchModule> modules, SearchModule? except, Func<ItemRecord, bool> first)
        => items.Where(item => first(item)
                && modules.Where(module => module.IsActive && !ReferenceEquals(module, except)).All(module => module.Passes(item, Context)))
            .Select(item => item.Id)
            .ToList();

    [Fact]
    public Task 結果と各条件の件数の相手が_条件ごとに照らし直したときと同じ() => UiThread.Run(() =>
    {
        var items = Items(600);
        var modules = Modules();
        Func<ItemRecord, bool> first = item => item.Id[^1] != '7';

        var pass = SearchFilterPass.Run(items, modules, first, Context);

        Assert.Equal(Naive(items, modules, null, first), pass.Matches.Select(item => item.Id));
        Assert.NotEmpty(pass.Matches);
        foreach (var module in modules)
        {
            Assert.Equal(
                Naive(items, modules, module, first).Order(StringComparer.Ordinal),
                pass.OthersFor(module).Select(item => item.Id).Order(StringComparer.Ordinal));
        }
    });

    [Fact]
    public Task 選択肢の件数が_条件ごとに照らし直して数えたときと同じ() => UiThread.Run(() =>
    {
        var items = Items(600);
        var modules = Modules();
        var pass = SearchFilterPass.Run(items, modules, _ => true, Context);
        foreach (var module in modules)
        {
            module.RefreshCounts(pass.OthersFor(module), Context);
        }

        var tags = (ListModule)modules[0];
        var favorite = (ChoiceModule)modules[2];
        var expectedChips = tags.Chips.Select(chip =>
            Naive(items, modules, tags, _ => true).Count(id => items.First(item => item.Id == id).Booth.Tags.Contains(chip.Key)));
        var expectedOptions = favorite.Options.Select(option =>
            Naive(items, modules, favorite, _ => true).Count(id =>
            {
                var item = items.First(entry => entry.Id == id);
                return option.Key switch { "favorite" => item.Local.IsFavorite, "other" => !item.Local.IsFavorite, _ => true };
            }));

        Assert.Equal(expectedChips, tags.Chips.Select(chip => chip.Count));
        Assert.Equal(expectedOptions, favorite.Options.Select(option => option.Count));
    });

    [Fact]
    public Task 条件が無ければ_先に照らす物だけで絞る() => UiThread.Run(() =>
    {
        var items = Items(50);
        var pass = SearchFilterPass.Run(items, [], item => item.Local.IsFavorite, Context);

        Assert.Equal(items.Where(item => item.Local.IsFavorite).Select(item => item.Id), pass.Matches.Select(item => item.Id));
    });

    // ---- 案c：用意した物が値の変更で古くならない ----

    [Fact]
    public Task 範囲の値を変えると_用意し直して新しい値で照らす() => UiThread.Run(() =>
    {
        var module = new RangeModule(SearchModuleKind.WishList, (item, _) => [item.Booth.WishListsCount], string.Empty);
        module.Load(new SearchModuleState { Kind = "WishList", Min = "100", Max = "200" });
        var item = Make.Item("1", "作り物") with { Booth = Make.Item("1", "作り物").Booth with { WishListsCount = 150 } };
        module.Prepare(Context);
        Assert.True(module.Passes(item, Context));

        module.MinText = "160";

        Assert.False(module.Passes(item, Context));
    });

    [Fact]
    public Task 日付の値を変えると_用意し直して新しい値で照らす() => UiThread.Run(() =>
    {
        var module = new DateModule(SearchModuleKind.AcquiredAt, entry => entry.Local.AcquiredAt);
        module.Load(new SearchModuleState { Kind = "AcquiredAt", Min = "2026-01-01", Max = "2026-12-31" });
        var item = Make.Item("1", "作り物") with { Local = Make.Item("1", "作り物").Local with { AcquiredAt = new DateOnly(2026, 2, 1) } };
        module.Prepare(Context);
        Assert.True(module.Passes(item, Context));

        module.SinceText = "2026-03-01";

        Assert.False(module.Passes(item, Context));
    });

    [Fact]
    public Task ユーザータグの枠を足すと_用意し直して新しい枠で照らす() => UiThread.Run(() =>
    {
        var module = new UserTagModule();
        module.AddTop("衣装", notify: false);
        var item = Make.Item("1", "作り物") with { Local = Make.Item("1", "作り物").Local with { UserTags = [new UserTagAssignment { Top = "小物" }] } };
        module.Prepare(Context);
        Assert.False(module.Passes(item, Context));

        module.AddTop("小物", notify: false);

        Assert.True(module.Passes(item, Context));
    });

    [Fact]
    public void ファイルの場所は_畳んだ形で比べても前と同じに判定する()
    {
        var item = Make.Item("1", "作り物").WithFiles(
            Make.File(@"D:\BOOTH\3Dモデル\衣装\a.zip"),
            Make.File(@"E:\外した\b.zip", detached: true));
        item = item with { Local = item.Local with { LocalFolders = [new LocalFolderRecord { Path = "F:/展開/髪" }] } };

        foreach (var folder in new[] { @"D:\BOOTH", @"d:\booth\3dモデル\", @"D:\BOOTH\3D", @"E:\外した", @"F:\展開", @"F:\展開\髪" })
        {
            Assert.Equal(
                Core.Services.FolderTree.IsUnderPrefix(item, Core.Services.FolderTree.UnderPrefix(folder)),
                folder is @"D:\BOOTH" or @"d:\booth\3dモデル\" or @"F:\展開");
        }
    }
}
