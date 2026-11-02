namespace Chmonos.Core.Services;

/// <summary>プロジェクトの中を調べた結果（検索の条件が持ち、<see cref="Storage.UnityScanStore"/> が次の起動のために控える）。</summary>
/// <param name="Packages">unitypackage で入っている商品。</param>
/// <param name="Images">手で置いた画像として見つかった物（商品ごと）。</param>
public sealed record UnityProjectScan(
    IReadOnlyList<UnityProjectMatch> Packages,
    IReadOnlyDictionary<string, IReadOnlyList<UnityImageMatch>> Images)
{
    public static UnityProjectScan Empty { get; } = new([], new Dictionary<string, IReadOnlyList<UnityImageMatch>>());

    private readonly HashSet<string> _packageIds = Packages.Select(match => match.ItemId).ToHashSet(StringComparer.Ordinal);

    /// <param name="sameName">「名前が同じ」だけの画像でも当てるか。</param>
    public bool Contains(string itemId, bool sameName)
        => _packageIds.Contains(itemId)
            || (Images.TryGetValue(itemId, out var images)
                && images.Any(image => sameName || image.Kind == UnityImageMatchKind.SameImage));

    /// <summary>絞り込みの結果が変わらないか。同じなら絞り直さない（調べ直すたびに一覧を組み直すと、見ている所が動く）。</summary>
    public bool FindsSameAs(UnityProjectScan other)
        => _packageIds.SetEquals(other._packageIds)
            && Images.Count == other.Images.Count
            && Images.All(pair => other.Images.TryGetValue(pair.Key, out var images) && pair.Value.SequenceEqual(images));
}
