using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保存先の中に、使う人のアセット（取り込み元・監視フォルダ・商品が記録しているファイルとフォルダ・未確定・除外）が置かれていないかを探す（点検29）。
///
/// 引越しは保存先の中を丸ごと運んで元を消すので、保存先の中にアセットを置いていると、アセットも新しい場所へ運ばれ、元の場所から消える。
/// 記録の場所は付け替えないので、商品とファイルのつながりも切れる。このアプリは「アセットは今ある場所のまま、コピーも移動もしない」と説明しているので、
/// 重なるときは引越しを断る（ユーザ判断 2026-10-08：おすすめの形）
/// </summary>
public static class StoreAssetOverlap
{
    /// <summary>保存先の中にある、使う人のアセットの場所を1つ返す（無ければ null）。読むだけで書かない。</summary>
    public static async Task<string?> FindAsync(DataStore store, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var root = store.Paths.Root;
        bool Inside(string? path) => !string.IsNullOrWhiteSpace(path) && (SamePlace(path, root) || StoreIds.IsInside(path, root));

        foreach (var folder in settings.ImportFolders.Concat(settings.WatchedFolders))
        {
            if (Inside(folder))
            {
                return folder;
            }
        }

        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        foreach (var item in loaded.Items)
        {
            foreach (var path in item.Local.LocalFiles.SelectMany(file => file.Paths).Concat(item.Local.LocalFolders.Select(folder => folder.Path)))
            {
                if (Inside(path))
                {
                    return path;
                }
            }
        }

        foreach (var path in store.Unresolved.Load().SelectMany(file => file.Paths).Concat(store.Excluded.Load().SelectMany(entry => entry.Paths)))
        {
            if (Inside(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool SamePlace(string path, string root)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
