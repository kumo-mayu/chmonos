using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 設定の取り込み元（<see cref="AppSettings.ImportFolders"/>）と監視対象（<see cref="AppSettings.WatchedFolders"/>）を、
/// **1件ずつ足し引きする**変え方。<c>UiCommand.ChangeSettings</c> に渡し、錠の中で今の設定に当てる。
///
/// 画面が持つ一覧の写しで丸ごと書かないためにここへ集めた（技術的負債 1-1 の再発）。
/// 取り込み画面は履歴を起動時に1回だけ読み、設定画面は保存のたびに自分の一覧で書いていたので、
/// 片方で足した取り込み元を、もう片方の何気ない保存（別の項目を変えただけ）が消していた。
/// </summary>
public static class FolderListChange
{
    /// <summary>取り込み元の履歴に足す。既にある物は足さない（並びは変えない）。</summary>
    public static AppSettings AddImportFolders(AppSettings settings, IEnumerable<string> paths)
        => settings with { ImportFolders = Added(settings.ImportFolders, paths) };

    /// <summary>取り込み元の履歴から外す。フォルダとファイルには触らない。</summary>
    public static AppSettings RemoveImportFolder(AppSettings settings, string path)
        => settings with { ImportFolders = Removed(settings.ImportFolders, path) };

    /// <summary>監視対象に足す・外す。</summary>
    public static AppSettings SetWatched(AppSettings settings, string path, bool watch)
        => settings with
        {
            WatchedFolders = watch ? Added(settings.WatchedFolders, [path]) : Removed(settings.WatchedFolders, path),
        };

    private static IReadOnlyList<string> Added(IReadOnlyList<string>? current, IEnumerable<string> paths)
    {
        // 手で直した JSON で欠けていても落とさない（空として受ける）
        var list = (current ?? []).ToList();
        foreach (var path in paths)
        {
            if (!list.Any(existing => PathText.Same(existing, path)))
            {
                list.Add(path);
            }
        }

        return list;
    }

    private static IReadOnlyList<string> Removed(IReadOnlyList<string>? current, string path)
        => [.. (current ?? []).Where(existing => !PathText.Same(existing, path))];
}
