using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 手元の物が**全部**、今つながっていないドライブの上にある商品（カードの帯「取り外し中」・ユーザ判断 2026-11-20）。
/// 無くなったのではなく今は見えないだけなので、「未所持」（持っていない）とも「見つからない」（無いと確かめた）とも分ける。
/// 所持の定義（ファイルかフォルダを1つ以上持つ）は変えない：ここで true の商品は所持のまま、印だけを足す。
/// </summary>
/// <remarks>
/// 見るのはドライブの根がつながっているかだけで、ファイルは見に行かない（数千件の商品を毎回ディスクで確かめない）。
/// 根の答えは <see cref="FilePresenceProbe"/> が1ドライブにつき1回だけ見て覚える（落ちた共有は 3 秒で打ち切る）。
/// 1つでも在るドライブの上に場所があれば false（一部だけ外した商品は、ほかの場所で使えるので帯を掛けない）。
/// </remarks>
public static class DetachedItems
{
    /// <summary>この商品の手元の物が全部つながっていないドライブの上か。何も持っていなければ false。</summary>
    public static bool IsAllOnDetachedDrives(
        ItemRecord item,
        FilePresenceProbe probe,
        Func<LocalFileRecord, string, string> remapFile,
        Func<LocalFolderRecord, string> remapFolder)
    {
        var files = item.Local.OwnedFiles;
        var folders = item.Local.LocalFolders;
        if (files.Count == 0 && folders.Count == 0)
        {
            return false;
        }

        foreach (var file in files)
        {
            // 場所が空のファイルは「無い」（取り外し中ではない）
            if (file.Paths.Count == 0
                || file.Paths.Any(path => !probe.IsOnDetachedDrive(path, remapFile(file, path), PlaceVolumes.Of(file, path))))
            {
                return false;
            }
        }

        return folders.All(folder => probe.IsOnDetachedDrive(folder.Path, remapFolder(folder), folder.Volume));
    }

    /// <summary>該当する商品の番号。</summary>
    public static HashSet<string> Find(
        IEnumerable<ItemRecord> items,
        FilePresenceProbe probe,
        Func<LocalFileRecord, string, string> remapFile,
        Func<LocalFolderRecord, string> remapFolder)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (IsAllOnDetachedDrives(item, probe, remapFile, remapFolder))
            {
                ids.Add(item.Id);
            }
        }

        return ids;
    }
}
