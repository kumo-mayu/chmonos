// 検索の絞り込みの重さを、作り物の商品で測る（docs/research/search-modules-2026-10-01.md「照らす重さと順番」）。
//
// 使い方（Release で測る。Debug だと数倍ぶれる）:
//   dotnet run -c Release --project experiments/SearchFilterBench -- kinds  [件数]   条件の種類ごとの、1件あたりの重さと当たる割合
//   dotnet run -c Release --project experiments/SearchFilterBench -- order  [件数]   条件3・6・10個で、照らす順を変えたときの絞り込み1回
//   dotnet run -c Release --project experiments/SearchFilterBench -- counts [件数]   選択肢の件数（RefreshFacetCounts）の重さと、条件の数での伸び・印（ビット）で数える案
//   dotnet run -c Release --project experiments/SearchFilterBench -- cold   [件数]   新しいプロセスでの最初の1回（JIT 込み）と2回目以降
//
// 条件の部品はアプリの物をそのまま使う：照らす中身（SearchViewModel.CreateModule の中の式）は、
// 初期化しない SearchViewModel から非公開の CreateModule を呼んで取り出す。絞り込みと件数の回し方
// （SearchViewModel.Filtering.cs の Matches・RefreshFacetCounts）は同じ形をここに写した。
// アプリの一式・保存先・BOOTH には触れない（念のため CHMONOS_HOME を一時フォルダへ向ける）。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace SearchFilterBench;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Environment.SetEnvironmentVariable("CHMONOS_HOME", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chmonos-searchbench-" + Environment.ProcessId));

        var mode = args.Length > 0 ? args[0] : "kinds";
        var sizes = args.Length > 1 ? [int.Parse(args[1])] : new[] { 2000, 10000 };

        if (mode is not ("kinds" or "order" or "counts" or "cold"))
        {
            Console.WriteLine("kinds / order / counts / cold");
            return 1;
        }

        foreach (var size in sizes)
        {
            var world = World.Make(size, seed: 20261001);
            if (mode != "cold")
            {
                // 動き続けているアプリと同じ「最適化し終えた後」を測る。新しいプロセスの最初は、関数が軽い翻訳のまま
                // 走って数倍重く出る（段の切り替えは裏で進む）。一度空回しして待ってから測る。最初の1回は cold で別に測る
                var output = Console.Out;
                Console.SetOut(System.IO.TextWriter.Null);
                Run(mode, world);
                Console.SetOut(output);
                Thread.Sleep(1000);
            }

            Console.WriteLine($"# {mode}  商品 {size} 件（作り物・seed 20261001）");
            Run(mode, world);
            Console.WriteLine();
        }

        return 0;
    }

    private static void Run(string mode, World world)
    {
        switch (mode)
        {
            case "kinds": Bench.Kinds(world); break;
            case "order": Bench.Order(world); break;
            case "counts": Bench.Counts(world); break;
            default: Bench.Cold(world); break;
        }
    }
}

/// <summary>作り物の商品と、照らす材料。</summary>
internal sealed class World
{
    public required List<ItemRecord> Items { get; init; }

    public required AvatarRegistry Registry { get; init; }

    public required RecentTimes Recent { get; init; }

    public required Dictionary<string, SearchHaystack> Haystacks { get; init; }

    public required SearchViewModel Vm { get; init; }

    private static readonly MethodInfo CreateModuleMethod =
        typeof(SearchViewModel).GetMethod("CreateModule", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>アプリと同じ照らし方の条件を作り、状態を入れる。</summary>
    public SearchModule Module(SearchModuleKind kind, SearchModuleState state)
    {
        var module = (SearchModule)CreateModuleMethod.Invoke(Vm, [kind])!;
        module.Load(state with { Kind = kind.ToString() });
        return module;
    }

    /// <param name="freshIndex">対応アバターの展開を毎回作り直す（アバターの登録を変えた直後の姿）。普段は画面が持ち回して答えを覚えている。</param>
    public SearchModuleContext Context(bool freshIndex = false)
    {
        var shared = freshIndex ? null : _index ??= AvatarCompatibilityIndex.Build(Registry);
        return new SearchModuleContext(() => shared ?? AvatarCompatibilityIndex.Build(Registry), ModificationUsage.Empty, Recent, null, Now);
    }

    private AvatarCompatibilityIndex? _index;

    public static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));

    // BOOTH の公開のカテゴリ名（作り物の分布に使うだけ）
    private static readonly (string Parent, string Child, double Weight)[] Categories =
    [
        ("3Dモデル", "3D衣装", 34), ("3Dモデル", "3D装飾品", 12), ("3Dモデル", "3Dキャラクター", 6), ("3Dモデル", "3Dテクスチャ", 6),
        ("3Dモデル", "3Dツール・システム", 6), ("3Dモデル", "3Dモーション・アニメーション", 3), ("3Dモデル", "3D小道具", 5),
        ("3Dモデル", "3D環境・ワールド", 3), ("3Dモデル", "3Dモデル（その他）", 4), ("素材データ", "イラスト素材", 5),
        ("素材データ", "音素材", 2), ("ソフトウェア", "ソフトウェア・ハードウェア", 3), ("イラスト", "イラスト集", 4), ("音楽", "音楽", 2),
    ];

    private static readonly string[] Words =
    [
        "ドレス", "パーカー", "スカート", "ブーツ", "髪型", "アクセサリー", "メイド", "制服", "水着", "コート", "シェーダー", "表情", "テクスチャ",
        "対応", "改変", "VRChat", "Unity", "PhysBone", "MA", "セットアップ", "着せ替え", "かわいい", "クール", "ゆったり", "リボン", "フリル",
    ];

    public static World Make(int count, int seed)
    {
        var rng = new Random(seed);
        int Skewed(int n, double power = 2.2) => Math.Min(n - 1, (int)(Math.Pow(rng.NextDouble(), power) * n));
        string Phrase(int words) => string.Join(" ", Enumerable.Range(0, words).Select(_ => Words[Skewed(Words.Length, 1.3)]));

        // アバター 150 体・共通素体 10（各5体ずつ属する）
        var avatarIds = Enumerable.Range(0, 150).Select(index => (9_000_000 + index).ToString()).ToList();
        var registry = new AvatarRegistry
        {
            Entries = avatarIds.Select((id, index) => new AvatarRegistryEntry
            {
                ItemId = id,
                DisplayName = $"アバター{index}",
                Category = "3Dキャラクター",
                BaseName = index < 50 ? $"素体{index / 5}" : null,
            }).ToList(),
            BaseGroups = Enumerable.Range(0, 10).Select(index => new AvatarBaseGroup { Name = $"素体{index}" }).ToList(),
        };

        var weightTotal = Categories.Sum(entry => entry.Weight);
        var items = new List<ItemRecord>(count);
        var added = new Dictionary<string, DateTimeOffset>();
        var viewed = new Dictionary<string, DateTimeOffset>();
        var used = new Dictionary<string, DateTimeOffset>();

        for (var index = 0; index < count; index++)
        {
            var id = (1_000_000 + index * 7).ToString();
            var pick = rng.NextDouble() * weightTotal;
            var category = Categories[^1];
            foreach (var entry in Categories)
            {
                if ((pick -= entry.Weight) <= 0)
                {
                    category = entry;
                    break;
                }
            }

            var shop = $"shop{Skewed(400)}";
            var free = rng.NextDouble() < 0.2;
            var variations = Enumerable.Range(0, 1 + Skewed(4, 1.6)).Select(v => new BoothVariation
            {
                Id = index * 10L + v,
                Name = $"種類{v}",
                Price = free ? 0 : (int)Math.Round(Math.Exp(rng.NextDouble() * 4.4 + 5.8) / 100) * 100,
            }).ToList();
            if (rng.NextDouble() < 0.02)
            {
                variations.Add(new BoothVariation { Id = index * 10L + 9, Name = "支援", Price = 99_999 });
            }

            var hasAvatars = category.Child is "3D衣装" or "3D装飾品" or "3Dテクスチャ" ? rng.NextDouble() < 0.8 : rng.NextDouble() < 0.2;
            var links = hasAvatars
                ? Enumerable.Range(0, 1 + Skewed(30, 2.5)).Select(_ => avatarIds[Skewed(avatarIds.Count, 2.0)]).Distinct()
                    .Select(avatar => new AvatarLink { AvatarItemId = avatar, Source = rng.NextDouble() < 0.1 ? AvatarLinkSource.H2Link : AvatarLinkSource.SupportSection })
                    .ToList()
                : [];
            var bases = rng.NextDouble() < 0.1 ? [new AvatarBaseLink { BaseName = $"素体{Skewed(10)}" }] : new List<AvatarBaseLink>();

            var tags = rng.NextDouble() < 0.65
                ? Enumerable.Range(0, 1 + Skewed(3, 1.5)).Select(_ => Skewed(12, 1.6)).Distinct().Select(top => new UserTagAssignment
                {
                    Top = $"大分類{top}",
                    Subs = Enumerable.Range(0, Skewed(3, 1.5)).Select(_ => $"小{Skewed(8)}").Distinct().ToList(),
                }).ToList()
                : [];

            var attributes = rng.NextDouble() < 0.3
                ? Enumerable.Range(0, 1 + Skewed(3)).Select(_ => $"属性{Skewed(6)}").Distinct().ToDictionary(name => name, _ => rng.Next(0, 101))
                : new Dictionary<string, int>();

            var owned = rng.NextDouble() < 0.8;
            var files = owned
                ? Enumerable.Range(0, 1 + Skewed(3)).Select(file => new LocalFileRecord
                {
                    Hash = $"{index:X8}{file:X2}",
                    Paths = [$@"D:\BOOTH\{category.Parent}\{category.Child}\{shop}\{id}\file{file}.zip"],
                    SizeBytes = 1_000_000,
                    ArchiveBroken = rng.NextDouble() < 0.01,
                }).ToList()
                : [];

            var purchases = rng.NextDouble() < 0.75
                ? [new Purchase { VariationId = variations[0].Id, Price = variations[0].Price, NameSnapshot = variations[0].Name }]
                : new List<Purchase>();

            var name = $"商品{index} {Phrase(3)}";
            items.Add(new ItemRecord
            {
                Id = id,
                Booth = new BoothBlock
                {
                    Name = name,
                    Description = string.Join("。", Enumerable.Range(0, 20).Select(_ => Phrase(6))),
                    IsAdult = rng.NextDouble() < 0.1,
                    IsEndOfSale = rng.NextDouble() < 0.07,
                    PublishedAt = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)).AddDays(rng.Next(0, 3200)),
                    WishListsCount = (int)Math.Exp(rng.NextDouble() * 9),
                    Tags = Enumerable.Range(0, 2 + Skewed(14, 1.4)).Select(_ => $"タグ{Skewed(500, 2.6)}").Distinct().ToList(),
                    Category = new BoothCategory { Id = 1, Name = category.Child, ParentName = category.Parent },
                    Shop = new BoothShop { Name = $"ショップ{shop}", Subdomain = shop },
                    Variations = variations,
                },
                Local = new LocalBlock
                {
                    Avatars = links,
                    AvatarBases = bases,
                    UserTags = tags,
                    Attributes = attributes,
                    Purchases = purchases,
                    LocalFiles = files,
                    AcquiredAt = rng.NextDouble() < 0.4 ? new DateOnly(2021, 1, 1).AddDays(rng.Next(0, 2000)) : null,
                    IsFavorite = rng.NextDouble() < 0.08,
                    IsHidden = rng.NextDouble() < 0.02,
                    Memo = rng.NextDouble() < 0.1 ? Phrase(4) : null,
                },
            });

            added[id] = Now.AddDays(-rng.Next(0, 900));
            if (rng.NextDouble() < 0.3)
            {
                viewed[id] = Now.AddDays(-rng.Next(0, 200));
            }

            if (rng.NextDouble() < 0.1)
            {
                used[id] = Now.AddDays(-rng.Next(0, 200));
            }
        }

        var vm = (SearchViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SearchViewModel));
        Set(vm, "_allItems", items);
        Set(vm, "_favoriteShops", (IReadOnlySet<string>)new HashSet<string>(["shop1", "shop2", "shop3"], StringComparer.OrdinalIgnoreCase));

        return new World
        {
            Items = items,
            Registry = registry,
            Recent = new RecentTimes(added, used, viewed),
            Haystacks = items.ToDictionary(item => item.Id, item => SearchText.Build(item)),
            Vm = vm,
        };
    }

    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}

/// <summary>測る条件の見本。よくある値を入れる。</summary>
internal static class Samples
{
    public static readonly (string Name, SearchModuleKind Kind, SearchModuleState State)[] All =
    [
        ("所持：所持している", SearchModuleKind.Owned, S() with { Choice = "owned" }),
        ("お気に入り：お気に入りのみ", SearchModuleKind.Favorite, S() with { Choice = "favorite" }),
        ("編集状況：未入力のみ（ユーザータグ）", SearchModuleKind.Unedited, S() with { Choice = "unedited" }),
        ("R-18：R-18以外のみ", SearchModuleKind.Adult, S() with { Choice = "general" }),
        ("販売終了：販売終了のみ", SearchModuleKind.EndOfSale, S() with { Choice = "ended" }),
        ("有料・無料：有料のみ", SearchModuleKind.FreePaid, S() with { Choice = "paid" }),
        ("壊れたzip：ある", SearchModuleKind.BrokenZip, S() with { Choice = "broken" }),
        ("カテゴリ：子1つ（3D衣装）", SearchModuleKind.Category, S() with { Items = ["3D衣装"] }),
        ("カテゴリ：親1つ（3Dモデル）", SearchModuleKind.Category, S() with { Items = ["3Dモデル"] }),
        ("BOOTHタグ：1つ", SearchModuleKind.BoothTag, S() with { Items = ["タグ1"] }),
        ("BOOTHタグ：3つ AND", SearchModuleKind.BoothTag, S() with { Items = ["タグ1", "タグ2", "タグ3"], MatchAll = true }),
        ("ショップ：1つ", SearchModuleKind.Shop, S() with { Items = ["shop5"] }),
        ("対応アバター：1体（素体経由あり）", SearchModuleKind.Avatar, S() with { Items = ["avatar:9000003"], Flag = true }),
        ("対応アバター：共通素体1つ", SearchModuleKind.Avatar, S() with { Items = ["base:素体1"], Flag = true }),
        ("対応アバター：指定が無いのみ", SearchModuleKind.Avatar, S() with { ShowMatched = false, ShowUnspecified = true, Flag = true }),
        ("ファイルの場所：1つ（D:\\BOOTH\\3Dモデル）", SearchModuleKind.Path, S() with { Items = [@"D:\BOOTH\3Dモデル"] }),
        ("ユーザータグ：大分類1つ", SearchModuleKind.UserTag, S() with { UserTags = [new UserTagCondition { Top = "大分類1" }] }),
        ("ユーザータグ：大分類＋小分類", SearchModuleKind.UserTag, S() with { UserTags = [new UserTagCondition { Top = "大分類1", Subs = ["小1"] }] }),
        ("属性：1つ 50〜100", SearchModuleKind.Attribute, S() with { Ranges = [new AttributeRange("属性0", 50, 100)] }),
        ("価格：払った額 1,000〜5,000円", SearchModuleKind.Price, S() with { Choice = "paid", Min = "1000", Max = "5000" }),
        ("価格：BOOTHの価格 1,000〜5,000円", SearchModuleKind.Price, S() with { Choice = "booth", Min = "1000", Max = "5000" }),
        ("スキ数：100以上", SearchModuleKind.WishList, S() with { Min = "100", MaxEnabled = false }),
        ("公開日：2024年", SearchModuleKind.PublishedAt, S() with { Min = "2024/1/1", Max = "2024/12/31" }),
        ("入手日：2025年以降", SearchModuleKind.AcquiredAt, S() with { Min = "2025/1/1", MaxEnabled = false }),
        ("最近：開いた 30日", SearchModuleKind.Recent, S() with { Choice = "viewed", Min = "30" }),
    ];

    public static (string Name, SearchModuleKind Kind, SearchModuleState State) Of(string prefix)
        => All.First(sample => sample.Name.StartsWith(prefix, StringComparison.Ordinal));

    private static SearchModuleState S() => new() { Kind = "x" };
}

/// <summary>SearchViewModel.Filtering.cs の照らし方の写し。</summary>
internal sealed class Pipeline
{
    private readonly World _world;
    private readonly List<SearchModule> _modules;
    private readonly List<SearchModule> _active;
    private readonly QueryHolder? _query;
    private SearchModuleContext _context;

    public Pipeline(World world, List<SearchModule> modules, string? query, bool freshIndex = false)
    {
        _world = world;
        _modules = modules;
        _active = modules.Where(module => module.IsActive).ToList();
        _query = query is null ? null : new QueryHolder(SearchQuery.Parse(query));
        _context = world.Context(freshIndex);
    }

    public void NewContext(bool freshIndex) => _context = _world.Context(freshIndex);

    private bool PassesBase(ItemRecord item) => !item.Local.IsHidden;

    private bool MatchesQuery(ItemRecord item)
        => _query is null || SearchQuery.Matches(_query.Node, _world.Haystacks[item.Id], SearchOptions.Default);

    private bool Matches(ItemRecord item, SearchModule? except)
    {
        if (!PassesBase(item))
        {
            return false;
        }

        foreach (var module in _active)
        {
            if (!ReferenceEquals(module, except) && !module.Passes(item, _context))
            {
                return false;
            }
        }

        return MatchesQuery(item);
    }

    public int Filter() => _world.Items.Count(item => Matches(item, null));

    /// <summary>今の作り：条件ごとに、全商品を「その条件を除いた他の全部」で照らし直す。</summary>
    public void CountsNow()
    {
        foreach (var module in _modules)
        {
            var others = _world.Items.Where(item => Matches(item, module)).ToList();
            module.RefreshCounts(others, _context);
        }
    }

    /// <summary>
    /// 案(b)：商品ごとに、外れた条件を2つ目まで数える（2つ外れたら、どの条件の件数にも入らない）。
    /// 外れが0の商品は全条件の「他の全部」に入り、外れが1つの商品はその条件の「他の全部」にだけ入る。
    /// </summary>
    public int CountsByMask()
    {
        var pass = new List<ItemRecord>();
        var onlyFails = _active.Select(_ => new List<ItemRecord>()).ToArray();
        foreach (var item in _world.Items)
        {
            if (!PassesBase(item) || !MatchesQuery(item))
            {
                continue;
            }

            var fails = 0;
            var which = -1;
            for (var index = 0; index < _active.Count && fails < 2; index++)
            {
                if (!_active[index].Passes(item, _context))
                {
                    fails++;
                    which = index;
                }
            }

            if (fails == 0)
            {
                pass.Add(item);
            }
            else if (fails == 1)
            {
                onlyFails[which].Add(item);
            }
        }

        foreach (var module in _modules)
        {
            var index = _active.IndexOf(module);
            var others = index < 0 ? pass : [.. pass, .. onlyFails[index]];
            module.RefreshCounts(others, _context);
        }

        return pass.Count;
    }

    /// <summary>
    /// アプリの今の作り（2026-10-01 に入れた案b・c）：<see cref="SearchFilterPass"/> で結果と件数の材料を1回で作り、件数を数える。
    /// 絞り込みと件数の両方の分を含む。
    /// </summary>
    public int AppPass()
    {
        var pass = SearchFilterPass.Run(_world.Items, _modules, item => PassesBase(item) && MatchesQuery(item), _context);
        foreach (var module in _modules)
        {
            module.RefreshCounts(pass.OthersFor(module), _context);
        }

        return pass.Matches.Count;
    }
}

internal static class Bench
{
    private const int Warmups = 3;
    private const int Runs = 15;

    /// <summary>1. 条件の種類ごとの、商品1件あたりの重さと当たる割合。</summary>
    public static void Kinds(World world)
    {
        Console.WriteLine("条件 | 1件あたり ns（中央・最小〜最大） | 割り当て B/件 | 当たる割合");
        foreach (var (name, kind, state) in Samples.All)
        {
            var module = world.Module(kind, state);
            var context = world.Context();
            var hits = world.Items.Count(item => module.Matches(item, context));
            var (median, min, max, bytes) = Measure(() =>
            {
                foreach (var item in world.Items)
                {
                    module.Matches(item, context);
                }
            });
            Row(name, median, min, max, bytes, world.Items.Count, hits);

            if (kind == SearchModuleKind.Avatar)
            {
                // 対応アバターの展開を作り直した直後（アバターの登録を変えた・商品を読み直した）
                var cold = Measure(() =>
                {
                    var fresh = world.Context(freshIndex: true);
                    foreach (var item in world.Items)
                    {
                        module.Matches(item, fresh);
                    }
                });
                Row(name + "（展開を作り直した直後）", cold.Median, cold.Min, cold.Max, cold.Bytes, world.Items.Count, hits);
            }
        }

        var query = new QueryHolder(SearchQuery.Parse("ドレス"));
        var queryHits = world.Items.Count(item => SearchQuery.Matches(query.Node, world.Haystacks[item.Id], SearchOptions.Default));
        var text = Measure(() =>
        {
            foreach (var item in world.Items)
            {
                SearchQuery.Matches(query.Node, world.Haystacks[item.Id], SearchOptions.Default);
            }
        });
        Row("文字列：ドレス（照合だけ）", text.Median, text.Min, text.Max, text.Bytes, world.Items.Count, queryHits);

        // 範囲・日付は、照らすたびに打った字を数・日付に読み直している。その分だけを測る
        var price = (RangeModule)world.Module(SearchModuleKind.Price, Samples.Of("価格：払った額").State);
        var parse = Measure(() =>
        {
            for (var index = 0; index < world.Items.Count; index++)
            {
                _ = price.Min;
                _ = price.Max;
            }
        });
        Row("（内訳）価格の下限・上限を読み直す分", parse.Median, parse.Min, parse.Max, parse.Bytes, world.Items.Count, -1);
        var date = (DateModule)world.Module(SearchModuleKind.PublishedAt, Samples.Of("公開日").State);
        var dateParse = Measure(() =>
        {
            for (var index = 0; index < world.Items.Count; index++)
            {
                _ = date.Since;
                _ = date.Till;
                _ = date.IsActive;
            }
        });
        Row("（内訳）日付の始まり・終わりを読み直す分", dateParse.Median, dateParse.Min, dateParse.Max, dateParse.Bytes, world.Items.Count, -1);

        // 案(c)の見積もり：アプリの部品ではなく、同じ判定を「絞り込みの1回で1回だけ用意した材料」で書いた形。
        // 意味の細部（文字の比べ方）は同じではないので、どこまで縮みうるかの目安にだけ使う
        var since = new DateOnly(2024, 1, 1);
        var till = new DateOnly(2024, 12, 31);
        Trial(world, "（案c 試し）公開日：境の日付を前もって読んだ形", item =>
            item.Booth.PublishedAt is { } at && DateOnly.FromDateTime(at.LocalDateTime) is var day && day >= since && day <= till);
        var folder = @"D:\BOOTH\3Dモデル\";
        Trial(world, "（案c 試し）ファイルの場所：前もって正規化した場所と序数で比べた形", item =>
            item.Local.LocalFiles.Any(file => !file.Detached && file.Paths.Any(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            || item.Local.LocalFolders.Any(entry => entry.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)));
        Trial(world, "（案c 試し）BOOTHタグ：序数で比べた形", item =>
            item.Booth.Tags.Any(tag => string.Equals(tag, "タグ1", StringComparison.OrdinalIgnoreCase)));
        Trial(world, "（案c 試し）ユーザータグ：条件の並びを作り直さない形", item =>
            item.Local.UserTags.Any(tag => string.Equals(tag.Top, "大分類1", StringComparison.CurrentCultureIgnoreCase)));
        Trial(world, "（案c 試し）所持：外したファイルを数えるだけの形", item =>
            item.Local.LocalFolders.Count > 0 || item.Local.LocalFiles.Any(file => !file.Detached));
    }

    private static void Trial(World world, string name, Func<ItemRecord, bool> predicate)
    {
        var hits = world.Items.Count(predicate);
        var timing = Measure(() =>
        {
            foreach (var item in world.Items)
            {
                predicate(item);
            }
        });
        Row(name, timing.Median, timing.Min, timing.Max, timing.Bytes, world.Items.Count, hits);
    }

    private static readonly string[] Set3 = ["対応アバター：1体", "所持", "カテゴリ：子1つ"];
    private static readonly string[] Set6 = [.. Set3, "価格：払った額", "ユーザータグ：大分類1つ", "ファイルの場所"];
    private static readonly string[] Set10 = [.. Set6, "BOOTHタグ：1つ", "公開日", "属性", "有料・無料"];

    /// <summary>2. 照らす順を変えたときの絞り込み1回。</summary>
    public static void Order(World world)
    {
        // 順を決める材料：1件あたりの重さと外れる割合（Kinds と同じ測り方を短く）
        var profile = new Dictionary<string, (double Ns, double Fail)>();
        foreach (var name in Set10)
        {
            var (_, kind, state) = Samples.Of(name);
            var module = world.Module(kind, state);
            var context = world.Context();
            var hits = world.Items.Count(item => module.Matches(item, context));
            var timing = Measure(() =>
            {
                foreach (var item in world.Items)
                {
                    module.Matches(item, context);
                }
            });
            profile[name] = (timing.Median * 1e6 / world.Items.Count, 1 - (double)hits / world.Items.Count);
        }

        Console.WriteLine("条件の数 | 並び | 絞り込み1回 ms（中央・最小〜最大） | 条件を照らした回数/件");
        foreach (var set in new[] { Set3, Set6, Set10 })
        {
            // 期待の重さが小さい順＝「重さ÷外れる割合」が小さい物から（外れたら打ち切るので）
            var best = set.OrderBy(name => profile[name].Ns / Math.Max(profile[name].Fail, 0.001)).ToArray();
            var worst = best.Reverse().ToArray();
            foreach (var (label, order) in new[] { ("作り物の並び", set), ("軽く外れる物を先", best), ("その逆", worst) })
            {
                var modules = order.Select(name => world.Module(Samples.Of(name).Kind, Samples.Of(name).State)).ToList();
                var pipeline = new Pipeline(world, modules, query: null);
                var counted = Counting(modules, world);
                var timing = Measure(() => pipeline.Filter());
                Console.WriteLine($"{set.Length} | {label} | {timing.Median:F2}（{timing.Min:F2}〜{timing.Max:F2}） | {counted:F2}");
            }

            Console.WriteLine($"  軽く外れる物を先の並び：{string.Join(" → ", best)}");
        }
    }

    /// <summary>外れたら打ち切るとき、1件あたり何個の条件を照らしたか。</summary>
    private static double Counting(List<SearchModule> modules, World world)
    {
        var context = world.Context();
        long total = 0;
        foreach (var item in world.Items)
        {
            foreach (var module in modules)
            {
                total++;
                if (!module.Matches(item, context))
                {
                    break;
                }
            }
        }

        return (double)total / world.Items.Count;
    }

    /// <summary>3・4. 選択肢の件数の重さと、条件の数での伸び。今の作りと案(b)。</summary>
    public static void Counts(World world)
    {
        // 15・20 は「同じ種類を複数」「除く」で条件が増えた姿。足すのは広めに当たる条件（結果が0件にならないよう）
        string[] extra = ["カテゴリ：親1つ", "R-18", "価格：BOOTHの価格", "スキ数", "入手日", "BOOTHタグ：3つ", "対応アバター：共通素体", "ショップ", "販売終了", "お気に入り"];
        var sets = new (string Label, string[] Names)[]
        {
            ("3", Set3), ("6", Set6), ("10", Set10), ("15", [.. Set10, .. extra[..5]]), ("20", [.. Set10, .. extra]),
        };

        // 「アプリの1回」は絞り込みと件数を合わせた時間（SearchFilterPass）。比べる相手は「絞り込み＋件数（今の作りの数え方）」。
        // 条件の部品はアプリの物なので、案c（照らす前の準備）はどの列にも効いている。案c の前の数は 2026-10-01 の案の文書 §9
        Console.WriteLine("条件の数 | 結果の件数 | 絞り込み ms | 件数（条件ごとに照らし直す）ms | 件数（案b の写し）ms | アプリの1回（絞り込み＋件数）ms");
        foreach (var (label, names) in sets)
        {
            var modules = names.Select(name => world.Module(Samples.Of(name).Kind, Samples.Of(name).State)).ToList();
            var pipeline = new Pipeline(world, modules, query: null);
            var result = pipeline.Filter();
            var filter = Measure(() => pipeline.Filter());
            var now = Measure(pipeline.CountsNow);
            var snapshot = Snapshot(modules);
            var mask = Measure(() => pipeline.CountsByMask());
            var same = Snapshot(modules) == snapshot ? "" : "（件数が食い違う）";
            var app = Measure(() => pipeline.AppPass());
            var sameApp = Snapshot(modules) == snapshot && pipeline.AppPass() == result ? "" : "（アプリの1回と食い違う）";
            Console.WriteLine($"{label} | {result} | {filter.Median:F2} | {now.Median:F2}（{now.Min:F2}〜{now.Max:F2}） | {mask.Median:F2}{same} | {app.Median:F2}（{app.Min:F2}〜{app.Max:F2}）{sameApp}");
        }

        // 文字列を入れたとき（今の作りは文字列を条件の後に照らす。アプリの1回は先に照らす）
        var withText = Set6.Select(name => world.Module(Samples.Of(name).Kind, Samples.Of(name).State)).ToList();
        var textPipeline = new Pipeline(world, withText, query: "ドレス");
        var textFilter = Measure(() => textPipeline.Filter());
        var textNow = Measure(textPipeline.CountsNow);
        var textMask = Measure(() => textPipeline.CountsByMask());
        var textApp = Measure(() => textPipeline.AppPass());
        Console.WriteLine($"6＋文字列「ドレス」 | {textPipeline.Filter()} | {textFilter.Median:F2} | {textNow.Median:F2} | {textMask.Median:F2} | {textApp.Median:F2}");
    }

    /// <summary>件数を写し取って、2つの数え方で同じになるかを見る。</summary>
    private static string Snapshot(List<SearchModule> modules)
        => string.Join("|", modules.Select(module => module switch
        {
            ListModule list => string.Join(",", list.Chips.Select(chip => chip.Count)) + list.MatchedLabel + list.IncludeText,
            ChoiceModule choice => string.Join(",", choice.Options.Select(option => option.Count)),
            _ => string.Empty,
        }));

    /// <summary>新しいプロセスでの最初の1回（JIT 込み）と、2回目以降。</summary>
    public static void Cold(World world)
    {
        var modules = Set6.Select(name => world.Module(Samples.Of(name).Kind, Samples.Of(name).State)).ToList();
        var pipeline = new Pipeline(world, modules, query: null, freshIndex: true);
        for (var run = 1; run <= 5; run++)
        {
            var watch = Stopwatch.StartNew();
            pipeline.Filter();
            var filter = watch.Elapsed.TotalMilliseconds;
            pipeline.CountsNow();
            Console.WriteLine($"{run}回目：絞り込み {filter:F2} ms・件数 {watch.Elapsed.TotalMilliseconds - filter:F2} ms（条件6・対応アバターの展開は1回目だけ作り直し）");
        }
    }

    private static (double Median, double Min, double Max, long Bytes) Measure(Action action)
    {
        for (var index = 0; index < Warmups; index++)
        {
            action();
        }

        var times = new List<double>(Runs);
        long bytes = 0;
        for (var index = 0; index < Runs; index++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            action();
            times.Add(watch.Elapsed.TotalMilliseconds);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        times.Sort();
        return (times[Runs / 2], times[0], times[^1], bytes / Runs);
    }

    private static void Row(string name, double median, double min, double max, long bytes, int count, int hits)
    {
        var perItem = median * 1e6 / count;
        var rate = hits < 0 ? "-" : $"{100.0 * hits / count:F1}%";
        Console.WriteLine($"{name} | {perItem:F0}（{min * 1e6 / count:F0}〜{max * 1e6 / count:F0}） | {bytes / count} | {rate}");
    }
}

/// <summary>読んだ検索の文字。</summary>
internal sealed record QueryHolder(SearchNode Node);
