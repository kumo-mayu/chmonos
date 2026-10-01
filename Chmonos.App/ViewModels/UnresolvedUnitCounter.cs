using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// ナビの札の未確定の数。**未確定の画面の見出しと同じく、登録する回数で数える**（ユーザ指示 2026-09-29：
/// zipの中身・展開したフォルダは1件。実際にIDを登録する回数を想像できるように）。
/// 画面と同じ行の組み方（<see cref="ResolveViewModel.BuildRows"/>）を通すので、登録済みのzipの中身も同じく数えない。
///
/// 行を組むにはフォルダの中と、元のzipが今もあるかを見る（ディスクを見る）。札は取り込み中に何度も読み直すので、
/// **未確定の記録・商品が持っているファイルの数・取り込み元が前と同じなら、前の数を使う。**
///
/// 「同じ」は2段で見る（2026-09-30）：
/// <list type="number">
/// <item>記録の印（大きさ・更新日時・書き込みの数）が前と同じなら、**記録を読まずに**前の数を返す。
///   前は、材料が同じかを見るために記録を毎回読み直していた（未確定 8万件で1回 190〜270ms・68MB。
///   取り込み中は数え直しが商品1件あたり約2回頼まれる）。</item>
/// <item>印が変わっていたら記録の中身で比べ（札の読み手がこの回に読んだ物を使う。同じ記録を2回読まない）、
///   中身が同じなら行は組まない（同じ一覧を書き直しただけの回）。</item>
/// </list>
/// 裏のスレッドで呼ぶ。
/// </summary>
internal sealed class UnresolvedUnitCounter
{
    /// <param name="Stamp">数えたときの記録の印。</param>
    /// <param name="OwnedCount">商品が持っているファイルの場所の数。</param>
    /// <param name="Folders">取り込み元（大文字小文字を均して並べた物）。</param>
    /// <param name="Signature">記録の中身（ハッシュと場所）と上の2つから作った印。</param>
    private sealed record Memo(JsonFileStamp Stamp, int OwnedCount, string Folders, int Signature, int Count);

    private readonly object _gate = new();
    private Memo? _memo;

    /// <param name="reading">札の読み手が数えた結果（この回に記録を読んでいれば、その中身を持つ）。</param>
    /// <param name="load">記録を読む。読み手がこの回に読んでおらず、覚えも使えないときだけ呼ぶ。</param>
    /// <param name="items">検索が抱えている商品の写し（持っているファイルの場所を見る）。</param>
    /// <param name="importFolders">取り込み元（展開物の根をここより広げない）。</param>
    /// <returns>登録する回数と、行を組み直して数えたか（組み直した回は、呼び手が使い終わったメモリを返させる）。</returns>
    public (int Count, bool Rebuilt) Count(
        NavReading reading,
        Func<List<UnresolvedFile>> load,
        IReadOnlyList<ItemRecord> items,
        IReadOnlyList<string> importFolders)
    {
        // 読み手が数えるのはファイルの数。0件なら記録を読まずに0
        if (reading.Counts.Unresolved == 0)
        {
            return (0, false);
        }

        var owned = items
            .SelectMany(item => item.Local.OwnedFiles)
            .SelectMany(file => file.Paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = string.Join('\n', importFolders.Select(folder => folder.ToUpperInvariant()));

        // 行を組むのは1本ずつ。取り込みの終わりには数え直しが続けて2回頼まれ（検索の読み直しと札の更新）、1回目が組み終える前に
        // 2回目が始まって、同じ行を2本が並んで組んでいた（8万件で各4秒・ゴミも2倍。2026-09-30 に採った足跡で見た）。
        // 後から来た方は待って、先の結果を使う（記録を読むのも錠の中なので、2本目は読まずに済む）。裏のスレッドなので待っても画面は止まらない
        lock (_gate)
        {
            if (_memo is { } memo && memo.Stamp == reading.UnresolvedStamp
                && memo.OwnedCount == owned.Count && memo.Folders == folders)
            {
                return (memo.Count, false);
            }

            var files = reading.UnresolvedRead ?? load();
            if (files.Count == 0)
            {
                return (0, false);
            }

            var signature = Signature(files, owned.Count, importFolders);
            if (_memo is { } same && same.Signature == signature)
            {
                // 同じ一覧を書き直しただけ。印だけ新しくして、次の回は記録を読まずに済ませる
                _memo = same with { Stamp = reading.UnresolvedStamp, OwnedCount = owned.Count, Folders = folders };
                return (same.Count, false);
            }

            var rows = ResolveViewModel.BuildRows(files, owned, importFolders);
            var count = Core.Scanning.UnresolvedUnits.Count(rows.Rows.Select(row => row.UnitKey));
            _memo = new Memo(reading.UnresolvedStamp, owned.Count, folders, signature, count);
            return (count, true);
        }
    }

    private static int Signature(List<UnresolvedFile> files, int ownedCount, IReadOnlyList<string> importFolders)
    {
        var signature = new HashCode();
        foreach (var file in files)
        {
            signature.Add(file.Hash, StringComparer.OrdinalIgnoreCase);
            signature.Add(file.Paths.Count > 0 ? file.Paths[0] : string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        signature.Add(ownedCount);
        foreach (var folder in importFolders)
        {
            signature.Add(folder, StringComparer.OrdinalIgnoreCase);
        }

        return signature.ToHashCode();
    }
}
