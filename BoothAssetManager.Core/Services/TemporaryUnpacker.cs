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
    /// </summary>
    public string Unpack(string zipPath, CancellationToken cancellationToken = default)
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
        gate.Wait(cancellationToken);
        try
        {
            OnGateEntered?.Invoke();
            return UnpackInto(zipPath, destination, cancellationToken);
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

    private static string UnpackInto(string zipPath, string destination, CancellationToken cancellationToken)
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

        Directory.CreateDirectory(destination);
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        File.WriteAllText(doneMarker, zipPath);
        return destination;
    }

    /// <summary>
    /// zip の中の1ファイルだけを取り出して、その場所を返す（#69 Unityへ順に送る）。
    ///
    /// Unity の「Custom Package...」のファイル選択には実在するパスを渡す必要がある
    /// （zip の中を指す仮想パスは、ファイル選択の画面を通したときに何が返るか分からない）。
    /// 置き場所は一時展開と同じで、アプリを閉じると消える。同じ zip の同じファイルは取り出し直さない。
    /// </summary>
    public string ExtractEntry(string zipPath, string entryPath, CancellationToken cancellationToken = default)
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

        if (File.Exists(target))
        {
            return target;
        }

        Directory.CreateDirectory(folder);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);
        var entry = archive.GetEntry(entryPath) ?? throw new FileNotFoundException("zip の中に見つかりません。", entryPath);

        cancellationToken.ThrowIfCancellationRequested();

        // 書きかけを本物の名前で置かない。Unity が途中のファイルを掴むと、壊れたパッケージとして読まれる
        var partial = target + ".part";
        entry.ExtractToFile(partial, overwrite: true);
        File.Move(partial, target, overwrite: true);
        return target;
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
