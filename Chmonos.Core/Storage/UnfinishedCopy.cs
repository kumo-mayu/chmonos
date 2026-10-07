namespace Chmonos.Core.Storage;

/// <summary>何の写しか。</summary>
public enum UnfinishedCopyKind
{
    /// <summary>保存先の引越し（置き換えも）。</summary>
    Move,

    /// <summary>バックアップから戻す。</summary>
    Restore,
}

/// <summary>
/// 「写している途中」の印（<see cref="UnfinishedCopy.MarkerName"/>）の中身。写し先の直下に置く。
/// アプリを介さず開いた人にも、何の写しかけで、どう扱えばよいかが分かるように書く。
/// </summary>
public sealed record UnfinishedCopyMarker
{
    public required UnfinishedCopyKind Kind { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>写し元（引越しなら元の保存先、戻すなら zip）。</summary>
    public required string From { get; init; }

    /// <summary>写し先のフォルダを、写すために作ったか。片付けで空になったら畳む。</summary>
    public bool CreatedFolder { get; init; }

    /// <summary>置き換えで、元々あった物を退けたフォルダ（写し先からの相対）。片付けで元の場所へ戻す。</summary>
    public string? Parked { get; init; }

    /// <summary>写し始める前から写し先にあったファイルとフォルダ（写し先からの相対）。片付けで消さない。</summary>
    public IReadOnlyList<string> Existing { get; init; } = [];

    public string Note { get; init; } =
        "Chmonosがデータをコピーしている途中に置く印です。コピーし終えると消えます。"
        + "残っていれば途中で止まったコピーで、ライブラリとしては使えません。設定の「場所を変える」でこの場所を選ぶと削除できます。";
}

/// <summary>片付けの前に見せる中身。</summary>
public sealed record UnfinishedCopyPlan(
    UnfinishedCopyMarker? Marker, IReadOnlyList<string> Files, long Bytes, string? ParkedAt);

/// <summary>片付けた結果。<see cref="Left"/> が 0 なら、写しかけはもう無い（印も外した）。</summary>
public sealed record UnfinishedCopyCleanup(int Removed, int Left, bool ParkedRestored);

/// <summary>
/// 引越し・戻すの「写している途中」の印と、途中で止まった写しかけの片付け（実機の確かめ 2026-10-07）。
///
/// 途中で止まる（強制終了・電源）と、写し先に写しかけが残る。止めた・失敗したときの片付け（<see cref="StoreMover"/>・
/// <see cref="BackupArchive"/>）はプロセスが生きていないと走らない。前は印が無く、写しかけの場所を後で選ぶと
/// 「既にあるライブラリ」として出て、「選んだ場所のデータを使う」で商品の大半が欠けたライブラリへ切り替わった。
/// 写し始める前に印を置き、突き合わせが済んだら外す。印のある場所はライブラリとして扱わない。
///
/// 片付けは別のプロセスで行うので、何を写したかの控え（メモリの中）は無い。代わりに、写し始める前から在った物を印に書いておき、
/// それ以外を写しで作った物と見て消す。写し先は空か、置き換えで退けた後のはずなので、控えはふつう空か退けたフォルダ1つ
/// </summary>
public static class UnfinishedCopy
{
    /// <summary>印の名前。エクスプローラーで見た人が、何のファイルか分かる名前にする。</summary>
    public const string MarkerName = "_Chmonos-コピーの途中.json";

    public static string MarkerPath(string root) => Path.Combine(root, MarkerName);

    /// <summary>印があるか（中身が読めなくても、あれば写しかけと見る）。</summary>
    public static bool IsAt(string root) => File.Exists(MarkerPath(root));

    /// <summary>印を読む。無い・読めなければ null。</summary>
    public static UnfinishedCopyMarker? Read(string root)
    {
        try
        {
            return JsonStore.Read<UnfinishedCopyMarker>(MarkerPath(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 写し始める前に印を置く。写し先のフォルダは呼び手が作っておく。
    /// 写し始める前から在った物を控える（片付けで消さないため）。退けたフォルダの中は控えず、丸ごと残す
    /// </summary>
    internal static void Begin(
        string destination, UnfinishedCopyKind kind, string from, bool createdFolder, string? parked = null)
    {
        var parkedFull = parked is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(parked));
        var existing = Directory.Exists(destination)
            ? Directory.EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories)
                .Where(path => parkedFull is null || !IsSameOrUnder(path, parkedFull))
                .Select(path => Path.GetRelativePath(destination, path))
                .Where(relative => !string.Equals(relative, MarkerName, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

        JsonStore.WriteOutsideStore(MarkerPath(destination), new UnfinishedCopyMarker
        {
            Kind = kind,
            StartedAt = DateTimeOffset.Now,
            From = from,
            CreatedFolder = createdFolder,
            Parked = parkedFull is null ? null : Path.GetRelativePath(destination, parkedFull),
            Existing = existing,
        });
    }

    /// <summary>写し終えて突き合わせが済んだら外す。外せなければ投げる（写し終えた物が写しかけに見えたまま進めない）。</summary>
    internal static void End(string destination) => File.Delete(MarkerPath(destination));

    /// <summary>片付けで消す物を数える。印が読めなければ <see cref="UnfinishedCopyPlan.Marker"/> が null で、何も数えない。</summary>
    public static UnfinishedCopyPlan Plan(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Read(root) is not { } marker)
        {
            return new UnfinishedCopyPlan(null, [], 0, null);
        }

        var files = CopiedFiles(root, marker).ToList();
        var parked = ParkedFolder(root, marker);
        return new UnfinishedCopyPlan(
            marker,
            files,
            files.Sum(file => new FileInfo(file).Length),
            parked is not null && Directory.Exists(parked) ? parked : null);
    }

    /// <summary>
    /// 写しかけを片付ける。写しで作った物だけを消し、写し始める前から在った物には触れない。
    /// 置き換えで退けた物は元の場所へ戻す（退けたままだと、選んだ場所にあったライブラリが見えなくなる）。
    /// 全部消せたら印を外し、写すために作ったフォルダは空なら畳む。印が読めなければ何もしない（何が元から在ったか分からない）
    /// </summary>
    public static UnfinishedCopyCleanup Clean(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Read(root) is not { } marker)
        {
            return new UnfinishedCopyCleanup(0, 1, ParkedRestored: false);
        }

        var removed = 0;
        var left = 0;
        foreach (var file in CopiedFiles(root, marker).ToList())
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error("写しかけを片付ける", exception);
                left++;
            }
        }

        var kept = new HashSet<string>(marker.Existing, StringComparer.OrdinalIgnoreCase);
        var parked = ParkedFolder(root, marker);
        foreach (var folder in StoreTree.Directories(root).OrderByDescending(path => path.Length))
        {
            if ((parked is not null && IsSameOrUnder(folder, parked)) || kept.Contains(Path.GetRelativePath(root, folder)))
            {
                continue;
            }

            TryRemoveEmptyFolder(folder);
        }

        if (left > 0)
        {
            return new UnfinishedCopyCleanup(removed, left, ParkedRestored: false);
        }

        var restored = parked is not null && Directory.Exists(parked) && RestoreParked(root, parked);

        try
        {
            File.Delete(MarkerPath(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("写しかけの印を外す", exception);
            return new UnfinishedCopyCleanup(removed, 1, restored);
        }

        if (marker.CreatedFolder)
        {
            TryRemoveEmptyFolder(root);
        }

        return new UnfinishedCopyCleanup(removed, 0, restored);
    }

    /// <summary>退けた物を元の場所へ戻す。同じ名前の物が既にあれば、その1つは退けたまま残す。</summary>
    private static bool RestoreParked(string root, string parked)
    {
        var all = true;
        foreach (var entry in Directory.EnumerateFileSystemEntries(parked).ToList())
        {
            var target = Path.Combine(root, Path.GetFileName(entry));
            try
            {
                if (File.Exists(target) || Directory.Exists(target))
                {
                    all = false;
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, target);
                }
                else
                {
                    File.Move(entry, target);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error("置き換えで退けた物を戻す", exception);
                all = false;
            }
        }

        if (all)
        {
            TryRemoveEmptyFolder(parked);
        }

        return all;
    }

    /// <summary>写しで作ったと見るファイル：印・退けたフォルダ・写す前から在った物のほか全部。リンクの先へは降りない。</summary>
    private static IEnumerable<string> CopiedFiles(string root, UnfinishedCopyMarker marker)
    {
        var kept = new HashSet<string>(marker.Existing, StringComparer.OrdinalIgnoreCase);
        var parked = ParkedFolder(root, marker);
        var markerPath = MarkerPath(root);

        return StoreTree.Files(root).Where(file =>
            !string.Equals(file, markerPath, StringComparison.OrdinalIgnoreCase)
            && !(parked is not null && IsSameOrUnder(file, parked))
            && !kept.Contains(Path.GetRelativePath(root, file)));
    }

    private static string? ParkedFolder(string root, UnfinishedCopyMarker marker)
    {
        if (string.IsNullOrWhiteSpace(marker.Parked))
        {
            return null;
        }

        // 手で書き換えた印が写し先の外を指していたら、退けた物とは見ない（外の物を動かさない）
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, marker.Parked)));
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsSameOrUnder(string path, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(full, folder, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryRemoveEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 空のフォルダが残るだけ。印は外してあるので、ライブラリにも写しかけにも見えない
        }
    }
}
