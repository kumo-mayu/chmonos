using System.IO.Enumeration;

namespace BoothAssetManager.Core.Services;

/// <summary>プロジェクトの中の1つの <c>.meta</c>。</summary>
/// <param name="MetaPath">Unity 上のパス（<c>Assets/FUKA/a.fbx.meta</c>）。</param>
/// <param name="WrittenUtc">更新時刻。変わっていなければ前に読んだ GUID を使い回す。</param>
public sealed record UnityMetaFile(string MetaPath, DateTime WrittenUtc);

/// <summary>
/// Unity プロジェクトの「GUID → 今のパス」の表（2026-09-29 ユーザ指示）。
///
/// **パスで見つからない物を GUID で探すため。**unitypackage の中のアセットは <c>&lt;GUID&gt;/pathname</c> の組で、
/// Unity は取り込んだアセットの <c>.meta</c> の <c>guid:</c> にその値を書く。利用者がプロジェクトの中でフォルダを移しても・名前を変えても
/// GUID は変わらない（変えると参照が切れるので Unity が保つ）。パスでは「入っていない」になる物も、GUID なら今の場所が分かる。
///
/// **全部を読むのは重いので、要るときだけ作り、プロジェクトごとに覚える。**
/// 手元の19のプロジェクトで測ると（2026-09-29）、<c>.meta</c> は1つのプロジェクトに 1,034〜5,449 個。
/// まだ開いていない <c>.meta</c> を1本ずつ読むと1つ約3.9ms で、5,449 個のプロジェクトは約20秒かかった。8本並べて読むと
/// 冷えた 5,000 個で約2.0秒。2回目からは一覧を取り直して更新時刻を比べるだけで 11〜46ms（一覧は冷えていても 80〜320ms）。
/// そこで、更新時刻が変わった物・新しい物だけを並べて読み直す。パスで全部見つかったときは作らない（<see cref="UnityProjectMatcher"/>）。
///
/// 同じ GUID が2か所にあるとき（Unity を閉じたままフォルダを写したなど）はどちらか言い切れないので、表から外す。
/// </summary>
public sealed class UnityProjectGuids(Func<IEnumerable<UnityMetaFile>> listMetas, Func<string, string?> readGuid)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Written, string? Guid)> _metas = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _byGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>今の表（GUID → アセットの Unity 上のパス）。前に作った物から、変わった <c>.meta</c> だけ読み直す。</summary>
    public IReadOnlyDictionary<string, string> Current()
    {
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stale = new List<UnityMetaFile>();
            foreach (var meta in listMetas())
            {
                seen.Add(meta.MetaPath);
                if (!_metas.TryGetValue(meta.MetaPath, out var known) || known.Written != meta.WrittenUtc)
                {
                    stale.Add(meta);
                }
            }

            // 並べて読む。まだ読んでいない .meta は1つ開くのに約3.9ms かかり（手元の SATA の SSD。開くたびの検査の分と見られる）、
            // 8本並べると約0.5ms になった（冷えた .meta 1000個ずつで測った。2026-09-29）
            var read = stale
                .AsParallel()
                .WithDegreeOfParallelism(Math.Clamp(Environment.ProcessorCount, 1, 8))
                .Select(meta => (Meta: meta, Guid: readGuid(meta.MetaPath)))
                .ToList();
            foreach (var (meta, guid) in read)
            {
                _metas[meta.MetaPath] = (meta.WrittenUtc, guid);
            }

            var changed = read.Count > 0;
            if (_metas.Count != seen.Count)
            {
                foreach (var gone in _metas.Keys.Where(path => !seen.Contains(path)).ToList())
                {
                    _metas.Remove(gone);
                }

                changed = true;
            }

            if (changed)
            {
                _byGuid = Build(_metas);
            }

            return _byGuid;
        }
    }

    private static Dictionary<string, string> Build(Dictionary<string, (DateTime Written, string? Guid)> metas)
    {
        var byGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doubled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (metaPath, (_, guid)) in metas)
        {
            if (guid is null)
            {
                continue;
            }

            var assetPath = metaPath[..^MetaExtension.Length];
            if (!byGuid.TryAdd(guid, assetPath))
            {
                doubled.Add(guid);
            }
        }

        foreach (var guid in doubled)
        {
            byGuid.Remove(guid);
        }

        return byGuid;
    }

    private const string MetaExtension = ".meta";

    /// <summary>
    /// <c>.meta</c> の頭の行から GUID を読む。Unity が書く <c>.meta</c> は1行目が <c>fileFormatVersion: 2</c>、2行目が <c>guid: …</c>。
    /// 手で書いた物や版の違いに備えて、頭の数行のどこにあっても拾う。32桁の16進でなければ null。
    /// </summary>
    public static string? ParseGuid(IEnumerable<string> lines)
    {
        foreach (var line in lines.Take(MetaHeadLines))
        {
            var text = line.Trim();
            if (!text.StartsWith("guid:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = text["guid:".Length..].Trim();
            return value.Length == 32 && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
        }

        return null;
    }

    /// <summary>GUID を探す頭の行の数。Unity の書く物は2行目にあるので、少し余裕を持たせるだけ。</summary>
    private const int MetaHeadLines = 5;

    // ---- ディスク ----

    /// <summary>プロジェクトの場所 → 表。アプリを開いている間だけ覚える（閉じれば捨てる。<c>.meta</c> を読み直せば戻る）。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UnityProjectGuids> s_projects
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ディスクのプロジェクトの表（プロジェクトごとに1つ。2回目からは変わった <c>.meta</c> だけ読む）。</summary>
    public static UnityProjectGuids ForProject(string projectPath)
        => s_projects.GetOrAdd(
            Path.GetFullPath(projectPath).TrimEnd('\\', '/'),
            static path => new UnityProjectGuids(DiskMetas(path), DiskGuid(path)));

    /// <summary>
    /// <c>Assets/</c> と <c>Packages/</c> の下の実フォルダの <c>.meta</c> の一覧。
    /// Packages の下は VPM などで置いた物が実フォルダで入り、unitypackage もそこへ入れることがある（BlendShare）。
    /// レジストリから入る物（<c>Library/PackageCache</c>）は unitypackage では入らないので見ない。
    /// </summary>
    public static Func<IEnumerable<UnityMetaFile>> DiskMetas(string projectPath) => () =>
    {
        var found = new List<UnityMetaFile>();
        Collect(projectPath, "Assets", found);
        var packages = Path.Combine(projectPath, "Packages");
        try
        {
            if (Directory.Exists(packages))
            {
                foreach (var folder in Directory.EnumerateDirectories(packages))
                {
                    Collect(projectPath, "Packages/" + Path.GetFileName(folder), found);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Packages を読めなくても Assets の分は使える
        }

        return found;
    };

    private static void Collect(string projectPath, string unityRoot, List<UnityMetaFile> found)
    {
        var root = Path.Combine(projectPath, unityRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
        {
            return;
        }

        // FileSystemEnumerable は一覧を取るときに更新時刻も一緒に返すので、ファイルごとに問い合わせずに済む
        var prefix = projectPath.TrimEnd('\\', '/').Length + 1;
        var files = new FileSystemEnumerable<UnityMetaFile>(
            root,
            (ref FileSystemEntry entry) => new UnityMetaFile(
                entry.ToFullPath()[prefix..].Replace('\\', '/'),
                entry.LastWriteTimeUtc.UtcDateTime),
            // 隠しフォルダ（Packages の下の .git など）は既定どおり飛ばす。Unity もアセットとして読まない
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry)
                => !entry.IsDirectory && entry.FileName.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase),
        };

        try
        {
            found.AddRange(files);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 途中で消えたフォルダなど。読めた分だけ使う
        }
    }

    /// <summary>1つの <c>.meta</c> の頭の数行だけ読む（本体は大きくない物が多いが、全部を読む理由は無い）。</summary>
    public static Func<string, string?> DiskGuid(string projectPath) => metaPath =>
    {
        try
        {
            using var reader = new StreamReader(
                Path.Combine(projectPath, metaPath.Replace('/', Path.DirectorySeparatorChar)),
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                new FileStreamOptions { Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete, BufferSize = 512 });
            return ParseGuid(Lines(reader));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        static IEnumerable<string> Lines(StreamReader reader)
        {
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }
    };
}
