using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>見つからない登録フォルダと、移した先かもしれないフォルダの合い方。強い順に並べてある。</summary>
public enum FolderMatchKind
{
    /// <summary>名前・ファイル数・合計の大きさが同じ。</summary>
    NameAndContents,

    /// <summary>名前は違うが、ファイル数と合計の大きさが同じ（移して名前も変えた）。</summary>
    Contents,

    /// <summary>名前だけ同じ（中のファイルが増えた・減った）。</summary>
    Name,
}

/// <summary>登録フォルダの場所を差し替えた結果（<c>ItemService.RelocateFolderAsync</c>）。</summary>
public enum FolderRelocation
{
    Moved,

    /// <summary>選んだ場所がもう無い。</summary>
    TargetMissing,

    /// <summary>選んだ場所（またはその外側）をほかの商品が登録している。</summary>
    RegisteredElsewhere,

    /// <summary>探してから選ぶまでの間に、元の登録が外された・商品が消された。</summary>
    RecordGone,

    /// <summary>選んだ場所の中を読めなかった（権限が無い・途中で外された）。0件として書かない。</summary>
    Unreadable,
}

/// <summary>移した先かもしれないフォルダ1つ。</summary>
public sealed record FolderCandidate(string Path, int? FileCount, long? TotalBytes, FolderMatchKind Kind);

/// <summary>見つからない登録フォルダ1つと、その候補。</summary>
public sealed record MissingFolder
{
    public required string ItemId { get; init; }

    public required string ItemName { get; init; }

    /// <summary>記録の場所（無くなった場所）。</summary>
    public required string Path { get; init; }

    public int FileCount { get; init; }

    public long TotalBytes { get; init; }

    /// <summary>強い順。空なら探した中には無かった。</summary>
    public IReadOnlyList<FolderCandidate> Candidates { get; init; } = [];
}

/// <summary>数えたフォルダ1つ。中を読めなかった（下のどこかを含む）ときは数が null。</summary>
public readonly record struct MeasuredFolder(string Path, int? FileCount, long? TotalBytes);

/// <summary>
/// 登録したフォルダを移したときの候補を決める（見つからない・移動の点検 10-A・ユーザ判断 2026-10-05）。
/// </summary>
/// <remarks>
/// フォルダはハッシュを持たない（中の1ファイルで別物になる。<see cref="LocalFolderRecord"/>）ので、ファイルのように中身で同じ物とは言えない。
/// 手掛かりは記録にある名前・ファイル数・合計の大きさだけなので、**合ったら候補として見せ、人が選んだときだけ差し替える**
/// （CLAUDE.md「推定した値を勝手に入れない」）。
/// 名前だけ合う物も出す：展開し直して中のファイルが増えた・減った物は数が合わないが、人が見れば同じ物と分かる。
/// 数だけ合う物も出す：移すついでに名前を変えた物。どちらも合わない物は出さない（名前も数も違えば、人にも見分ける手掛かりが無い）。
/// </remarks>
public static class MovedFolderCandidates
{
    /// <summary>
    /// 1つの登録フォルダに見せる候補の上限。「Textures」のようなありふれた名前だと名前だけ合う物が並びすぎる。
    /// 強い順に並べるので、本命は上に来る。
    /// </summary>
    public const int MaxPerFolder = 5;

    /// <summary>
    /// 登録の数え方（<see cref="Scanning.RegisteredFolderSet.Measure"/>：隠し・システムの属性の物も含め、下の全部のファイル）に合わせる。
    /// 違う数え方をすると、移しただけのフォルダが数で合わなくなる。
    /// </summary>
    private static readonly EnumerationOptions TopOnly = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        MatchType = MatchType.Win32,
    };

    /// <summary>合い方。合わなければ null。</summary>
    /// <param name="fileCount">候補のファイル数。中を読めなかったなら null（名前でだけ合わせる）。</param>
    public static FolderMatchKind? Match(LocalFolderRecord record, string candidatePath, int? fileCount, long? totalBytes)
    {
        var sameName = string.Equals(NameOf(record.Path), NameOf(candidatePath), StringComparison.OrdinalIgnoreCase);

        // 数えた物が空の記録（登録のときに読めなかった）は数で合わせない。空のフォルダがみな候補になる
        var sameContents = record.FileCount > 0
            && fileCount == record.FileCount
            && totalBytes == record.TotalBytes;

        return (sameName, sameContents) switch
        {
            (true, true) => FolderMatchKind.NameAndContents,
            (false, true) => FolderMatchKind.Contents,
            (true, false) => FolderMatchKind.Name,
            _ => null,
        };
    }

    /// <summary>
    /// <paramref name="root"/> とその下のフォルダを全部、中のファイル数と合計の大きさを添えて返す。木を1回だけたどる（下から足し上げる）。
    /// </summary>
    /// <remarks>
    /// フォルダごとに <see cref="Scanning.RegisteredFolderSet.Measure"/> を呼ぶと、深さの分だけ同じファイルを数え直す。
    /// ジャンクション・シンボリックリンクには降りない（登録の数え方も降りない。輪になり得る）。
    /// </remarks>
    public static List<MeasuredFolder> MeasureTree(string root, CancellationToken cancellationToken)
    {
        var result = new List<MeasuredFolder>();
        Visit(new DirectoryInfo(Path.TrimEndingDirectorySeparator(root)), result, cancellationToken);
        return result;
    }

    private static (int? Count, long? Bytes) Visit(DirectoryInfo folder, List<MeasuredFolder> result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = 0;
        var bytes = 0L;
        var known = true;
        try
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", TopOnly))
            {
                if (entry is FileInfo file)
                {
                    count++;
                    bytes += file.Length;
                }
                else if (entry is DirectoryInfo child && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var (childCount, childBytes) = Visit(child, result, cancellationToken);
                    if (childCount is { } c && childBytes is { } b)
                    {
                        count += c;
                        bytes += b;
                    }
                    else
                    {
                        known = false;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 下のどこかを読めなければ数は分からない（数で合わせず、名前でだけ合わせる）
            known = false;
        }

        var measured = known ? ((int?)count, (long?)bytes) : (null, null);
        result.Add(new MeasuredFolder(folder.FullName, measured.Item1, measured.Item2));
        return measured;
    }

    /// <summary>
    /// 数えたフォルダから候補を強い順に選ぶ（<see cref="MaxPerFolder"/> まで）。
    /// <paramref name="isRegistered"/> が真の場所（ほかの登録そのものとその中）は候補にしない——選ぶと2つの登録が同じ場所を指す。
    /// </summary>
    /// <remarks>
    /// **包んでいるだけのフォルダは数で合わせない。**移したフォルダの親が、ほかにファイルを持たなければ、親も同じ数になる
    /// （「移した先\sub\costume_v1」なら sub も 3件・600バイト）。下に同じ数のフォルダがあるなら、そちらが本物なので親は出さない。
    /// 名前が合う物は残す（展開で同じ名前が二重になったフォルダは、どちらを登録していたか人が選ぶ）。
    /// </remarks>
    public static IReadOnlyList<FolderCandidate> Pick(
        LocalFolderRecord record, IReadOnlyCollection<MeasuredFolder> folders, Func<string, bool> isRegistered)
    {
        var sameCounts = folders
            .Where(folder => folder.FileCount == record.FileCount && folder.TotalBytes == record.TotalBytes)
            .Select(folder => Path.TrimEndingDirectorySeparator(folder.Path))
            .ToList();

        bool WrapsAnother(string path)
        {
            var prefix = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
            return sameCounts.Any(other => other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        return [.. folders
            .Select(folder => Match(record, folder.Path, folder.FileCount, folder.TotalBytes) is { } kind
                ? new FolderCandidate(folder.Path, folder.FileCount, folder.TotalBytes, kind)
                : null)
            .OfType<FolderCandidate>()
            .Where(candidate => !(candidate.Kind == FolderMatchKind.Contents && WrapsAnother(candidate.Path)))
            .Where(candidate => !isRegistered(candidate.Path))
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPerFolder)];
    }

    private static string NameOf(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
}
