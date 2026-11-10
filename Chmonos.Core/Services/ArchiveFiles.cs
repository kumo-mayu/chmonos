namespace Chmonos.Core.Services;

/// <summary>
/// 書庫（配布の zip など）のファイルの見分けと、記録の場所から開ける zip を選ぶ式。
/// 前は拡張子の一覧と「最初に在る zip」の式を、取り込み・変換の読み取り・包みの控え・zip の絵・商品ページに写していた。
/// </summary>
public static class ArchiveFiles
{
    /// <summary>
    /// 書庫として扱う拡張子。展開済みのフォルダの見分けと、未確定の元 zip の割り出しが使う
    /// （BOOTH の配布物で見たことのある形。中を開けるのは zip だけ）。
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".zip", ".rar", ".7z"];

    public static bool IsArchive(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>中を開ける zip か（拡張子で見る）。</summary>
    public static bool IsZip(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 記録の場所のうち、最初に在る zip。無ければ null。
    /// 在るかの見方は呼ぶ側が選ぶ：画面から見る所はつながっていないドライブで待たない <see cref="DiskCheck.FileExists"/>、
    /// 裏の作業は <see cref="File.Exists(string?)"/>（既定）。
    /// </summary>
    public static string? FirstExistingZip(IEnumerable<string> paths, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return paths.FirstOrDefault(path => IsZip(path) && exists(path));
    }
}
