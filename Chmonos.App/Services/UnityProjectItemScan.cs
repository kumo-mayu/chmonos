using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// 手元の商品のうち、Unity プロジェクトの中に入っている物を調べる（改変の「プロジェクトの中を調べる」と検索の条件で共有する）。
///
/// **持っている商品を全部渡す。**ほかの商品と共有しているファイル（同梱の lilToon など）は数えないので、
/// 一部の商品だけで調べると、共有の部品だけで商品が当たってしまう。
/// </summary>
public static class UnityProjectItemScan
{
    /// <param name="progress">読み終えた商品の数。</param>
    public static IReadOnlyList<UnityProjectMatch> Run(
        string project, IReadOnlyList<ItemRecord> items, IProgress<int>? progress, CancellationToken token)
    {
        var paths = new Dictionary<string, IReadOnlyList<UnityPackageAsset>>(StringComparer.Ordinal);
        var done = 0;

        // 同じ zip の包みは控えを1回だけ読んで配る（前は包みごとに控えを丸ごと読んでいた）
        var reads = new UnityPackageReads();
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            paths[item.Id] = UnityImportQueue.PackagesOf(item).SelectMany(reads.ReadAssets)
                .Concat(item.Local.OwnedFiles.SelectMany(reads.StoredIfGone))
                .ToList();
            progress?.Report(++done);
        }

        return UnityProjectMatcher.Match(project, paths);
    }
}
