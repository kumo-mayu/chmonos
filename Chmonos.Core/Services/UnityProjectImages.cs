using System.Security.Cryptography;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>商品の画像1つ。大きさと中身の手掛かりが無い物は、名前でだけ照らす。</summary>
/// <param name="Name">ファイルの名前（フォルダを含まない）。</param>
/// <param name="Size">バイト数。分からなければ null。</param>
/// <param name="Crc">zip の目次の CRC-32。</param>
/// <param name="Sha256">単体のファイルの記録のハッシュ。</param>
/// <param name="SourcePath">登録したフォルダの中のファイルの場所（その場で CRC を計算する）。</param>
public sealed record UnityItemImage(string Name, long? Size, uint? Crc = null, string? Sha256 = null, string? SourcePath = null);

/// <summary>プロジェクトの中の画像1つ。</summary>
/// <param name="Path">Unity 上のパス（<c>Assets/…</c>）。</param>
/// <param name="FullPath">ディスクの場所。</param>
public sealed record UnityProjectImage(string Path, string FullPath, long Size);

/// <summary>どう見つかったか。割合にせず見つかり方をそのまま出す（数字は根拠を説明できない。ユーザ判断 2026-11-01）。</summary>
public enum UnityImageMatchKind
{
    /// <summary>大きさと中身が一致した。</summary>
    SameImage,

    /// <summary>名前だけ一致した（Unity の外で編集した物か、別の画像）。</summary>
    SameName,
}

/// <param name="ProjectPath">プロジェクトの中の場所。</param>
/// <param name="SourceName">商品の側の画像の名前。</param>
public sealed record UnityImageMatch(string ProjectPath, string SourceName, UnityImageMatchKind Kind);

/// <summary>
/// 手で Unity へ置いた画像を見つける（ユーザ判断 2026-11-01）。手で置いた物には unitypackage の GUID もパスも無い。
///
/// **名前から探さず、大きさ → 中身の順に探す。**名前を変えた画像も見つかり、大きさの合う物しか読まないので軽い。
/// 手元で最も画像の多いプロジェクト（514枚・0.50GB）で、大きさが他と重なるのは46枚だけ、一覧を作るのは約0.4秒だった。
///
/// 同じ画像が複数の商品にあれば、どちらにも出す。手で置いた画像は、どちらから入れた物か決められない
/// （unitypackage の照合が共有の部品を数えないのは、lilToon の同梱で全商品が当たるのを避けるためで、事情が違う）。
/// </summary>
public static class UnityProjectImages
{
    /// <summary>照らす形式（ユーザ判断 2026-11-01）。BOOTH のテクスチャの配布と Unity が読む形式の重なり。</summary>
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".psd",
    };

    public static bool IsImage(string name) => Extensions.Contains(Path.GetExtension(name));

    /// <summary>プロジェクトの <c>Assets</c> の下の画像。Packages は見ない（手で置く場所ではなく、プロジェクトタブの検索にも出ない）。</summary>
    public static IReadOnlyList<UnityProjectImage> ListProject(string project, CancellationToken token = default)
    {
        var assets = Path.Combine(project, "Assets");
        if (!Directory.Exists(assets))
        {
            return [];
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(assets).EnumerateFiles("*", options)
            .Where(file =>
            {
                token.ThrowIfCancellationRequested();
                return IsImage(file.Name);
            })
            .Select(file => new UnityProjectImage(
                Path.GetRelativePath(project, file.FullName).Replace('\\', '/'),
                file.FullName,
                file.Length))
            .ToList();
    }

    /// <summary>手元のファイル1つの画像：単体の画像か、zip の中の画像（控えが無ければ名前だけ）。</summary>
    public static IEnumerable<UnityItemImage> ImagesOf(LocalFileRecord file, ZipImageStore store)
    {
        if (file.Paths.FirstOrDefault(IsImage) is { } single)
        {
            yield return new UnityItemImage(Path.GetFileName(single), file.SizeBytes, Sha256: file.Hash);
            yield break;
        }

        if (store.LoadOrRemember(file) is { } rows)
        {
            foreach (var row in rows)
            {
                yield return new UnityItemImage(Path.GetFileName(row.Path), row.Size, row.CrcValue);
            }

            yield break;
        }

        foreach (var name in file.Contents.Where(IsImage))
        {
            yield return new UnityItemImage(Path.GetFileName(name), null);
        }
    }

    /// <summary>登録したフォルダの中の画像（在るときだけ。記録に無いのでその場で並べる）。</summary>
    public static IEnumerable<UnityItemImage> ImagesInFolder(string folder, CancellationToken token = default)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(folder).EnumerateFiles("*", options)
            .Where(file =>
            {
                token.ThrowIfCancellationRequested();
                return IsImage(file.Name);
            })
            .Select(file => new UnityItemImage(file.Name, file.Length, SourcePath: file.FullName))
            .ToList();
    }

    /// <summary>商品ごとに、プロジェクトの中で見つかった画像を返す。見つからない商品は結果に入らない。</summary>
    /// <param name="fromPackages">unitypackage で入ったと分かっているパス。名前だけでは当てない（よくある名前で別の商品と取り違える）。</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<UnityImageMatch>> Match(
        IReadOnlyList<UnityProjectImage> project,
        IReadOnlyDictionary<string, IReadOnlyList<UnityItemImage>> items,
        IReadOnlySet<string> fromPackages,
        CancellationToken token = default)
    {
        var bySize = project.ToLookup(image => image.Size);
        var byName = project
            .Where(image => !fromPackages.Contains(image.Path))
            .ToLookup(image => Path.GetFileName(image.Path), StringComparer.OrdinalIgnoreCase);

        // 同じ画像を商品ごとに読み直さない
        var crcs = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        var shas = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, IReadOnlyList<UnityImageMatch>>(StringComparer.Ordinal);
        foreach (var (itemId, images) in items)
        {
            var same = new Dictionary<string, UnityImageMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var image in images)
            {
                token.ThrowIfCancellationRequested();
                if (image.Size is not ({ } size and > 0))
                {
                    continue;
                }

                foreach (var candidate in bySize[size])
                {
                    if (!same.ContainsKey(candidate.Path) && SameContent(image, candidate, crcs, shas, token))
                    {
                        same[candidate.Path] = new UnityImageMatch(candidate.Path, image.Name, UnityImageMatchKind.SameImage);
                    }
                }
            }

            // 中身で見つかった画像は、名前だけの当たりを重ねない。編集した物かもしれないので、中身が違っても名前の当たりは残す
            var foundNames = same.Values.Select(match => match.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var named = images
                .Where(image => !foundNames.Contains(image.Name))
                .SelectMany(image => byName[image.Name].Select(candidate => new UnityImageMatch(candidate.Path, image.Name, UnityImageMatchKind.SameName)))
                .Where(match => !same.ContainsKey(match.ProjectPath))
                .DistinctBy(match => match.ProjectPath, StringComparer.OrdinalIgnoreCase);

            var found = same.Values.OrderBy(match => match.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .Concat(named.OrderBy(match => match.ProjectPath, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (found.Count > 0)
            {
                result[itemId] = found;
            }
        }

        return result;
    }

    private static bool SameContent(
        UnityItemImage image,
        UnityProjectImage candidate,
        Dictionary<string, uint?> crcs,
        Dictionary<string, string?> shas,
        CancellationToken token)
    {
        if (image.Sha256 is { } sha)
        {
            return string.Equals(ShaOf(candidate.FullPath, shas, token), sha, StringComparison.OrdinalIgnoreCase);
        }

        if (image.Crc is { } crc)
        {
            return CrcOf(candidate.FullPath, crcs, token) == crc;
        }

        return image.SourcePath is { } source
            && CrcOf(source, crcs, token) is { } sourceCrc
            && CrcOf(candidate.FullPath, crcs, token) == sourceCrc;
    }

    private static uint? CrcOf(string path, Dictionary<string, uint?> known, CancellationToken token)
    {
        if (known.TryGetValue(path, out var crc))
        {
            return crc;
        }

        try
        {
            crc = Crc32.OfFile(path, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unity が書いている途中・消えた。一致とは言えないだけで、ほかの画像は続ける
            crc = null;
        }

        known[path] = crc;
        return crc;
    }

    private static string? ShaOf(string path, Dictionary<string, string?> known, CancellationToken token)
    {
        if (known.TryGetValue(path, out var sha))
        {
            return sha;
        }

        try
        {
            // 大きな画像でも途中で止められるよう、少しずつ読む
            using var stream = File.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }

            sha = Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            sha = null;
        }

        known[path] = sha;
        return sha;
    }
}
