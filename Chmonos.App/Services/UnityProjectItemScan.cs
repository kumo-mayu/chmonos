using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// 手元の商品のうち、Unity プロジェクトの中に入っている物を調べる（改変の「プロジェクトの中を調べる」と検索の条件で共有する）。
///
/// **持っている商品を全部渡す。**ほかの商品と共有しているファイル（同梱の lilToon など）は数えないので、
/// 一部の商品だけで調べると、共有の部品だけで商品が当たってしまう。
/// unitypackage ごとに、どの unitypackage か（zip のハッシュと zip の中の場所）を添えて渡す。同じ zip をセットと単品に付けたとき、
/// その中身を共有の部品と見て両方とも落とさないため（<see cref="UnityPackageContents"/>）。
/// </summary>
public static class UnityProjectItemScan
{
    /// <param name="progress">読み終えた商品の数。</param>
    public static IReadOnlyList<UnityProjectMatch> Run(
        string project, IReadOnlyList<ItemRecord> items, IProgress<int>? progress, CancellationToken token)
        => UnityProjectMatcher.Match(project, ReadPackages(items, progress, token));

    /// <summary>unitypackage で入っている物と、手で置いた画像を調べる。</summary>
    public static UnityProjectScan RunWithImages(
        string project,
        IReadOnlyList<ItemRecord> items,
        ZipImageStore images,
        IProgress<int>? progress,
        CancellationToken token)
    {
        var packages = ReadPackages(items, progress, token);
        var matches = UnityProjectMatcher.Match(project, packages);

        // unitypackage で入ったと分かっているファイルは、手で置いた画像と取り違えない
        var fromPackages = packages.Values.SelectMany(contents => contents).SelectMany(contents => contents.Assets).Select(asset => asset.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new UnityProjectScan(matches, FindImages(project, items, images, fromPackages, token));
    }

    /// <summary>1つの商品の、手で置いた画像を調べる（「Unityで選択」）。</summary>
    public static IReadOnlyList<UnityImageMatch> FindImages(string project, ItemRecord item, ZipImageStore images, CancellationToken token)
    {
        var fromPackages = ReadPackages([item], null, token).Values.SelectMany(contents => contents).SelectMany(contents => contents.Assets).Select(asset => asset.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return FindImages(project, [item], images, fromPackages, token).GetValueOrDefault(item.Id) ?? [];
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<UnityImageMatch>> FindImages(
        string project,
        IReadOnlyList<ItemRecord> items,
        ZipImageStore images,
        IReadOnlySet<string> fromPackages,
        CancellationToken token)
    {
        var sources = items.ToDictionary(
            item => item.Id,
            item =>
            {
                token.ThrowIfCancellationRequested();
                return (IReadOnlyList<UnityItemImage>)ImagesOf(item, images, token).ToList();
            },
            StringComparer.Ordinal);
        token.ThrowIfCancellationRequested();
        return UnityProjectImages.Match(UnityProjectImages.ListProject(project, token), sources, fromPackages, token);
    }

    /// <summary>商品の画像：単体のファイル・zip の中・登録したフォルダの中（今在る物だけ）。</summary>
    private static IEnumerable<UnityItemImage> ImagesOf(ItemRecord item, ZipImageStore images, CancellationToken token)
    {
        foreach (var file in item.Local.OwnedFiles)
        {
            foreach (var image in UnityProjectImages.ImagesOf(file, images))
            {
                yield return image;
            }
        }

        foreach (var folder in item.Local.LocalFolders)
        {
            foreach (var image in UnityProjectImages.ImagesInFolder(folder.Path, token))
            {
                yield return image;
            }
        }
    }

    private static Dictionary<string, IReadOnlyList<UnityPackageContents>> ReadPackages(
        IReadOnlyList<ItemRecord> items, IProgress<int>? progress, CancellationToken token)
    {
        var paths = new Dictionary<string, IReadOnlyList<UnityPackageContents>>(StringComparer.Ordinal);
        var done = 0;

        // 同じ zip の包みは控えを1回だけ読んで配る（前は包みごとに控えを丸ごと読んでいた）
        var reads = new UnityPackageReads();
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            // 包んでいる手元のファイル（Owner）を添える。どの unitypackage かの印はそのハッシュから組む
            paths[item.Id] = ModificationSendPlan.CandidatesOf(item).Select(candidate => reads.ReadContents(candidate.Place.Entry, candidate.Owner))
                .Concat(item.Local.OwnedFiles.SelectMany(reads.StoredContentsIfGone))
                .ToList();
            progress?.Report(++done);
        }

        return paths;
    }
}
