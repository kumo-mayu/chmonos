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
    /// <summary>数えるときも運ぶときも、この名前は除く（実行中のロックは持ち出せない）。</summary>
    private static readonly string[] Skipped = ["app.lock"];

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
        CancellationToken cancellationToken = default)
    {
        // 今の保存先が選んだ先の内側にあると、下で選んだ先の中身を退けるときに**今の保存先ごと退けてしまう**。
        // 運ぶ元が消えるので、空のまま進むか元を消す所で落ちていた（試験で確かめた）。始める前に断る
        if (Overlap(source, destination) is { } refusal)
        {
            return Refused(refusal);
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

        return Move(source, destination, progress, cancellationToken) with { ParkedAt = parked };
    }

    public static StoreMoveResult Move(
        string source,
        string destination,
        IProgress<StoreMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 運ぶ先が今の保存先の内側だと、運んだ物がまた運ぶ元に数えられ、突き合わせで必ず落ちる
        if (IsSameOrInside(destination, source))
        {
            return Refused("選んだ場所が今の保存先の中にあります。今の保存先の外の場所を選んでください。");
        }

        var files = Enumerate(source).ToList();
        var copied = 0;
        var bytes = 0L;

        // 止めた・失敗したときに消すため、書いたファイルを控える。
        // 運ぶ先は空か、置き換えで元の物を退けた後なので、消すのはここで書いた物だけになる
        var written = new List<string>();
        var createdDestination = !Directory.Exists(destination);

        try
        {
            Directory.CreateDirectory(destination);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // コピーの途中で落ちると書きかけが残るので、書く前に控える
                written.Add(target);
                File.Copy(file, target, overwrite: true);

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
                LeftoverAt = RemoveCopies(destination, written, createdDestination),
            };
        }

        // 検証。1件ずつ大きさを突き合わせる。
        // ハッシュまで取ると数GBで現実的な時間に収まらないので、件数と大きさで見る
        if (Verify(source, destination) is { } mismatch)
        {
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = $"コピーの確認に失敗しました：{mismatch}",
                LeftoverAt = RemoveCopies(destination, written, createdDestination),
            };
        }

        return new StoreMoveResult
        {
            Succeeded = true,
            Copied = copied,
            Bytes = bytes,
            SourceRemoved = TryRemoveSource(source, files),
        };
    }

    /// <summary>
    /// 止めた・失敗した引越しの途中のコピーを消す（ユーザ判断 2026-10-01）。
    /// 残すと、次に同じ場所を選んだときに「既にあるライブラリ」に見え、「選んだ場所のデータを使う」で
    /// 半分しか無いライブラリへ切り替えられた（作り物の2GBで確かめた。docs/research/store-transfer-2026-10-01.md）。
    /// 消すのは書いたファイルと、それで空になったフォルダだけ。置き換えで退けた物（_置き換え前-…）には触れない。
    /// </summary>
    /// <returns>消しきれずに残した場所。全部消せたら null。</returns>
    private static string? RemoveCopies(string destination, IReadOnlyList<string> written, bool createdDestination)
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

        // 書いたファイルの親を、深い方から畳む。中に別の物があれば残る（空のときだけ消す）
        var folders = written
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .SelectMany(folder => Ancestors(folder, destination))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(folder => folder.Length);
        foreach (var folder in folders)
        {
            TryRemoveEmptyFolder(folder);
        }

        if (createdDestination)
        {
            TryRemoveEmptyFolder(destination);
        }

        return allRemoved ? null : destination;

        static IEnumerable<string> Ancestors(string folder, string root)
        {
            var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            for (var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
                 current.Length > stop.Length && IsSameOrInside(current, stop);
                 current = Path.GetDirectoryName(current)!)
            {
                yield return current;
            }
        }

        static void TryRemoveEmptyFolder(string folder)
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
    }

    private static StoreMoveResult Refused(string error)
        => new() { Succeeded = false, Copied = 0, Bytes = 0, Error = error };

    /// <summary>置き換えで、2つの場所が重なっていれば断る理由を返す。</summary>
    private static string? Overlap(string source, string destination)
        => IsSameOrInside(source, destination)
            ? "今の保存先が、選んだ場所の中にあります。別の場所を選んでください。"
            : IsSameOrInside(destination, source)
                ? "選んだ場所が今の保存先の中にあります。今の保存先の外の場所を選んでください。"
                : null;

    /// <summary><paramref name="path"/> が <paramref name="folder"/> そのものか、その内側か。</summary>
    private static bool IsSameOrInside(string path, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var container = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(container, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 突き合わせない物。<c>logs/</c> は運んでいる間も書き足される（ログは引越しの門を通らない——
    /// 書けなくても投げない決まりで、待たせる相手でもない）。大きさが食い違って
    /// 「コピーは済んでいるのに確認に失敗」になっていた。運ぶ・消すのは他と同じ（食い違うのは最後の数行だけ）。
    /// </summary>
    private static bool IsUnverified(string relative)
        => relative.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>全部揃っていれば null、足りなければその名前を返す。</summary>
    private static string? Verify(string source, string destination)
    {
        foreach (var file in Enumerate(source))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsUnverified(relative))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);

            if (!File.Exists(target))
            {
                return relative;
            }

            if (new FileInfo(target).Length != new FileInfo(file).Length)
            {
                return relative + "（大きさが違います）";
            }
        }

        return null;
    }

    /// <summary>
    /// 元を消す。運んだファイルだけを消し、空になったフォルダも畳む。
    /// <c>app.lock</c> は実行中のこのプロセスが握っているので消せない。
    /// 握りを放してから呼べば消えるが、消せなくても引越し自体は成立している。
    /// </summary>
    private static bool TryRemoveSource(string source, IReadOnlyList<string> files)
    {
        var allRemoved = true;

        foreach (var file in files)
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

        // 深い方から畳む
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
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

    private static IEnumerable<string> Enumerate(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !Skipped.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            : [];
}
