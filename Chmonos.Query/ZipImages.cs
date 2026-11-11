using System.Globalization;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>引きたい画像1つ（ファイルの CRC-32 と大きさ）。</summary>
/// <param name="Crc">CRC-32（16進8桁。zip の目次と同じ値）。</param>
/// <param name="Size">バイト数。</param>
public sealed record ZipImageKeyRef(string? Crc, long Size);

/// <summary>zip の中の絵の逆引きの要求（<c>POST /v1/zip-images/lookup</c>）。</summary>
public sealed record ZipImagesLookupRequest(IReadOnlyList<ZipImageKeyRef?>? Images);

/// <summary>同じ大きさと CRC の画像が入っている zip 1つと、その持ち主の商品。</summary>
/// <param name="Crc">送られた CRC（大文字の16進8桁に揃えた物）。</param>
/// <param name="Path">zip の中の場所。</param>
/// <param name="FileState">持ち主のファイルの状態（<see cref="FileRef.State"/> と同じ）。</param>
public sealed record ZipImageOwnerRef(string Crc, long Size, string Path, string ItemId, string ItemName, string ZipHash, string FileState);

/// <param name="Found">見つかった物（送った順・同じ画像の中は商品 ID・zip のハッシュ・場所の順）。</param>
/// <param name="Missing">どの商品の zip にも見つからなかった画像（送った順）。</param>
public sealed record ZipImagesLookupAnswer(IReadOnlyList<ZipImageOwnerRef> Found, IReadOnlyList<ZipImageKeyRef> Missing);

/// <summary>
/// zip の中の絵の目録（<c>zipimages/&lt;zip のハッシュ&gt;.json</c>。<see cref="ZipImageStore"/>）を引く表（plan 32）。
/// 照らし方は画面の「手で置いた画像を見つける」（<see cref="Core.Services.UnityProjectImages"/>）と同じ：大きさが同じで、CRC が同じ物を「同じ画像」とする。
/// 鍵は大きさと CRC を並べた 128 ビットの数（<see cref="HashKeyedIndex"/>）。
/// </summary>
public sealed class ZipImageIndex
{
    /// <summary>一度に引ける数（GUID の逆引きと同じ。1件は表を1回引くだけ）。</summary>
    public const int MaxImages = 1000;

    private readonly ZipImageStore _store;
    private readonly HashKeyedIndex _index;

    public ZipImageIndex(AppPaths paths)
    {
        _store = new ZipImageStore(paths);
        _index = new HashKeyedIndex(
            paths.ZipImagesDir,
            hash => _store.Load(hash)?.Where(row => row.CrcValue is not null && row.Size > 0).Select(row => KeyOf(row.CrcValue!.Value, row.Size)).ToList());
    }

    private static UInt128 KeyOf(uint crc, long size) => ((UInt128)(ulong)size << 32) | crc;

    /// <summary>送られた画像を読む形に揃える。CRC が16進8桁でない・大きさが1以上でない物は入力の誤り。</summary>
    public static IReadOnlyList<(uint Crc, long Size)> Normalize(IReadOnlyList<ZipImageKeyRef?>? images)
    {
        if (images is not { Count: > 0 })
        {
            throw QueryException.Invalid("invalidImages", "images に画像を1つ以上書いてください。");
        }

        if (images.Count > MaxImages)
        {
            throw QueryException.Invalid("invalidLimit", $"一度に引ける画像は {MaxImages} 件までです。");
        }

        return images.Select(image =>
            image is { Crc: { Length: 8 } crc, Size: > 0 } && uint.TryParse(crc, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
                ? (value, image.Size)
                : throw QueryException.Invalid("invalidImage", "画像は crc（16進8桁）と size（1以上）で書いてください。"))
            .ToList();
    }

    public ZipImagesLookupAnswer Lookup(QuerySnapshot snapshot, IReadOnlyList<(uint Crc, long Size)> images)
    {
        var places = _index.Find(images.Select(image => KeyOf(image.Crc, image.Size)));
        var hits = new Dictionary<UInt128, List<(string Hash, string Path)>>();
        foreach (var hash in places.Values.SelectMany(list => list).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var row in _store.Load(hash) ?? [])
            {
                if (row.CrcValue is { } crc && KeyOf(crc, row.Size) is var key && places.TryGetValue(key, out var wanted)
                    && wanted.Contains(hash, StringComparer.OrdinalIgnoreCase))
                {
                    (hits.TryGetValue(key, out var list) ? list : hits[key] = []).Add((hash, row.Path));
                }
            }
        }

        var found = new List<ZipImageOwnerRef>();
        var missing = new List<ZipImageKeyRef>();
        foreach (var (crc, size) in images.Distinct())
        {
            var text = crc.ToString("X8", CultureInfo.InvariantCulture);
            var rows = (hits.GetValueOrDefault(KeyOf(crc, size)) ?? [])
                .SelectMany(hit => snapshot.FilesByHash[hit.Hash].Select(owner => new ZipImageOwnerRef(
                    text, size, hit.Path, owner.Item.Id, owner.Item.DisplayName, owner.File.Hash, ItemMapper.FileState(owner.File))))
                .OrderBy(row => row.ItemId, StringComparer.Ordinal)
                .ThenBy(row => row.ZipHash, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Path, StringComparer.Ordinal)
                .ToList();
            if (rows.Count == 0)
            {
                missing.Add(new ZipImageKeyRef(text, size));
            }

            found.AddRange(rows);
        }

        return new ZipImagesLookupAnswer(found, missing);
    }
}
