namespace Chmonos.Core.Scanning;

/// <summary>
/// 商品に紐付けて登録済みのフォルダ。スキャン時に配下を丸ごと飛ばすために使う。
///
/// 登録は「このフォルダは既にあの商品のものだ」という宣言なので、
/// 中身を1件ずつ未確定へ流す必要がない。除外リストと似ているが、
/// 除外が「管理しない」なのに対し、こちらは「管理済み」である点が違う。
/// </summary>
public sealed class RegisteredFolderSet
{
    private readonly HashSet<string> _folders;

    public RegisteredFolderSet(IEnumerable<string> folders)
    {
        _folders = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _folders.Count;

    /// <summary>
    /// このパスが登録済みフォルダの中（またはそのもの）か。
    ///
    /// **パスの区切りごとの頭の部分を集合で引く**（2026-09-24）。前は登録したフォルダを1つずつ前方一致で比べていたので、
    /// 走査で見つけたファイルごとに登録の数だけ比べていた（ファイル数×登録数）。
    /// 区切りまで見て一致を判定するのは前と同じ（前方一致だけだと "rurune_v1" が "rurune_v1.1.3" を巻き込む）。
    /// </summary>
    public bool Contains(string path)
    {
        if (_folders.Count == 0)
        {
            return false;
        }

        var normalized = Normalize(path);
        if (_folders.Contains(normalized))
        {
            return true;
        }

        // 頭の部分は切り出さずに引く（ファイルごと・区切りごとに文字列を作らない）
        var lookup = _folders.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var index = normalized.IndexOf(Path.DirectorySeparatorChar);
             index >= 0;
             index = normalized.IndexOf(Path.DirectorySeparatorChar, index + 1))
        {
            if (lookup.Contains(normalized.AsSpan(0, index)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

    /// <summary>
    /// 登録済みフォルダに対応するアーカイブが、同じ場所に現れていないかを見る。
    ///
    /// フォルダ登録はzipが手元に無い場合の受け皿なので、zipが手に入ったら役目を終える。
    /// 放っておくと、zipの取り込みとフォルダ登録で容量が二重に乗ったままになる。
    /// 見つからなければ null。
    /// </summary>
    public static string? FindArchiveFor(string folderPath)
    {
        var normalized = Normalize(folderPath);
        var parent = Path.GetDirectoryName(normalized);
        var name = Path.GetFileName(normalized);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            var siblings = Directory.EnumerateFiles(parent).Select(Path.GetFileName).OfType<string>().ToList();
            var match = UnpackedFolderDetector.FindMatchingArchive(name, siblings);
            return match is null ? null : Path.Combine(parent, match);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>フォルダの中身を数える。ハッシュは計算しない（列挙して足すだけ）。</summary>
    public static (int FileCount, long TotalBytes) Measure(string folder)
    {
        try
        {
            var files = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            return (files.Count, files.Sum(file => file.Length));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }
}
