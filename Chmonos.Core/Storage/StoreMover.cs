namespace Chmonos.Core.Storage;

/// <summary>引越しの進み具合。件数だけで足りる（1件あたりは小さい）。</summary>
public readonly record struct StoreMoveProgress(int Copied, int Total, string CurrentName);

public sealed record StoreMoveResult
{
    public required bool Succeeded { get; init; }

    /// <summary>コピーできたファイル数。</summary>
    public required int Copied { get; init; }

    public required long Bytes { get; init; }

    /// <summary>失敗したときの理由。成功なら null。</summary>
    public string? Error { get; init; }

    /// <summary>元の場所を消せたか。消せていなければ、そこに残っている。</summary>
    public bool SourceRemoved { get; init; }

    /// <summary>置き換えたとき、元々あったライブラリを退けた場所。消していないので後から戻せる。</summary>
    public string? ParkedAt { get; init; }

    /// <summary>
    /// 失敗・中断したとき、運ぶ先に途中のコピーを消しきれずに残した場所。消せた（または何も書いていない）なら null。
    /// </summary>
    public string? LeftoverAt { get; init; }
}

/// <summary>ライブラリの姿。どちらを残すか決めてもらうために出す。</summary>
public sealed record StoreSummary
{
    public required int Files { get; init; }

    public required long Bytes { get; init; }

    /// <summary>いちばん新しいファイルの更新日時。中身が無ければ null。</summary>
    public required DateTime? LastWrite { get; init; }
}

/// <summary>
/// 保存先の引越し。
///
/// 「コピー → 検証 → 元を消す」の順で行う。途中で失敗しても元が消えていないので、
/// 呼び出し側は保存先を古いままにしておけば何も失われない。
/// 移動（Directory.Move）を使わないのは、ドライブをまたぐと使えないことと、
/// 途中で失敗したときに半分だけ移った状態になるため。
/// </summary>
public static class StoreMover
{
    /// <summary>数えるときも運ぶときも、この名前は除く（実行中のロックは持ち出せない。写している途中の印は運ぶ先が自分で置く）。</summary>
    private static readonly string[] Skipped = ["app.lock", UnfinishedCopy.MarkerName];

    /// <summary>運ぶ量を先に測る。確認のダイアログに出す。</summary>
    public static (int Files, long Bytes) Measure(string root)
    {
        var summary = Summarize(root);
        return (summary.Files, summary.Bytes);
    }

    /// <summary>
    /// そのライブラリの姿。2つを見比べてもらうために使う。
    ///
    /// 件数だけでは「どちらが新しいか」は分からない。少ない方が新しいこともある
    /// （消して作り直した直後など）ので、最終更新も一緒に出す。
    /// </summary>
    public static StoreSummary Summarize(string root)
    {
        if (!Directory.Exists(root))
        {
            return new StoreSummary { Files = 0, Bytes = 0, LastWrite = null };
        }

        var files = Enumerate(root).Select(path => new FileInfo(path)).ToList();

        return new StoreSummary
        {
            Files = files.Count,
            Bytes = files.Sum(file => file.Length),
            LastWrite = files.Count == 0 ? null : files.Max(file => file.LastWriteTime),
        };
    }

    /// <summary>
    /// 選んだ場所にあるライブラリを退けてから、今のデータを入れる。
    ///
    /// 消してからコピーすると、途中で失敗したときに両方失う。
    /// 退避したものは消さずに残すので、入れ替えた後で中身を確かめてから捨てられる。
    /// </summary>
    public static StoreMoveResult Replace(
        string source,
        string destination,
        IProgress<StoreMoveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action? commit = null)
    {
        // 今の保存先が選んだ先の内側にあると、下で選んだ先の中身を退けるときに**今の保存先ごと退けてしまう**。
        // 運ぶ元が消えるので、空のまま進むか元を消す所で落ちていた（試験で確かめた）。始める前に断る
        if (Overlap(source, destination) is { } refusal)
        {
            return Refused(refusal);
        }

        // 退ける前に断る。Move の中で断ると、選んだ先の中身を退けたまま返ってしまう
        if (LinkInside(source, destination) is { } linked)
        {
            return Refused(linked);
        }

        // 写しかけを退けると、退けた中に写しかけの印ごと埋もれ、元々あった物との見分けが付かなくなる。先に片付けてもらう
        if (UnfinishedCopy.IsAt(destination))
        {
            return Refused(UnfinishedRefusal);
        }

        var parked = Path.Combine(destination, $"_置き換え前-{DateTime.Now:yyyyMMdd-HHmmss}");

        try
        {
            Directory.CreateDirectory(parked);

            foreach (var entry in Directory.EnumerateFileSystemEntries(destination))
            {
                if (string.Equals(entry, parked, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.Move(entry, Path.Combine(parked, Path.GetFileName(entry)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("置き換えで選んだ場所を退ける", exception);
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = 0,
                Bytes = 0,
                Error = $"選んだ場所のデータを移動できませんでした。{Services.FailureText.Cause(exception)}",
            };
        }

        return MoveCore(source, destination, progress, cancellationToken, commit, parked) with { ParkedAt = parked };
    }

    /// <summary>写しかけの場所へ写そうとしたときの文。</summary>
    public const string UnfinishedRefusal =
        "選んだ場所は、前の引越しかバックアップから戻す途中で止まったコピーです。途中のコピーを削除してから、もう一度選んでください。";

    /// <param name="commit">
    /// 突き合わせが済んでから、元を消す前に呼ぶ（呼び手はここで <c>location.json</c> を書き換える）。投げたら、運んだ物を消して失敗で返す。
    /// 元を消した後に書き換えて失敗すると、データは新しい場所にあるのに、開き直すと空になった古い場所が開いた
    /// （置き場を差し替えて試験で通し、2026-10-06 に見つけた）。消す前なら、失敗しても元のまま続けられる
    /// </param>
    public static StoreMoveResult Move(
        string source,
        string destination,
        IProgress<StoreMoveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action? commit = null)
        => MoveCore(source, destination, progress, cancellationToken, commit, parked: null);

    private static StoreMoveResult MoveCore(
        string source,
        string destination,
        IProgress<StoreMoveProgress>? progress,
        CancellationToken cancellationToken,
        Action? commit,
        string? parked)
    {
        // 運ぶ先が今の保存先の内側（または別名で同じ実体）だと、運んだ物がまた運ぶ元に数えられ、
        // 同じ実体なら失敗の片付けが元のファイルを消す。文字だけでなく実体で比べる（FolderIdentity）
        if (FolderIdentity.IsSameOrInside(destination, source))
        {
            return Refused("選んだ場所が今の保存先の中にあります。今の保存先の外の場所を選んでください。");
        }

        if (LinkInside(source, destination) is { } linked)
        {
            return Refused(linked);
        }

        // 印を置き直すと、前の写しかけを「写す前から在った物」と控えてしまい、片付けで消せなくなる
        if (parked is null && UnfinishedCopy.IsAt(destination))
        {
            return Refused(UnfinishedRefusal);
        }

        var files = Enumerate(source).ToList();

        // 運ぶ先に同じ名前の物が既にあれば始めない。上書きすると、失敗したときにその物の中身は戻せない
        // （運ぶ先は空か、置き換えで退けた後のはず。そうでない呼び方を通さない）
        if (files.Select(file => Path.GetRelativePath(source, file))
                .FirstOrDefault(relative => File.Exists(Path.Combine(destination, relative))) is { } collision)
        {
            return Refused($"選んだ場所に同じ名前のファイルがあります：{collision}");
        }

        var copied = 0;
        var bytes = 0L;

        // 止めた・失敗したときに消すため、**ここで新しく作った**ファイルとフォルダを控える。
        // 前から在った物は控えない（消す相手にしない）
        var written = new List<string>();
        var createdFolders = new List<string>();
        var createdDestination = !Directory.Exists(destination);

        // 元を消す前に、運んだ時から元が変わっていないかを見る（ほかの書き手が書いた入力を消さない）
        var snapshots = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);

        try
        {
            CreateFolder(destination, createdFolders);

            // 写している途中の印。途中でプロセスごと止まると下の片付けは走らず、写しかけが「既にあるライブラリ」に見えていた
            // （実機の確かめ 2026-10-07）。印のある場所はライブラリとして扱わない（StoreLocation.LooksLikeStore）
            UnfinishedCopy.Begin(destination, UnfinishedCopyKind.Move, source, createdDestination, parked);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);

                CreateFolder(Path.GetDirectoryName(target)!, createdFolders);

                // 写す前の姿を控える。写した後に変わった物は、突き合わせで落とすか、元を消さずに残す
                snapshots[file] = FileStamp.Of(file);

                // コピーの途中で落ちると書きかけが残るので、書く前に控える。上書きはしない（上で無いと確かめた）
                written.Add(target);
                try
                {
                    File.Copy(file, target, overwrite: false);
                }
                catch (IOException exception) when ((exception.HResult & 0xFFFF) == FileExists)
                {
                    // 確かめた後に誰かが同じ名前で置いた。その物はこちらが作っていないので、片付けで消さない
                    written.RemoveAt(written.Count - 1);
                    throw;
                }

                copied++;
                bytes += new FileInfo(file).Length;
                progress?.Report(new StoreMoveProgress(copied, files.Count, Path.GetFileName(file)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // 元には手を付けていないので、保存先を古いままにすれば何も失われない
            if (exception is not OperationCanceledException)
            {
                Diagnostics.AppLog.Error("保存先を運ぶ", exception);
            }

            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = exception is OperationCanceledException ? "中断しました。" : Services.FailureText.Cause(exception),
                LeftoverAt = RemoveCopies(destination, written, createdFolders, createdDestination),
            };
        }

        // 突き合わせ。1件ずつ**中身まで**比べる（外部の点検 2026-10-06）。
        // 前は在るかと大きさだけで、運んでいる間にほかの書き手が同じ大きさで書き換えた入力を見逃し、その後で元を消していた。
        // 重さは docs/spec/architecture.md「保存先の引越し」に測った数を置く
        string? mismatch;
        try
        {
            mismatch = Verify(source, destination, snapshots, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (exception is not OperationCanceledException)
            {
                Diagnostics.AppLog.Error("保存先を運んだ後の突き合わせ", exception);
            }

            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = exception is OperationCanceledException ? "中断しました。" : Services.FailureText.Cause(exception),
                LeftoverAt = RemoveCopies(destination, written, createdFolders, createdDestination),
            };
        }

        if (mismatch is not null)
        {
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = $"コピーの確認に失敗しました：{mismatch}",
                LeftoverAt = RemoveCopies(destination, written, createdFolders, createdDestination),
            };
        }

        // 印は場所を書き換える前に外す。書き換えた後に外して落ちると、保存先が写しかけに見える場所を指したまま残る。
        // 外した後で止まっても、運ぶ先は突き合わせの済んだ完全な写しなので、ライブラリに見えてよい
        try
        {
            UnfinishedCopy.End(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("写している途中の印を外す", exception);
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = Services.FailureText.Cause(exception),
                LeftoverAt = RemoveCopies(destination, written, createdFolders, createdDestination),
            };
        }

        if (commit is not null)
        {
            try
            {
                commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Diagnostics.AppLog.Error("引越しの後に保存先の場所を覚える", exception);
                return new StoreMoveResult
                {
                    Succeeded = false,
                    Copied = copied,
                    Bytes = bytes,
                    Error = $"新しい保存先の場所を記録できませんでした。{Services.FailureText.Cause(exception)}",
                    LeftoverAt = RemoveCopies(destination, written, createdFolders, createdDestination),
                };
            }
        }

        return new StoreMoveResult
        {
            Succeeded = true,
            Copied = copied,
            Bytes = bytes,
            SourceRemoved = TryRemoveSource(source, files, snapshots),
        };
    }

    /// <summary>
    /// 止めた・失敗した引越しの途中のコピーを消す（ユーザ判断 2026-10-01）。
    /// 残すと、次に同じ場所を選んだときに「既にあるライブラリ」に見え、「選んだ場所のデータを使う」で
    /// 半分しか無いライブラリへ切り替えられた（作り物の2GBで確かめた。docs/research/store-transfer-2026-10-01.md）。
    /// 消すのは**ここで作った**ファイルと、ここで作って空になったフォルダだけ。前から在った物・置き換えで退けた物（_置き換え前-…）には触れない。
    /// </summary>
    /// <returns>消しきれずに残した場所。全部消せたら null。</returns>
    private static string? RemoveCopies(
        string destination, IReadOnlyList<string> written, IReadOnlyList<string> createdFolders, bool createdDestination)
    {
        var allRemoved = true;
        foreach (var file in written)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        // 写している途中の印は、写しを全部消せたときだけ外す。消し残しがあれば、印を残して写しかけだと分かるようにする
        if (allRemoved)
        {
            try
            {
                if (File.Exists(UnfinishedCopy.MarkerPath(destination)))
                {
                    UnfinishedCopy.End(destination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        // 作ったフォルダを深い方から畳む。中に別の物があれば残る（空のときだけ消す）
        foreach (var folder in createdFolders.OrderByDescending(folder => folder.Length))
        {
            if (createdDestination || !string.Equals(folder, destination, StringComparison.OrdinalIgnoreCase))
            {
                TryRemoveEmptyFolder(folder);
            }
        }

        return allRemoved ? null : destination;
    }

    /// <summary>フォルダを作り、無かった祖先を（浅い方から）控える。前から在ったフォルダは控えない。</summary>
    internal static void CreateFolder(string folder, List<string> created)
    {
        var missing = new Stack<string>();
        for (var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
             !string.IsNullOrEmpty(current) && !Directory.Exists(current);
             current = Path.GetDirectoryName(current))
        {
            missing.Push(current);
        }

        foreach (var path in missing)
        {
            Directory.CreateDirectory(path);
            created.Add(path);
        }
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
            // 空のフォルダが残るだけ。ライブラリには見えないので、引越しの結果には響かない
        }
    }

    private static StoreMoveResult Refused(string error)
        => new() { Succeeded = false, Copied = 0, Bytes = 0, Error = error };

    /// <summary>
    /// 置き換えで、2つの場所が重なっていれば断る理由を返す。
    /// 別名（ジャンクションなど）で同じ実体を指す場所も重なりと見る（<see cref="FolderIdentity"/>）——
    /// 文字だけで見ていたので、選んだ先の中身を退ける所で今の保存先の中身ごと退けていた
    /// </summary>
    private static string? Overlap(string source, string destination)
        => FolderIdentity.IsSameOrInside(source, destination)
            ? "今の保存先が、選んだ場所の中にあります。別の場所を選んでください。"
            : FolderIdentity.IsSameOrInside(destination, source)
                ? "選んだ場所が今の保存先の中にあります。今の保存先の外の場所を選んでください。"
                : null;

    /// <summary>
    /// 突き合わせない物。<c>logs/</c> は運んでいる間も書き足される（ログは引越しの門を通らない——
    /// 書けなくても投げない決まりで、待たせる相手でもない）。大きさが食い違って
    /// 「コピーは済んでいるのに確認に失敗」になっていた。運ぶ・消すのは他と同じ（食い違うのは最後の数行だけ）。
    /// </summary>
    private static bool IsUnverified(string relative)
        => relative.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 全部揃っていれば null、足りない・違えばその名前を返す。
    /// 運んだ後に元へ増えた物（列挙の後に書かれた物）は運ぶ先に無いので、ここで落ちる。
    /// </summary>
    private static string? Verify(
        string source, string destination, IReadOnlyDictionary<string, FileStamp> snapshots, CancellationToken cancellationToken)
    {
        foreach (var file in Enumerate(source))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(source, file);
            if (IsUnverified(relative))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);

            if (!File.Exists(target) || !snapshots.ContainsKey(file))
            {
                return relative;
            }

            if (FileStamp.Of(file) != snapshots[file])
            {
                return relative + "（運んでいる間に書き換えられました）";
            }

            // 中身まで比べるのは画像の外（人が入れた記録・設定・改変など）。画像は大きさで見る。
            // 書いたばかりのファイルを初めて読むのは遅く（おそらくウイルス対策の検査）、22,021ファイル・692MB の作り物で
            // 全部を比べると 103秒、写すのは 15秒だった。画像は BOOTH から取り直せ、数で9割を占める。
            // ほかの書き手が書いた入力は、中身を読まずに上の姿の比べで拾う
            if (IsImage(relative)
                    ? new FileInfo(target).Length != new FileInfo(file).Length
                    : !SameContent(file, target))
            {
                return relative + "（中身が違います）";
            }
        }

        return null;
    }

    private static bool IsImage(string relative)
        => relative.StartsWith("images" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 中身が同じか。ハッシュを取らずに並べて比べる——両方を1回ずつ読むだけで済み、ハッシュの計算の分だけ速い。
    /// 大きさが違えば読まずに違うと返す。
    /// </summary>
    internal static bool SameContent(string left, string right)
    {
        const int BufferSize = 1 << 20;

        using var a = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        using var b = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        if (a.Length != b.Length)
        {
            return false;
        }

        var bufferA = new byte[Math.Min(BufferSize, Math.Max(1, a.Length))];
        var bufferB = new byte[bufferA.Length];
        while (true)
        {
            var readA = a.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            var readB = b.ReadAtLeast(bufferB, bufferB.Length, throwOnEndOfStream: false);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
            {
                return false;
            }

            if (readA == 0)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// 元を消す。運んだファイルだけを消し、空になったフォルダも畳む。
    /// **運んだ時から変わった物は消さない**（ほかの書き手が突き合わせの後に書いた入力。消すとどこにも残らない）。
    /// <c>app.lock</c> は実行中のこのプロセスが握っているので運ばず、消さない（プロセスが閉じると OS が消す）。
    /// </summary>
    private static bool TryRemoveSource(
        string source, IReadOnlyList<string> files, IReadOnlyDictionary<string, FileStamp> snapshots)
    {
        var allRemoved = true;

        foreach (var file in files)
        {
            try
            {
                var relative = Path.GetRelativePath(source, file);
                if (!IsUnverified(relative) && snapshots.TryGetValue(file, out var stamp) && FileStamp.Of(file) != stamp)
                {
                    allRemoved = false;
                    continue;
                }

                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        // 深い方から畳む
        // リンクのフォルダには降りない（StoreTree）。先の外のフォルダを畳まない
        foreach (var directory in StoreTree.Directories(source)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        return allRemoved;
    }

    /// <summary>
    /// 保存先の中にリンクがあれば、断る文を返す（ユーザ判断「A」2026-10-06）。
    /// 辿って運ぶと、保存先の外のファイルを写したうえで消していた。辿らずに運ぶと、リンクの先の中身は新しい場所に無い。
    /// どちらも黙って進めず、外すか戻すかを使う人に決めてもらう。
    /// 運ぶ先の中のリンクも断る——写した物がリンクを通って選んだ場所の外へ書かれ、失敗の片付けもそこを消す
    /// </summary>
    private static string? LinkInside(string source, string destination)
    {
        if (StoreTree.FindLink(source) is { } inSource)
        {
            return StoreTree.LinkRefusal(inSource);
        }

        return StoreTree.FindLink(destination) is { } inDestination
            ? $"選んだ場所の中の「{inDestination}」は、ほかの場所を指すリンクです。リンクの無い場所を選んでください。"
            : null;
    }

    private static IEnumerable<string> Enumerate(string root)
        => Directory.Exists(root)
            ? StoreTree.Files(root)
                .Where(file => !Skipped.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            : [];

    /// <summary>ERROR_FILE_EXISTS。上書きしないコピーが、同じ名前の物に当たったとき。</summary>
    private const int FileExists = 0x50;

    /// <summary>ファイルの姿（大きさと更新日時）。写した後に変わったかを、読まずに見る。</summary>
    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
    {
        public static FileStamp Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : new FileStamp(-1, DateTime.MinValue);
        }
    }
}
