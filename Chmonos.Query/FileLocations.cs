using System.Text.Json;

namespace Chmonos.Query;

/// <summary>ファイルの逆引きをまとめて頼む（<c>POST /v1/files/lookup</c>）。どちらか片方だけでもよい。</summary>
public sealed record FilesLookupRequest(IReadOnlyList<string?>? Hashes, IReadOnlyList<string?>? Paths);

/// <summary>送った1つ（ハッシュか場所）と、それを持つ商品（<c>GET /v1/files/lookup</c> の1回分の答えと同じ）。見つからなければ空。</summary>
public sealed record FileLookupResult(string Key, IReadOnlyList<FileOwnerRef> Owners);

/// <param name="Hashes">送ったハッシュごと（送った順）。</param>
/// <param name="Paths">送った場所ごと（送った順）。</param>
public sealed record FilesLookupAnswer(IReadOnlyList<FileLookupResult> Hashes, IReadOnlyList<FileLookupResult> Paths);

/// <summary>ハッシュから場所を引く（<c>POST /v1/files/locations</c>）。</summary>
public sealed record FileLocationsRequest(IReadOnlyList<string?>? Hashes);

/// <summary>ハッシュを数えた控え（<c>scan-cache.json</c>）の1行。</summary>
/// <param name="Hash">ハッシュ（控えに書かれた形）。</param>
/// <param name="Path">最後に数えたときの場所。今そこに在るかは見ない。</param>
/// <param name="ModifiedAt">そのときのファイルの更新日時。</param>
/// <param name="Owner"><c>item</c>（商品のファイル）・<c>unresolved</c>（商品が決まっていないファイル）。</param>
public sealed record FileLocationRef(string Hash, string Path, long SizeBytes, DateTimeOffset ModifiedAt, string Owner);

/// <param name="Found">見つかった場所（送った順・同じハッシュの中は場所の順）。</param>
/// <param name="Missing">控えに場所の無いハッシュ（出さない範囲の物も含む）。</param>
public sealed record FileLocationsAnswer(IReadOnlyList<FileLocationRef> Found, IReadOnlyList<string> Missing);

/// <summary>
/// ファイルのハッシュの逆引き（plan 32）。まとめて引く道は <see cref="Catalogs.LookupFiles"/> を1つずつ当てるだけ（同じ判断を2つ持たない）。
/// 場所を引く道は取り込みがハッシュを数えた控え（<c>scan-cache.json</c>）を読む。
/// </summary>
public static class FileLocations
{
    /// <summary>1回で送れる数（ハッシュと場所を合わせて）。items/batch と同じ。</summary>
    public const int MaxKeys = 200;

    public static FilesLookupAnswer Lookup(QuerySnapshot snapshot, FilesLookupRequest request)
    {
        var hashes = Keys(request.Hashes, "hashes");
        var paths = Keys(request.Paths, "paths");
        if (hashes.Count + paths.Count == 0)
        {
            throw QueryException.Invalid("invalidLookup", "hashes か paths に1つ以上書いてください。");
        }

        if (hashes.Count + paths.Count > MaxKeys)
        {
            throw QueryException.Invalid("invalidLimit", $"一度に引けるのは、ハッシュと場所を合わせて {MaxKeys} 件までです。");
        }

        return new FilesLookupAnswer(
            hashes.Select(hash => new FileLookupResult(hash, Catalogs.LookupFiles(snapshot, null, hash))).ToList(),
            paths.Select(path => new FileLookupResult(path, Catalogs.LookupFiles(snapshot, path, null))).ToList());
    }

    /// <summary>
    /// ハッシュを数えた控えから場所を引く。**出すのは商品のファイルと、商品が決まっていないファイルの場所だけ**：
    /// 控えには登録したフォルダの中で数えた全部の物（外したファイル・どこにも属さない物）が載るが、外した物は使う人が「資産ではない」とした物なので出さない。
    /// </summary>
    public static FileLocationsAnswer Locations(QuerySnapshot snapshot, FileLocationsRequest request)
    {
        var hashes = Keys(request.Hashes, "hashes");
        if (hashes.Count == 0)
        {
            throw QueryException.Invalid("invalidHashes", "hashes に1つ以上書いてください。");
        }

        if (hashes.Count > MaxKeys)
        {
            throw QueryException.Invalid("invalidLimit", $"一度に引けるのは {MaxKeys} 件までです。");
        }

        var cache = Read(() => snapshot.Store.ScanCache.Load()) ?? [];
        var unresolved = (Read(() => snapshot.Store.Unresolved.Load()) ?? [])
            .Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byHash = cache.Where(row => row is { Hash.Length: > 0, Path.Length: > 0 }).ToLookup(row => row.Hash, StringComparer.OrdinalIgnoreCase);

        var found = new List<FileLocationRef>();
        var missing = new List<string>();
        foreach (var hash in hashes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var owner = snapshot.FilesByHash.Contains(hash) ? "item" : unresolved.Contains(hash) ? "unresolved" : null;
            var rows = owner is null
                ? []
                : byHash[hash]
                    .OrderBy(row => row.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new FileLocationRef(row.Hash, row.Path, row.SizeBytes, row.ModifiedAtUtc.ToLocalTime(), owner))
                    .ToList();
            if (rows.Count == 0)
            {
                missing.Add(hash);
            }

            found.AddRange(rows);
        }

        return new FileLocationsAnswer(found, missing);
    }

    /// <summary>送られた値の一覧。null・空の物は入力の誤り（点検32と同じ）。</summary>
    private static IReadOnlyList<string> Keys(IReadOnlyList<string?>? values, string name)
    {
        if (values is null)
        {
            return [];
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw QueryException.Invalid("invalidIds", $"{name} に null や空の値は書けません。");
        }

        return values.Select(value => value!.Trim()).ToList();
    }

    /// <summary>控えが読めない（手で壊した）ときは無いのと同じにする（作り直せる控えなので、答えを止めない）。</summary>
    private static T? Read<T>(Func<T> load)
        where T : class
    {
        try
        {
            return load();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
