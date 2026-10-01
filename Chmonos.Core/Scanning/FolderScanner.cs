using System.IO.Enumeration;

namespace Chmonos.Core.Scanning;

/// <summary>フォルダ走査で見つかった1ファイル。</summary>
public sealed class ScannedFile
{
    public required string Path { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    /// <summary>小文字の拡張子（ドット付き）。</summary>
    public required string Extension { get; init; }

    /// <summary>BoothID解決の対象になるアーカイブか。zip以外はユーザに問い合わせる扱いになる。</summary>
    public bool IsArchive => Extension is ".zip";
}

/// <summary>
/// 取り込み対象フォルダの走査。ここではファイルを列挙するだけで、ハッシュもZIPの中身も見ない。
/// 重い処理を後段に分けているのは、3フェーズの進捗を正しく出すため。
/// </summary>
public sealed class FolderScanner
{
    /// <summary>
    /// 取り込み対象の拡張子。zip以外もリストに含めるのは、ユーザに紐付けを問い合わせるため。
    /// </summary>
    public static readonly IReadOnlySet<string> TargetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar",
        ".psd", ".ai", ".lip", ".pdf",
        ".mp3", ".m4a", ".wav", ".aif", ".aiff", ".flac",
        ".epub",
        ".vroid", ".vroidcustomitem", ".vrm", ".vrma",
        ".xwear", ".xavatar", ".xroid",
        ".jpg", ".jpeg", ".gif", ".png",
        ".mp4", ".mov", ".avi",
    };

    // Windows の属性で .NET の列挙に名前が無い物。オンラインのみのクラウドのファイルに付く
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>
    /// 手元に中身が無い（開くとダウンロードが始まる）クラウドのファイルか。
    ///
    /// **取り込まない。**取り込むとハッシュを取るために中身を読むので、OneDrive なら数十GBのダウンロードが
    /// 人の知らないうちに始まる。落とすかどうかはユーザが決める事で、今は手元にある物だけを読む
    /// （「常にこのデバイスに保持する」にすれば取り込める）。数は <see cref="ScanResult.OnlineOnly"/> に残す。
    /// </summary>
    public static bool IsOnlineOnly(FileAttributes attributes)
        => (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    /// <summary>
    /// ジャンクション・シンボリックリンクか（クラウドの再解析点は違う）。走査はこれだけを飛ばす。
    ///
    /// 再解析点は**種類を見て**飛ばす（点検 2026-09-23）。前は再解析点を全部飛ばしていたが、
    /// OneDrive（ファイル オンデマンド）は同期フォルダの中のフォルダもファイルも全部が再解析点なので、
    /// OneDrive に置いたアセットが1件も取り込まれていなかった（この機械の OneDrive で、フォルダも手元にあるファイルも
    /// ReparsePoint を持つことを確かめた）。
    /// 飛ばすのはジャンクションとシンボリックリンクだけ——自分の親を指せばループになり、別の場所を指せば同じファイルを二度読む。
    /// </summary>
    private static bool IsLink(ref FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            // LinkTarget は再解析点をたどらずに開くので、クラウドのファイルを落とさない（この機械で確かめた）
            return entry.ToFileSystemInfo().LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 種類が読めない物は、ループの元かもしれないので前どおり飛ばす
            return true;
        }
    }

    /// <summary>
    /// 指定フォルダ以下を再帰的に走査する。アクセスできないフォルダは飛ばして続行する
    /// （1つの権限エラーで全体を止めないため）。
    /// アーカイブの展開先とみなしたフォルダの中身は、取り込み対象から外して別に返す。
    /// </summary>
    public ScanResult Scan(string rootFolder, CancellationToken cancellationToken = default)
    {
        // ファイルが直接指定されたら、そのファイルだけを対象にする。
        // 親フォルダへ広げると、ダウンロードフォルダの1件を落としただけで
        // フォルダ全体が取り込み対象になってしまう。
        // 一時展開（#56）の中は取り込まない。閉じると消えるので、紐付けても「見つからない」になるだけ
        if (Services.TemporaryUnpacker.IsInsideTemporaryArea(rootFolder))
        {
            return new ScanResult();
        }

        if (File.Exists(rootFolder))
        {
            var missed = 0;
            var single = Describe(rootFolder, ref missed);
            return new ScanResult { Files = single is null ? [] : [single], Unreadable = missed };
        }

        if (!Directory.Exists(rootFolder))
        {
            return new ScanResult();
        }

        return ScanTree(rootFolder, cancellationToken);
    }

    /// <summary>1つのフォルダの中身1件（列挙で取れた物だけで足りる）。</summary>
    private readonly record struct Entry(string Path, string Name, bool IsDirectory, FileAttributes Attributes, long Length, DateTimeOffset LastWriteUtc, bool IsLink);

    /// <summary>走査の中で見つけた展開先。中のファイルの数と大きさは、木をたどりながら足していく。</summary>
    private sealed class UnpackedTally(string path, string archivePath)
    {
        public string Path { get; } = path;

        public string ArchivePath { get; } = archivePath;

        public int Count { get; set; }

        public long Bytes { get; set; }
    }

    /// <summary>
    /// **木を1回だけたどる**（2026-09-24）。前は ①フォルダを全部並べる ②フォルダごとに親のファイルを全部並べ直す
    /// ③展開先ごとに中を並べ直して測る ④ファイルを全部並べる、と同じ所を何度もたどり、
    /// 見つけたファイルごとに大きさと日時をファイルシステムへ問い直していた（作り物の1万ファイル・400フォルダで1回 0.3〜0.4秒・148MB）。
    ///
    /// 1つのフォルダを1回だけ並べ、その一覧から「子のフォルダが展開先か（兄弟のファイル名）」「取り込むファイル」「展開先の中の数と大きさ」を全部決める。
    /// **見つかる物は前と同じにする**：たどる順（幅優先・各フォルダの中はファイルシステムの返す順）・飛ばす物（システム属性・リンク）・
    /// 入れ子の展開先・展開先の中の数え方を、前の <c>FileSystemEnumerable</c> の再帰と揃えてある。
    /// 大きさと日時は列挙で取れた物を使う（前は1件ずつ <see cref="FileInfo"/> で問い直していた）。
    /// </summary>
    private static ScanResult ScanTree(string rootFolder, CancellationToken cancellationToken)
    {
        var files = new List<ScannedFile>();
        var unpacked = new List<UnpackedTally>();
        var skipped = 0;
        var onlineOnly = 0;
        var unreadableFolders = new List<string>();

        // 前の再帰の列挙と同じく、並べ終えたフォルダの子を後ろに積む（幅優先）。
        // 各フォルダは「どの展開先の中か」（外側から順に）を持って積む
        var pending = new Queue<(string Path, IReadOnlyList<UnpackedTally> Inside)>();
        pending.Enqueue((rootFolder, []));

        while (pending.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = List(current.Path, unreadableFolders);
            if (entries.Count == 0)
            {
                continue;
            }

            // 展開先かを見る兄弟のファイル名。前は Directory.GetFiles（属性で飛ばさない・リンクも入れる）で並べていたので、それと揃える
            var siblings = entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Name).ToList();

            foreach (var entry in entries)
            {
                // 前の走査（システム属性を飛ばす列挙）と同じく、システム属性の物は数えず降りない。リンクも降りない（ループの元）
                if ((entry.Attributes & FileAttributes.System) != 0 || entry.IsLink)
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    var inside = current.Inside;
                    if (UnpackedFolderDetector.FindMatchingArchive(entry.Name, siblings) is { } archive)
                    {
                        var tally = new UnpackedTally(entry.Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(entry.Path)!, archive));
                        unpacked.Add(tally);
                        inside = [.. inside, tally];
                    }

                    pending.Enqueue((entry.Path, inside));
                    continue;
                }

                // 展開先の中の数と大きさは、取り込む拡張子かに関わらず全部のファイルで数える（削除すれば空く量）
                foreach (var tally in current.Inside)
                {
                    tally.Count++;
                    tally.Bytes += entry.Length;
                }

                var extension = System.IO.Path.GetExtension(entry.Name);
                if (string.IsNullOrEmpty(extension) || !TargetExtensions.Contains(extension))
                {
                    continue;
                }

                if (current.Inside.Count > 0)
                {
                    skipped++;
                    continue;
                }

                if (IsOnlineOnly(entry.Attributes))
                {
                    onlineOnly++;
                    continue;
                }

                files.Add(new ScannedFile
                {
                    Path = entry.Path,
                    SizeBytes = entry.Length,
                    ModifiedAtUtc = entry.LastWriteUtc,
                    Extension = extension.ToLowerInvariant(),
                });
            }
        }

        return new ScanResult
        {
            Files = files,
            UnpackedFolders = [.. unpacked.Select(tally => new UnpackedFolder
            {
                Path = tally.Path,
                ArchivePath = tally.ArchivePath,
                FileCount = tally.Count,
                TotalBytes = tally.Bytes,
            })],
            SkippedInsideUnpackedFolders = skipped,
            OnlineOnly = onlineOnly,
            UnreadableFolders = unreadableFolders,
        };
    }

    /// <summary>
    /// 1つのフォルダの中を、ファイルシステムの返す順で1回だけ並べる。読めないフォルダ（権限・途中で消えた）は空として飛ばす
    /// （1つの権限エラーで全体を止めない）。
    ///
    /// **読めなかったフォルダは <paramref name="unreadable"/> に足す**（大容量の確かめ #5・2026-09-30）。
    /// 前は IgnoreInaccessible で権限の無いフォルダを黙って空にしていたので、取り込みの結果の「読めなかった」にも数えられず、
    /// ログにも残らなかった（読めないファイルは数えていたのに、フォルダごと読めないと何も言わない）。
    /// 途中で消えたフォルダは数えない——中の物は無くなっていて、取り込めていない物が無い。
    /// </summary>
    private static List<Entry> List(string directory, List<string> unreadable)
    {
        try
        {
            return [.. new FileSystemEnumerable<Entry>(
                directory,
                (ref FileSystemEntry entry) => new Entry(
                    entry.ToFullPath(),
                    entry.FileName.ToString(),
                    entry.IsDirectory,
                    entry.Attributes,
                    entry.Length,
                    entry.LastWriteTimeUtc,
                    IsLink(ref entry)),
                ListOptions)];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(directory);
            return [];
        }
    }

    /// <summary>
    /// 1段だけ並べる。属性では飛ばさない（兄弟のファイル名には全部要る。飛ばすのは <see cref="ScanTree"/> で決める）。
    /// 読めないフォルダで投げさせる（IgnoreInaccessible を入にすると、権限の無いフォルダが黙って空になり、数えられない）。
    /// 1段だけ並べるので、投げるのは並べようとしたそのフォルダが読めないときだけ。
    /// </summary>
    private static readonly EnumerationOptions ListOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    /// <summary>1ファイルを見て取り込み対象なら情報を返す。対象外・読めない場合は null（読めなかったものは数える）。</summary>
    private static ScannedFile? Describe(string path, ref int unreadable)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension) || !TargetExtensions.Contains(extension))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            return new ScannedFile
            {
                Path = path,
                SizeBytes = info.Length,
                ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                Extension = extension.ToLowerInvariant(),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 走査中に消えた・触れないファイルは飛ばすが、**数は残す**（E4：無言で消えていた）
            unreadable++;
            return null;
        }
    }
}

public sealed class ScanResult
{
    public IReadOnlyList<ScannedFile> Files { get; init; } = [];

    /// <summary>アーカイブの展開先とみなしたフォルダ。削除機能の対象候補にもなる。</summary>
    public IReadOnlyList<UnpackedFolder> UnpackedFolders { get; init; } = [];

    public int SkippedInsideUnpackedFolders { get; init; }

    /// <summary>
    /// 権限などで**読めなかった**ファイルの数（E4・ユーザ判断 2026-09-20）。
    /// 黙って飛ばすと、取り込んだつもりの物が入っていないのに気付けない。
    /// 1件ずつ言うと何千件も出るので、数だけを取り込みの結果に足す
    /// （`SkippedInsideUnpackedFolders` と同じ道）。
    /// </summary>
    public int Unreadable { get; init; }

    /// <summary>
    /// 権限などで中を並べられなかったフォルダ（大容量の確かめ #5）。ファイルと違って数件で済むので、パスを持ってログに残す。
    /// 中にいくつファイルがあったかは読めないので分からない——ファイルの数（<see cref="Unreadable"/>）とは別に数える。
    /// </summary>
    public IReadOnlyList<string> UnreadableFolders { get; init; } = [];

    /// <summary>
    /// 中身が手元に無いクラウドのファイル（OneDrive の「オンラインのみ」）で、読まなかった数。
    /// 読むとダウンロードが始まるので取り込まない（<see cref="FolderScanner.IsOnlineOnly"/>）。
    /// </summary>
    public int OnlineOnly { get; init; }
}
