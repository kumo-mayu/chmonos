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
/// </summary>
public sealed class UsedItems
{
    private readonly Dictionary<string, int> _modifications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _projects = new(StringComparer.Ordinal);

    public static UsedItems Empty { get; } = new([], new Dictionary<string, UnityProjectScan>());

    /// <param name="scans">調べたプロジェクトの場所 → 中身。</param>
    public UsedItems(IEnumerable<ModificationRecord> modifications, IReadOnlyDictionary<string, UnityProjectScan> scans)
    {
        foreach (var modification in modifications)
        {
            foreach (var itemId in modification.Members.Where(member => !member.Detached).Select(member => member.ItemId).Distinct(StringComparer.Ordinal))
            {
                _modifications[itemId] = _modifications.GetValueOrDefault(itemId) + 1;
            }
        }

        foreach (var scan in scans.Values)
        {
            // 数えるのは unitypackage で入っている物と、同じ絵だと分かった画像だけ（名前が同じだけの画像は当て推量なので促さない）
            var ids = scan.Packages.Select(match => match.ItemId)
                .Concat(scan.Images.Where(pair => pair.Value.Any(image => image.Kind == UnityImageMatchKind.SameImage)).Select(pair => pair.Key))
                .Distinct(StringComparer.Ordinal);
            foreach (var itemId in ids)
            {
                _projects[itemId] = _projects.GetValueOrDefault(itemId) + 1;
            }
        }
    }

    public UsedBy Of(string itemId)
        => new(_modifications.GetValueOrDefault(itemId), _projects.GetValueOrDefault(itemId));
}
