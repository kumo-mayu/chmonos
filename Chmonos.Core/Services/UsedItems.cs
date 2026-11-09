using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>商品を使っている所の数（使っている商品の更新の促し・<c>docs/spec/used-updates.md</c>）。</summary>
/// <param name="Modifications">その商品を使った改変の数（外した行だけの改変は数えない）。</param>
/// <param name="Projects">その商品が入っている Unity のプロジェクトの数（調べたプロジェクトのうち）。</param>
public sealed record UsedBy(int Modifications, int Projects)
{
    public static UsedBy None { get; } = new(0, 0);

    public bool IsUsed => Modifications > 0 || Projects > 0;
}

/// <summary>
/// 改変と Unity のプロジェクトを調べた結果から、商品ごとに「使っている所」を数える。
/// 記録には書かない（改変もプロジェクトの中身もアプリの外で変わるので、見せる所で組む）。
/// 使っている所ごとにアバターも持つ：特定のアバター向けの更新は、そのアバターで使う所だけ数える（ユーザ判断 2026-11-09）。
/// </summary>
public sealed class UsedItems
{
    /// <summary>商品ID → その商品を使った改変のアバター（改変1つにつき1つ）。</summary>
    private readonly Dictionary<string, List<string>> _modifications = new(StringComparer.Ordinal);

    /// <summary>商品ID → その商品が入っているプロジェクトごとの、紐付けた改変のアバター（紐付いた改変が無ければ空＝分からない）。</summary>
    private readonly Dictionary<string, List<IReadOnlyList<string>>> _projects = new(StringComparer.Ordinal);

    public static UsedItems Empty { get; } = new([], new Dictionary<string, UnityProjectScan>());

    /// <param name="scans">調べたプロジェクトの場所 → 中身。</param>
    public UsedItems(IEnumerable<ModificationRecord> modifications, IReadOnlyDictionary<string, UnityProjectScan> scans)
    {
        var records = modifications.ToList();
        foreach (var modification in records)
        {
            foreach (var itemId in modification.Members.Where(member => !member.Detached).Select(member => member.ItemId).Distinct(StringComparer.Ordinal))
            {
                Add(_modifications, itemId, modification.AvatarItemId);
            }
        }

        foreach (var (project, scan) in scans)
        {
            var normalized = UnityProjectPath.Normalize(project) ?? project;
            IReadOnlyList<string> avatars = records
                .Where(record => string.Equals(UnityProjectPath.Normalize(record.UnityProject), normalized, StringComparison.OrdinalIgnoreCase))
                .Select(record => record.AvatarItemId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // 数えるのは unitypackage で入っている物と、同じ絵だと分かった画像だけ（名前が同じだけの画像は当て推量なので促さない）
            var ids = scan.Packages.Select(match => match.ItemId)
                .Concat(scan.Images.Where(pair => pair.Value.Any(image => image.Kind == UnityImageMatchKind.SameImage)).Select(pair => pair.Key))
                .Distinct(StringComparer.Ordinal);
            foreach (var itemId in ids)
            {
                Add(_projects, itemId, avatars);
            }
        }
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string itemId, T value)
    {
        if (!map.TryGetValue(itemId, out var list))
        {
            map[itemId] = list = [];
        }

        list.Add(value);
    }

    /// <summary>使っている所の数。</summary>
    /// <param name="applies">
    /// 使っている所のアバター → その所に当たるか（<see cref="UsedUpdates.AppliesTo"/>）。null なら全部数える。
    /// プロジェクトは、紐付けた改変のアバターのどれかに当たれば数え、紐付いた改変が無ければ（アバターが分からないので）数える。
    /// </param>
    public UsedBy Of(string itemId, Func<string, bool>? applies = null)
    {
        var modifications = _modifications.TryGetValue(itemId, out var avatars)
            ? avatars.Count(avatar => applies is null || applies(avatar))
            : 0;
        var projects = _projects.TryGetValue(itemId, out var places)
            ? places.Count(linked => applies is null || linked.Count == 0 || linked.Any(applies))
            : 0;
        return new UsedBy(modifications, projects);
    }

    /// <summary>商品の控えに当たる所だけを数える（特定のアバター向けの更新なら、そのアバターで使う所だけ）。</summary>
    public UsedBy Of(ItemRecord item, AvatarRegistry registry)
        => item.Local.ContentUpdate is { IsTargeted: true } update
            ? Of(item.Id, avatar => UsedUpdates.AppliesTo(update, avatar, registry))
            : Of(item.Id);
}
