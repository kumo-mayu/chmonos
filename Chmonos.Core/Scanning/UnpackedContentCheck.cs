using System.IO.Compression;
using BoothZipInspector;

namespace Chmonos.Core.Scanning;

/// <summary>
/// 展開先とみなしたフォルダの中身が、本当に展開元の zip と同じかを確かめる（消す直前だけ）。
///
/// 展開先の見分けは名前だけ（<see cref="UnpackedFolderDetector"/>）なので、展開した後に手を入れたフォルダも
/// 「zip が残っているから消してよい物」として並んでいた。2026-10-07 に手元のダウンロードのフォルダで測ると、
/// zip と同じ名前のフォルダ36のうち7つに zip に無いファイル（組んだ結果の .obj・.pdb、使ってできた .db・.log など）があり、
/// 1つは大きさの違うファイルがあった。消すとそれらは zip から戻せない。
///
/// 照らすのは有無と大きさだけ。日時は36のどれも合わなかった（展開した道具が展開した時刻を付ける）。
/// 中身のハッシュまでは見ない（大きさの同じ書き換えは見逃すが、zip を全部解くことになる）。
/// zip にあってフォルダに無い物は構わない（消した物は zip から戻せる）。
/// </summary>
public static class UnpackedContentCheck
{
    /// <summary>Windows が勝手に作るファイル。展開した後に手を入れた跡ではないので照らさない。</summary>
    private static readonly HashSet<string> MadeByWindows = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini" };

    /// <summary>消してはいけない理由。中身が zip と同じなら null。</summary>
    public static string? Refusal(string folderPath, string archivePath, CancellationToken cancellationToken = default)
    {
        // rar・7z は目録を読めないので比べられない。比べられない物は消さない
        if (!string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "zip以外は中身を比べられないため、削除しません。";
        }

        Dictionary<string, long> entries;
        try
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Read, ZipNameEncoding.Instance);
            entries = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                entries.TryAdd(entry.FullName.Replace('/', Path.DirectorySeparatorChar), entry.Length);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "展開元のzipを読めないため、中身を比べられません。削除しません。";
        }

        var extra = 0;
        var resized = 0;
        try
        {
            // 中にリンクがあれば消さない。zip を展開してリンクはできないので、後から作った物。
            // リンクの先は zip と関係の無い場所で、照らすと先の中まで読み続けてしまう（外部の点検 2026-10-07）
            if (Storage.StoreTree.FindLink(folderPath) is not null)
            {
                return "フォルダの中にほかの場所へのリンクがあるため、削除しません。";
            }

            // リンクの先へは降りない数え方（StoreTree）で照らす
            foreach (var (file, size) in Storage.StoreTree.FilesWithLength(folderPath, cancellationToken))
            {
                if (MadeByWindows.Contains(Path.GetFileName(file)))
                {
                    continue;
                }

                if (!entries.TryGetValue(Path.GetRelativePath(folderPath, file), out var length))
                {
                    extra++;
                }
                else if (size != length)
                {
                    resized++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 照らしの途中で消えた・読めないフォルダがあった・ドライブが外れた。そのフォルダは消さず、ほかのフォルダは続ける
            Diagnostics.AppLog.Error("展開したフォルダを zip と照らす", exception);
            return "フォルダの中を読めないため、中身を比べられません。削除しません。";
        }

        return extra > 0
            ? $"展開元のzipに無いファイルが {extra:N0} 件あります。展開した後に追加したものかもしれないので、削除しません。"
            : resized > 0
                ? $"展開元のzipと大きさの違うファイルが {resized:N0} 件あります。展開した後に変えたものかもしれないので、削除しません。"
                : null;
    }
}
