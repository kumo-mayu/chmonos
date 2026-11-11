using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>選んだ商品の手元のファイルを、中身（ハッシュ）ごとに1行にまとめた物。</summary>
/// <param name="Hash">中身。付ける・外す命令にはこれを渡す。</param>
/// <param name="FileName">見せる名前（最初に見つかった場所のファイル名）。どの持ち主も場所を覚えていなければ null。</param>
/// <param name="SizeBytes">1つ分の大きさ。</param>
/// <param name="Paths">持ち主たちの記録にある場所を合わせた物（重なりは1つ）。<c>AttachFile</c> に渡す場所はここから選ぶ。</param>
/// <param name="HolderIds">選んだ商品のうち、このファイルを持つ商品（外した・古い版の行は持ち主に数えない）。渡された順。</param>
public sealed record PooledFile(
    string Hash,
    string? FileName,
    long SizeBytes,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> HolderIds)
{
    /// <summary>重複回数：選んだ商品の中で、このファイルを持つ商品の数（ユーザ判断 2026-11-11 plan 30「選んだ商品の中で数える」）。</summary>
    public int HolderCount => HolderIds.Count;
}

/// <summary>
/// 選んだ商品の間でファイルを割り振る画面（plan 30）の材料：選んだ商品が持つファイルを1つの溜まりにし、
/// 中身ごとに持ち主と重複回数を返す。**読むだけ**で、付ける・外すは命令（<c>UiCommand.AttachFile</c> の
/// <see cref="OtherHolders.Share"/>・<c>UiCommand.DetachFile</c>）で行う。
/// 頭文字での組分けは、実際のデータで区切り方を測ってから決める（plan 30）ので、ここでは持たない。
/// </summary>
public static class FilePool
{
    /// <summary>名前の順（名前の無い物は後ろ）、同じ名前は中身の順。</summary>
    public static IReadOnlyList<PooledFile> Of(IEnumerable<ItemRecord> selected)
    {
        var byHash = new Dictionary<string, (LocalFileRecord First, List<string> Paths, List<string> Holders)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected.DistinctBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (var file in item.Local.OwnedFiles)
            {
                if (!byHash.TryGetValue(file.Hash, out var entry))
                {
                    entry = (file, [], []);
                    byHash[file.Hash] = entry;
                }

                entry.Paths.AddRange(file.Paths);

                // 1つの商品が同じ中身を2行持つ手書きの記録でも、持ち主は1回
                if (!entry.Holders.Contains(item.Id, StringComparer.Ordinal))
                {
                    entry.Holders.Add(item.Id);
                }
            }
        }

        return byHash.Values
            .Select(entry =>
            {
                var paths = entry.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new PooledFile(
                    entry.First.Hash,
                    paths.Count > 0 ? Path.GetFileName(paths[0]) : null,
                    entry.First.SizeBytes,
                    paths,
                    entry.Holders);
            })
            .OrderBy(file => file.FileName is null)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(file => file.Hash, StringComparer.Ordinal)
            .ToList();
    }
}
