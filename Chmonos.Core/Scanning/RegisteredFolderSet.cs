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

    /// <summary>
    /// フォルダの中身を数える。ハッシュは計算しない（列挙して足すだけ）。中を読めなかったら null（0件として保存しないため）。
    /// </summary>
    public static (int FileCount, long TotalBytes)? Measure(string folder, CancellationToken cancellationToken = default)
        => Survey(folder, cancellationToken) is { } survey ? (survey.FileCount, survey.TotalBytes) : null;

    /// <summary>
    /// <see cref="Measure"/> と同じ1回の列挙で、中の <c>.unitypackage</c> の場所も拾う（<see cref="Models.LocalFolderRecord.UnityPackages"/>）。
    /// 場所はフォルダからの相対で、区切りは <c>/</c>（zip の中の場所と同じ書き方。送る道が同じ形で扱える）。
    /// 数は zip の中と同じ上限で切る（<see cref="Services.UnityHandoff.MaxPackages"/>。壊れた物や別物で一覧が埋まらないように）。
    /// </summary>
    /// <remarks>
    /// **リンク（ジャンクション・シンボリックリンク）の先には降りない**（<see cref="Storage.StoreTree"/>。外部の点検 2026-10-06）。
    /// 前は <see cref="SearchOption.AllDirectories"/> で先まで降り、自分や祖先を指すリンクで輪になり、ドライブの根を指すリンクで
    /// ドライブ中を数えて画面が止まった。登録の外の物を数えると容量も二重になる。
    /// 途中で読めなくなったら null を返す。前は途中の値を捨てて 0件・0バイト・空の一覧を返し、それをそのまま記録に書いていた
    /// （数え直しで、それまでの正しい値が 0 に置き換わる）。取り消しは <see cref="OperationCanceledException"/> で抜ける。
    /// </remarks>
    public static FolderSurvey? Survey(string folder, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.GetFullPath(folder);
            var count = 0;
            long bytes = 0;
            var packages = new List<string>();
            foreach (var (path, length) in Storage.StoreTree.FilesWithLength(root, cancellationToken))
            {
                count++;
                bytes += length;
                if (packages.Count < Services.UnityHandoff.MaxPackages
                    && path.EndsWith(Services.UnityHandoff.PackageExtension, StringComparison.OrdinalIgnoreCase))
                {
                    packages.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                }
            }

            // 並びはファイルシステムの返す順で揺れるので、場所の順にそろえる（数え直すたびに記録が書き換わらないように）
            packages.Sort(StringComparer.OrdinalIgnoreCase);
            return new FolderSurvey(count, bytes, packages);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
/// <summary>登録したフォルダを数えた結果（<see cref="RegisteredFolderSet.Survey"/>）。</summary>
public sealed record FolderSurvey(int FileCount, long TotalBytes, IReadOnlyList<string> UnityPackages)
{
    /// <summary>記録の中の unitypackage の一覧と同じか（数え直して変わったときだけ書くため）。</summary>
    public bool SamePackages(IReadOnlyList<string>? recorded)
        => (recorded ?? []).SequenceEqual(UnityPackages, StringComparer.Ordinal);
}
