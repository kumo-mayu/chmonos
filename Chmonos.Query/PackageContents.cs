using System.Globalization;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>unitypackage の中のアセット1つ。</summary>
/// <param name="Guid">Unity の GUID（32桁の16進・小文字）。</param>
/// <param name="Path">Unity 上のパス（<c>Assets/…</c>）。</param>
public sealed record PackageAssetRef(string Guid, string Path);

/// <summary>商品のファイルの中の unitypackage 1つと、その中身（<c>/v1/items/{id}/unitypackage-assets</c>）。</summary>
/// <param name="ZipHash">zip のハッシュ（<c>send-to-unity</c> の <c>zipHash</c> にそのまま使える）。</param>
/// <param name="FileState">ファイルの状態（<see cref="FileRef.State"/> と同じ）。</param>
/// <param name="Entry">zip の中の場所（<c>send-to-unity</c> の <c>entry</c>）。</param>
/// <param name="Assets">中身。**まだ読んでいなければ null**（控えが無い。取り込みの裏で読むまで待つ。API は zip を解かない）。</param>
public sealed record PackageContentsRef(string ZipHash, string FileState, string Entry, IReadOnlyList<PackageAssetRef>? Assets);

/// <summary>GUID の逆引きの要求（<c>POST /v1/unitypackages/lookup</c>）。</summary>
public sealed record GuidLookupRequest(IReadOnlyList<string?>? Guids);

/// <summary>GUID が入っていた unitypackage 1つと、その持ち主の商品。同じ GUID が複数の包み・商品にあれば全部並ぶ。</summary>
/// <param name="Guid">送られた GUID（小文字に揃えた物）。</param>
/// <param name="Path">その包みの中での Unity 上のパス。</param>
/// <param name="FileState">持ち主のファイルの状態（<see cref="FileRef.State"/> と同じ。外した物・古い版も潰さずに返す）。</param>
public sealed record GuidOwnerRef(string Guid, string Path, string ItemId, string ItemName, string ZipHash, string Entry, string FileState);

/// <param name="Found">見つかった物（送った順・同じ GUID の中は商品 ID・zip のハッシュ・entry の順）。</param>
/// <param name="Missing">どの商品の包みにも見つからなかった GUID（送った順）。</param>
public sealed record GuidLookupAnswer(IReadOnlyList<GuidOwnerRef> Found, IReadOnlyList<string> Missing);

/// <summary>
/// unitypackage の中身の控え（<c>unitypackages/&lt;zip のハッシュ&gt;.json</c>。<see cref="UnityPackagePathStore"/>）を読む道（plan 32）。
/// 控えは取り込みの裏が書く物で、API は読むだけ（zip を解かない・控えに書き足さない）。だから<b>単体の unitypackage と登録したフォルダの中の物は出ない</b>
/// （控えを作るのは zip の中の物だけ。<see cref="UnityPackageCatalog"/>）。
/// </summary>
public static class PackageContents
{
    /// <summary>一度に引ける GUID の数。Unity のプロジェクトで外れた参照を何十件もまとめて引くため。1件は表を1回引くだけなので、items/batch の 200 より多くした。</summary>
    public const int MaxGuids = 1000;

    /// <summary>商品のファイルの中の unitypackage と、その中身（外した物・古い版も状態つきで。中身は zip のハッシュで決まるので古くならない）。</summary>
    public static IReadOnlyList<PackageContentsRef> OfItem(QuerySnapshot snapshot, Core.Models.ItemRecord item)
    {
        var store = new UnityPackagePathStore(snapshot.Paths);
        var result = new List<PackageContentsRef>();
        foreach (var file in item.Local.LocalFiles)
        {
            // 中身の一覧が正（画面の RecordedPackagesIn と同じ）。一覧の無い古い記録は要約の entry を使う
            var entries = UnityHandoff.PackageEntriesIn(file);
            if (entries.Count == 0)
            {
                entries = (file.UnityPackages ?? []).Select(package => package.Entry).Distinct(StringComparer.Ordinal).ToList();
            }

            if (entries.Count == 0)
            {
                continue;
            }

            var stored = store.Load(file.Hash);
            var state = ItemMapper.FileState(file);
            foreach (var entry in entries)
            {
                var assets = stored?.GetValueOrDefault(entry)?.Select(asset => new PackageAssetRef(asset.Guid.ToLowerInvariant(), asset.Path)).ToList();
                result.Add(new PackageContentsRef(file.Hash, state, entry, assets));
            }
        }

        return result;
    }

    /// <summary>送られた GUID を読む形に揃える（前後の空白を除き小文字に）。32桁の16進でなければ入力の誤り。</summary>
    public static IReadOnlyList<string> NormalizeGuids(IReadOnlyList<string?>? guids)
    {
        if (guids is not { Count: > 0 })
        {
            throw QueryException.Invalid("invalidGuids", "guids に GUID を1つ以上書いてください。");
        }

        if (guids.Count > MaxGuids)
        {
            throw QueryException.Invalid("invalidLimit", $"一度に引ける GUID は {MaxGuids} 件までです。");
        }

        var result = new List<string>(guids.Count);
        foreach (var guid in guids)
        {
            var text = guid?.Trim().ToLowerInvariant();
            if (text is null || PackageGuidIndex.KeyOf(text) is null)
            {
                throw QueryException.Invalid("invalidGuid", $"GUID は32桁の16進で書いてください: {guid}");
            }

            result.Add(text);
        }

        return result;
    }
}

/// <summary>
/// GUID → それが入っている unitypackage の控え（zip のハッシュ）の表。全部の控えを読んで作り、控えのフォルダが変わるまで使い回す。
///
/// **持つのは GUID と控えの番号だけ**（パスも商品も持たない）。パスは当たった控えだけを読み直して取り、商品は要求ごとの写しで引く。
/// 控えのフォルダの印（数・大きさの合計・最新の日時）は要求ごとに見る（取り込みの裏が書き足すのを、見張りの対象に足さずに拾うため）。
/// 測った数は docs/spec/local-api-stored.md。
/// </summary>
public sealed class PackageGuidIndex(AppPaths paths)
{
    private readonly object _gate = new();
    private string? _stamp;
    private string[] _hashes = [];

    // GUID は32桁の16進なので 128 ビットの数で持つ（文字列で持つより1件あたり小さい）。
    // 1つの控えにしか無い GUID がほとんどなので、2つ目からは別の表に分ける（全部に一覧を持たせると1件ごとに配列の分が増える）
    private Dictionary<UInt128, int> _first = [];
    private Dictionary<UInt128, List<int>> _more = [];

    /// <summary>表にある GUID の数（測るため）。</summary>
    public int GuidCount { get { lock (_gate) { return _first.Count; } } }

    /// <summary>読んだ控えの数（測るため）。</summary>
    public int FileCount { get { lock (_gate) { return _hashes.Length; } } }

    internal static UInt128? KeyOf(string guid)
        => guid.Length == 32 && UInt128.TryParse(guid, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>GUID をまとめて引く（<paramref name="guids"/> は <see cref="PackageContents.NormalizeGuids"/> を通した物）。</summary>
    public GuidLookupAnswer Lookup(QuerySnapshot snapshot, IReadOnlyList<string> guids)
    {
        var (hashes, places) = Places(guids);
        var store = new UnityPackagePathStore(paths);

        // 当たった控えだけを1回ずつ読み、GUID のパスと entry を取る
        var hits = new Dictionary<string, List<(string Hash, string Entry, string Path)>>(StringComparer.Ordinal);
        foreach (var fileIndex in places.Values.SelectMany(list => list).Distinct())
        {
            var hash = hashes[fileIndex];
            if (store.Load(hash) is not { } packages)
            {
                continue;
            }

            foreach (var (entry, assets) in packages)
            {
                foreach (var asset in assets)
                {
                    var guid = asset.Guid.ToLowerInvariant();
                    if (places.TryGetValue(guid, out var wanted) && wanted.Contains(fileIndex))
                    {
                        (hits.TryGetValue(guid, out var list) ? list : hits[guid] = []).Add((hash, entry, asset.Path));
                    }
                }
            }
        }

        var found = new List<GuidOwnerRef>();
        var missing = new List<string>();
        foreach (var guid in guids.Distinct(StringComparer.Ordinal))
        {
            var rows = (hits.GetValueOrDefault(guid) ?? [])
                .SelectMany(hit => snapshot.FilesByHash[hit.Hash].Select(owner => new GuidOwnerRef(
                    guid, hit.Path, owner.Item.Id, owner.Item.DisplayName, owner.File.Hash, hit.Entry, ItemMapper.FileState(owner.File))))
                .OrderBy(row => row.ItemId, StringComparer.Ordinal)
                .ThenBy(row => row.ZipHash, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Entry, StringComparer.Ordinal)
                .ToList();

            // 控えにはあっても、どの商品のファイルでもない zip（未確定のまま・商品を消した）は見つからないと答える。商品へ戻せないので
            if (rows.Count == 0)
            {
                missing.Add(guid);
            }

            found.AddRange(rows);
        }

        return new GuidLookupAnswer(found, missing);
    }

    /// <summary>GUID ごとの、入っている控えの番号。表は錠の中で今の控えに合わせてから引く。</summary>
    private (string[] Hashes, Dictionary<string, List<int>> Places) Places(IReadOnlyList<string> guids)
    {
        lock (_gate)
        {
            Refresh();
            var places = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var guid in guids)
            {
                if (KeyOf(guid) is not { } key || places.ContainsKey(guid) || !_first.TryGetValue(key, out var first))
                {
                    continue;
                }

                places[guid] = [first, .. _more.GetValueOrDefault(key) ?? []];
            }

            return (_hashes, places);
        }
    }

    /// <summary>控えのフォルダの印が変わっていれば、表を作り直す。</summary>
    private void Refresh()
    {
        var files = ListFiles();
        var stamp = $"{files.Count}:{files.Sum(file => file.Length)}:{(files.Count == 0 ? 0 : files.Max(file => file.LastWriteTimeUtc.Ticks))}";
        if (stamp == _stamp)
        {
            return;
        }

        var store = new UnityPackagePathStore(paths);
        var hashes = new List<string>(files.Count);
        var first = new Dictionary<UInt128, int>();
        var more = new Dictionary<UInt128, List<int>>();
        foreach (var file in files)
        {
            var hash = Path.GetFileNameWithoutExtension(file.Name);
            if (store.Load(hash) is not { } packages)
            {
                continue;
            }

            var index = hashes.Count;
            hashes.Add(hash);
            foreach (var asset in packages.Values.SelectMany(assets => assets))
            {
                if (KeyOf(asset.Guid.ToLowerInvariant()) is not { } key)
                {
                    continue;
                }

                if (!first.TryAdd(key, index) && first[key] != index)
                {
                    var list = more.TryGetValue(key, out var known) ? known : more[key] = [];
                    if (!list.Contains(index))
                    {
                        list.Add(index);
                    }
                }
            }
        }

        first.TrimExcess();
        _hashes = [.. hashes];
        _first = first;
        _more = more;
        _stamp = stamp;
    }

    private List<FileInfo> ListFiles()
    {
        try
        {
            var directory = new DirectoryInfo(paths.UnityPackagesDir);
            return directory.Exists
                ? directory.EnumerateFiles("*.json").Where(file => StoreIds.IsPackageHash(Path.GetFileNameWithoutExtension(file.Name))).ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
