using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// zip を一時フォルダへ展開する（#56）。
///
/// **unitypackage になっていない配布物（テクスチャ・PSD など）は、展開しないと Unity に入れられない**
/// （友人の話）。このアプリは zip を展開しない方針（Unity へは zip の中を直接指して渡す）なので、
/// そういう物のために「一時的に展開してエクスプローラで開く」逃げ道を置く。
///
/// **置き場所は一時フォルダで、アプリを閉じると消す。**消し残りは次の起動で消す（ユーザ判断）。
/// 取り込み・監視の対象には入れない——閉じると消えるパスを商品に紐付けても「見つからない」になるだけ。
/// </summary>
public sealed class TemporaryUnpacker
{
    /// <summary>
    /// 書き出すときに1回で読む量。<see cref="Stream.CopyTo(Stream)"/> の既定と同じ 81,920 バイト
    /// （大きいオブジェクトの置き場に入らない上限に合わせた値）。この区切りごとに中止を見て、進み具合を知らせる
    /// </summary>
    private const int CopyBufferBytes = 81920;

    /// <summary>既定の置き場所。取り込みの走査はここを見ない（<see cref="IsInsideDefaultRoot"/>）。</summary>
    public static string DefaultRoot { get; } = Path.Combine(Path.GetTempPath(), "Chmonos", "unpacked");

    private readonly string _root;

    /// <param name="root">置き場所。試験では別の場所を渡す（本物の一時フォルダを消さないため）。</param>
    public TemporaryUnpacker(string? root = null)
    {
        _root = root ?? DefaultRoot;
    }

    /// <summary>そのパスが既定の置き場所の中か。取り込みで拾わないために見る。</summary>
    public static bool IsInsideDefaultRoot(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DefaultRoot)) + Path.DirectorySeparatorChar;
        try
        {
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// 展開して、展開先のフォルダを返す。
    ///
    /// **同じ zip は展開し直さない。**展開先の名前に zip の場所・大きさ・更新時刻から作った印を入れ、
    /// 終わった印のファイル（展開先の隣）があればそのまま返す。途中で止まった展開は消してからやり直す。
    ///
    /// zip の外へ書き出そうとする名前（<c>../</c> で上がる・絶対パス）は飛ばす。配布物を開くだけなので、
    /// 置き場所の外に何かを書く理由が無い。
    ///
    /// **中止すると、書きかけを消してから <see cref="OperationCanceledException"/> を投げる**（ユーザ判断 2026-09-30）。
    /// 遅いディスクでは数十秒かかるので、下の帯に進み具合と「中止」を出す。終わった印は書かないので、
    /// 同じ zip をもう一度押せば最初から展開し直す。
    ///
    /// **失敗したときも、書きかけを消してから投げる**（ユーザ判断 2026-09-30）。空き容量が足りなくて失敗した人は
    /// 空きを作りたいのに、書きかけが次に押すかアプリを閉じるまで残って邪魔をしていた。
    /// </summary>
    /// <param name="progress">書き出した量。ファイルの途中でも細かく届くので、画面へ出す側で間引く。</param>
    public string Unpack(
        string zipPath,
        IProgress<TemporaryUnpackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("zip が見つかりません。", zipPath);
        }

        var destination = Path.Combine(_root, $"{SafeName(Path.GetFileNameWithoutExtension(zipPath))}-{Stamp(info)}");

        // **同じ展開先は1本ずつ**（大容量の確かめ #3・2026-09-30）。展開の途中にもう一度押すと、
        // 2本目が1本目の書きかけ（終わった印がまだ無い）を「途中で止まった展開」と見て消し、
        // 1本目が自分の消された・掴まれたファイルで失敗して「別のアプリがファイルを開いています」と出していた（掴んでいたのはこのアプリ）。
        // 2本目は1本目を待ち、終わった印を見てそのまま返す。印を作る元の値（場所・大きさ・更新時刻）が同じなら展開先も同じなので、
        // 展開先の名前で錠を分ける（別の zip の展開は待たせない）。錠は展開ごとに作らず持ち続ける——数は押した zip の数だけ
        var gate = Gates.GetOrAdd(Path.GetFullPath(destination).ToUpperInvariant(), _ => new SemaphoreSlim(1, 1));
        // 錠を待っている間の中止は、ここで投げて出る。**錠を持たないうちは展開先に触らない**
        // （先に入っている1本の書きかけを消すと、2本目が書きかけを消していたときと同じ壊れ方になる）
        gate.Wait(cancellationToken);
        try
        {
            OnGateEntered?.Invoke();
            return UnpackInto(zipPath, destination, progress, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>展開先ごとの錠。命令は展開のたびに新しい <see cref="TemporaryUnpacker"/> を作るので、インスタンスをまたいで持つ。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    /// <summary>錠に入った直後に呼ぶ（試験で、1本目が展開している最中に2本目を押した状況を作る）。</summary>
    internal Action? OnGateEntered { get; init; }

    private static string UnpackInto(
        string zipPath,
        string destination,
        IProgress<TemporaryUnpackProgress>? progress,
        CancellationToken cancellationToken)
    {
        var doneMarker = destination + ".done";
        if (Directory.Exists(destination) && File.Exists(doneMarker))
        {
            return destination;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        try
        {
            Extract(zipPath, destination, progress, cancellationToken);
        }
        catch
        {
            // **錠の中で片付ける。**錠を放してから消すと、待っていた次の1本が書き始めた物を消してしまう。
            // zip と書きかけのファイルは Extract を抜けた時点で閉じているので、ここで消せる。
            // 中止だけでなく失敗（空きが足りない・zip が壊れている）でも消す——失敗の種類で分けると、
            // 「ディスクの空きが足りません」と言われた人の前に、その空きを食っている書きかけが残る。
            // 消せなくても元の例外をそのまま投げる（RemovePartial は投げない。失敗の文は元の原因で決まる）
            RemovePartial(destination);
            throw;
        }

        File.WriteAllText(doneMarker, zipPath);
        return destination;
    }

    private static void Extract(
        string zipPath,
        string destination,
        IProgress<TemporaryUnpackProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);

        // 書き出す物を先に決める。全体の大きさが分からないと、進み具合を「どこまで来たか」で出せない
        var targets = new List<(ZipArchiveEntry Entry, string Target)>();
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add((entry, target));
            }
        }

        var total = targets.Where(item => !item.Entry.FullName.EndsWith('/')).Sum(item => item.Entry.Length);
        long done = 0;
        progress?.Report(new TemporaryUnpackProgress(done, total));

        var buffer = new byte[CopyBufferBytes];
        foreach (var (entry, target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteEntry(entry, target, buffer, ref done, total, progress, cancellationToken);
        }
    }

    /// <summary>
    /// zip の中の1件を書き出す。一時展開と、Unity へ送る前の取り出し（<see cref="ExtractEntry"/>）で同じ書き方をする。
    ///
    /// **ファイルの途中でも中止を見る。**`ExtractToFile` は1ファイルを書き切るまで戻らないので、
    /// 数GBの1ファイル（PSD・動画・unitypackage）では、中止を押しても書き終わるまで止まらず、進み具合も動かない
    /// </summary>
    private static void WriteEntry(
        ZipArchiveEntry entry,
        string target,
        byte[] buffer,
        ref long done,
        long total,
        IProgress<TemporaryUnpackProgress>? progress,
        CancellationToken cancellationToken)
    {
        using (var source = entry.Open())
        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                done += read;
                progress?.Report(new TemporaryUnpackProgress(done, total));
            }
        }

        // `ExtractToFile` と同じく、zip に書いてある更新時刻を付ける（エクスプローラで日付順に並べたときに元の順になる）
        File.SetLastWriteTime(target, entry.LastWriteTime.DateTime);
    }

    /// <summary>
    /// 中止した・失敗した展開の書きかけを消す。消せない物（ウイルス対策ソフトが掴んでいる等）が残っても、終わった印が無いので
    /// 次の展開が消してからやり直し、アプリを閉じるときにも消す。ここでは諦めて進む
    /// </summary>
    private static void RemovePartial(string destination)
    {
        try
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// zip の中の1ファイルだけを取り出して、その場所を返す（#69 Unityへ順に送る）。
    ///
    /// Unity の「Custom Package...」のファイル選択には実在するパスを渡す必要がある
    /// （zip の中を指す仮想パスは、ファイル選択の画面を通したときに何が返るか分からない）。
    /// 置き場所は一時展開と同じで、アプリを閉じると消える。同じ zip の同じファイルは取り出し直さない。
    ///
    /// **中止か失敗のときは、書きかけを消してから投げる**（ユーザ判断 2026-09-30）。前は1件を書き切るまで中止が効かず、
    /// 数GBの unitypackage を遅いディスクで送ると、「中止」を押してから長く待たされた。
    /// 本物の名前のファイルは書き切るまで置かないので、もう一度送れば最初から取り出し直す。
    /// </summary>
    /// <param name="progress">書き出した量。細かく届くので、画面へ出す側で間引く（<see cref="Unpack"/> と同じ）。</param>
    public string ExtractEntry(
        string zipPath,
        string entryPath,
        IProgress<TemporaryUnpackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("zip が見つかりません。", zipPath);
        }

        // **zip の中のパス全体で置き場所を決める。**前はファイル名だけで決めていたので、
        // 同じ zip の `PC/X.unitypackage` と `Quest/X.unitypackage` が同じ控えになり、
        // 後から頼んだ方に先に取り出した方を渡していた（取り違えたまま Unity に入る）。
        // ファイル名は最後にそのまま残す——Unity の取り込みの窓はファイル名を出すので
        var segments = entryPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(SafeSegment).ToArray();
        string[] directories = segments.Length > 1 ? segments[..^1] : [];
        var folder = Path.Combine([_root, "packages", Stamp(info), .. directories]);
        var fileName = SafeFileName(segments.Length == 0 ? string.Empty : segments[^1]);
        var target = Path.Combine(folder, fileName);

        // **長すぎるパスは Unity に渡せない**（ファイル選択の窓は 260 字まで）。そのときだけ、zip の中のフォルダの段を
        // 中のパス全体から作った短い印1段に畳む（印は全体から作るので PC/ と Quest/ の取り違えは起きない）。
        // それでも長ければファイル名を切り詰める（拡張子は残す）。
        // 2026-09-23 に手元の zip 336 個（unitypackage 382 件）で測ると、中のパスは最長 102 字・置き場所の頭が 69 字で、
        // 合わせて 171 字。今は届かないが、利用者名や一時フォルダの場所、深い配布物で伸びるので守りだけ置く
        if (target.Length > MaxUnityPath)
        {
            folder = Path.Combine(_root, "packages", Stamp(info), ShortStamp(entryPath));
            var room = MaxUnityPath - folder.Length - 1;
            if (fileName.Length > room)
            {
                var extension = Path.GetExtension(fileName);
                fileName = fileName[..Math.Max(1, room - extension.Length)] + extension;
            }

            target = Path.Combine(folder, fileName);
        }

        // **同じ取り出し先は1本ずつ**（一時展開と同じ錠）。送信は1列なので今は重ならないが、重なると2本目が
        // 1本目の書きかけ（同じ「.part」）を開こうとして「別のアプリがファイルを開いています」になる。
        // 在るかを見るのも錠の中——待っている間に1本目が書き終えていれば、それをそのまま返す
        var gate = Gates.GetOrAdd(Path.GetFullPath(target).ToUpperInvariant(), _ => new SemaphoreSlim(1, 1));
        gate.Wait(cancellationToken);
        try
        {
            if (File.Exists(target))
            {
                return target;
            }

            // 書きかけを本物の名前で置かない。Unity が途中のファイルを掴むと、壊れたパッケージとして読まれる
            var partial = target + ".part";
            try
            {
                Directory.CreateDirectory(folder);
                ExtractEntryTo(zipPath, entryPath, partial, progress, cancellationToken);
                File.Move(partial, target, overwrite: true);
                return target;
            }
            catch
            {
                // **錠の中で片付ける**（UnpackInto と同じ理由）。中止でも失敗でも消す。
                // 消せなくても元の例外をそのまま投げる（失敗の文は元の原因で決まる）。
                // 残った書きかけは、次に同じ物を送るときに上書きし、アプリを閉じるときにも消す
                RemovePartialFile(partial);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ExtractEntryTo(
        string zipPath,
        string entryPath,
        string partial,
        IProgress<TemporaryUnpackProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);
        var entry = archive.GetEntry(entryPath) ?? throw new FileNotFoundException("zip の中に見つかりません。", entryPath);

        cancellationToken.ThrowIfCancellationRequested();

        long done = 0;
        progress?.Report(new TemporaryUnpackProgress(done, entry.Length));
        WriteEntry(entry, partial, new byte[CopyBufferBytes], ref done, entry.Length, progress, cancellationToken);
    }

    private static void RemovePartialFile(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// zip の中のフォルダ名を1段ぶん、置き場所の名前にする。
    /// <c>..</c> と <c>.</c> は置き場所の外へ出る・同じ段に留まるので、ただの名前に変える。
    /// </summary>
    private static string SafeSegment(string segment)
    {
        var cleaned = SafeFileName(segment);
        return cleaned is "." or ".." ? "_" : cleaned;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return cleaned.Length == 0 ? "package.unitypackage" : cleaned;
    }

    /// <summary>
    /// 置き場所ごと消す。エクスプローラが中を開いたままだと消せない物が残るが、
    /// 次の起動でまた消すので、ここでは諦めて進む。
    /// </summary>
    /// <returns>全部消せたか。</returns>
    public bool CleanUp()
    {
        if (!Directory.Exists(_root))
        {
            return true;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();

        // 深い階層の zip で全体のパスが長くなりすぎないよう、名前は切り詰める
        return cleaned.Length == 0 ? "archive" : cleaned[..Math.Min(cleaned.Length, 60)];
    }

    /// <summary>
    /// Unity へ渡すパスの上限。Windows の MAX_PATH（260）から終端の1字を除いた長さ。
    /// 「.part」を足した書きかけの名前は Unity に渡さないので、ここには数えない（.NET は長いパスも扱える）
    /// </summary>
    internal const int MaxUnityPath = 259;

    private static string ShortStamp(string entryPath)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(entryPath)))[..8];

    private static string Stamp(FileInfo info)
    {
        var seed = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..8];
    }
}

/// <summary>一時展開と、Unity へ送る前の取り出しの進み具合。大きさは展開した後のバイト数（zip の中に書いてある値）。</summary>
/// <param name="DoneBytes">書き出した量。</param>
/// <param name="TotalBytes">書き出す物の合計。空のファイルとフォルダだけの zip では 0。</param>
public readonly record struct TemporaryUnpackProgress(long DoneBytes, long TotalBytes);
