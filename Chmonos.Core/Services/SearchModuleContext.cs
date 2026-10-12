using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 「最近」の足跡を、商品IDから引ける形にまとめたもの。
///
/// 絞り込みの1回ぶんで使い回す。1商品ごとにファイルを読み直さないため。
/// </summary>
public sealed record RecentTimes(
    IReadOnlyDictionary<string, DateTimeOffset> Added,
    IReadOnlyDictionary<string, DateTimeOffset> Used,
    IReadOnlyDictionary<string, DateTimeOffset> Viewed)
{
    public static RecentTimes Empty { get; } = new(
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>());

    public DateTimeOffset? Of(string itemId, RecentKind kind)
    {
        var source = kind switch
        {
            RecentKind.Added => Added,
            RecentKind.Used => Used,
            _ => Viewed,
        };

        return source.TryGetValue(itemId, out var at) ? at : null;
    }
}

/// <summary>
/// 改変から引いた「どの改変・アバター・プロジェクトにどの商品を使ったか」。
///
/// **絞り込みの1回ぶんで使い回す**（<see cref="RecentTimes"/> と同じ理由。1商品ごとに改変のファイルを読み直さないため）。
/// </summary>
public sealed record ModificationUsage(
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByAvatar,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByModification,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByProject,
    IReadOnlyList<ModificationRecord> Records)
{
    /// <summary>
    /// 商品ごとの、「使ったもの」に入れている改変の数（並べ替えの「改変に使った回数」。ユーザ判断 2026-10-07）。
    /// 1つの改変に同じ商品が2回入っていても1回と数える（改変ごとの集合から数える）
    /// </summary>
    public IReadOnlyDictionary<string, int> UseCounts { get; } = ItemIdsByModification.Values
        .SelectMany(members => members)
        .GroupBy(itemId => itemId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public static ModificationUsage Empty { get; } = new(
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
        []);

    public static ModificationUsage From(IEnumerable<ModificationRecord> records)
    {
        var list = records.ToList();
        var byAvatar = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var byProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var byModification = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var record in list)
        {
            // 同じ商品が2回入っていても、絞り込みに要るのは「入っているか」だけ
            var members = record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            byModification[record.Id] = members;
            Collect(byAvatar, record.AvatarItemId, members);

            if (!string.IsNullOrWhiteSpace(record.UnityProject))
            {
                Collect(byProject, record.UnityProject!, members);
            }
        }

        return new ModificationUsage(
            byAvatar.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal),
            byModification,
            byProject.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            list);

        static void Collect(Dictionary<string, HashSet<string>> map, string key, IEnumerable<string> members)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }

            set.UnionWith(members);
        }
    }

    public bool Used(string avatarItemId, string itemId)
        => ItemIdsByAvatar.TryGetValue(avatarItemId, out var used) && used.Contains(itemId);

    public bool InModification(string modificationId, string itemId)
        => ItemIdsByModification.TryGetValue(modificationId, out var used) && used.Contains(itemId);

    public bool InProject(string projectPath, string itemId)
        => ItemIdsByProject.TryGetValue(projectPath, out var used) && used.Contains(itemId);
}

/// <summary>絞り込み1回ぶんの材料。モジュールが商品を照らすときに使う。</summary>
public sealed class SearchModuleContext
{
    private readonly Func<AvatarCompatibilityIndex> _compatibility;
    private AvatarCompatibilityIndex? _index;

    public SearchModuleContext(
        Func<AvatarCompatibilityIndex> compatibility,
        ModificationUsage modifications,
        RecentTimes recent,
        Func<string, string>? pathMap,
        DateTimeOffset now)
    {
        _compatibility = compatibility;
        Modifications = modifications;
        Recent = recent;
        PathMap = pathMap;
        Now = now;
    }

    /// <summary>素体経由の対応の索引。対応アバターで絞るときだけ作る。</summary>
    public AvatarCompatibilityIndex Compatibility => _index ??= _compatibility();

    public ModificationUsage Modifications { get; }

    public RecentTimes Recent { get; }

    /// <summary>外付けのドライブ文字が変わった記録を今の場所に読み替える（フォルダの条件）。</summary>
    public Func<string, string>? PathMap { get; }

    public DateTimeOffset Now { get; }

    /// <summary>
    /// 手元の商品の全部（絞る前）。範囲の外れ値の境・日付の端のように、商品の全体から決まる値を求めるときに使う。
    /// 画面の条件はこれを使わず、自分で求めた値を渡す（帯を描くときに同じ物を求めている）。
    /// </summary>
    public IReadOnlyList<ItemRecord> Items { get; init; } = [];

    /// <summary>
    /// 何も持たない材料（今日は作った時点）。商品の全体を見ずに値の決まる条件（画面が自分で端を求めた日付など）が、絞り込みの外で使う。
    /// </summary>
    public static SearchModuleContext Unused { get; } = new(
        () => AvatarCompatibilityIndex.Build(new AvatarRegistry()),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        DateTimeOffset.Now);

    /// <summary>お気に入りのショップの鍵（shops.json の星）。ショップの条件の「お気に入りのショップ」に使う。</summary>
    public IReadOnlySet<string> FavoriteShops { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>商品ID → 未読の「商品の更新」の知らせが含む種類（条件「更新通知あり」とカードの札と同じ表）。</summary>
    public Func<string, BoothChangeKind> UnreadUpdateKinds { get; init; } = _ => BoothChangeKind.None;

    /// <summary>
    /// 取り込みの③（検出）をまだ待っている商品か。編集状況の「未入力のみ」に数えない（編集画面に出てこないため）。
    /// アプリの外（API）からは分からないので、既定は待っていない。
    /// </summary>
    public Func<string, bool> IsAwaitingDetection { get; init; } = _ => false;
}
