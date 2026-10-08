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
/// 照らすのは有無と大きさと中身。日時は36のどれも合わなかった（展開した道具が展開した時刻を付ける）。
/// 中身は、zip が各ファイルに記録している CRC-32 と、フォルダの側のファイルから計算した CRC-32 で比べる（点検29：大きさの同じ書き換えを見逃していた）。
/// zip は解かずに済む（前は「中身まで見るには zip を全部解くことになる」と見送っていた）。代わりにフォルダの中を全部読むので、大きなフォルダでは時間がかかる。
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

        Dictionary<string, (long Length, uint Crc)> entries;
        try
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Read, ZipNameEncoding.Instance);
            entries = new Dictionary<string, (long Length, uint Crc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                entries.TryAdd(entry.FullName.Replace('/', Path.DirectorySeparatorChar), (entry.Length, entry.Crc32));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "展開元のzipを読めないため、中身を比べられません。削除しません。";
        }

        var extra = 0;
        var changed = 0;
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

                if (!entries.TryGetValue(Path.GetRelativePath(folderPath, file), out var expected))
                {
                    extra++;
                }
                else if (size != expected.Length || Crc32.OfFile(file, cancellationToken) != expected.Crc)
                {
                    // 大きさが同じでも中身が違えば、zip からは戻せない
                    changed++;
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
            : changed > 0
                ? $"展開元のzipと中身の違うファイルが {changed:N0} 件あります。展開した後に変えたものかもしれないので、削除しません。"
                : null;
    }
}
