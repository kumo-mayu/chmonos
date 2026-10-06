using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chmonos.Core.Storage;

/// <summary>商品の記録1件の、額の空いた購入記録の数。</summary>
/// <param name="FileName">記録のファイルの名前（<c>{商品ID}.json</c>）。</param>
/// <param name="Count">額が空いている購入記録の数。</param>
public sealed record NullPriceHit(string FileName, int Count);

/// <summary>保存先を見た結果。</summary>
/// <param name="ItemsScanned">読んだ商品の記録の数。</param>
/// <param name="Hits">額の空いた購入記録を持つ記録。</param>
/// <param name="Unreadable">読めなかった（壊れた・途中で切れた）記録の名前。触らない。</param>
public sealed record NullPriceScan(int ItemsScanned, IReadOnlyList<NullPriceHit> Hits, IReadOnlyList<string> Unreadable)
{
    public int PurchaseCount => Hits.Sum(hit => hit.Count);
}

/// <summary>書き換えた結果。</summary>
/// <param name="Changed">書き換えた記録の名前。</param>
/// <param name="BackupDir">書き換える前の記録の控えを置いた場所。書き換えた物が無ければ作らない。</param>
public sealed record NullPriceApplied(IReadOnlyList<string> Changed, string? BackupDir);

/// <summary>
/// 購入記録の額の空欄（<c>price</c> が無い・<c>null</c>）を 0 に書き換える（ユーザ指示 2026-10-06）。
///
/// 2026-09-29 から、編集画面の額の空欄は 0円として記録している（0f39516）。それより前の版は空欄を「未入力」として
/// 書いていて（保存は <c>WhenWritingNull</c> なので <c>price</c> の欄ごと省かれる）、友人はその版で 0円のつもりで空欄にしていた。
/// 今の版はそれを「額の分からない記録」と読むので、統計で「払った額の記録が無い」に数え、払った額の範囲の絞り込みから外す。
///
/// **記録を型で読み直して書き出さない。**型に無い欄が消え、ほかの欄の形も今の版の書き方に揃ってしまう。
/// JSON の木のまま、購入記録の <c>price</c> だけを足し、ほかは1文字も変えない（書き出しは保存と同じ決まり <see cref="JsonStore.Options"/>）。
/// 読めない記録は飛ばして名前だけ返す。何度走らせても、2回目からは何も変えない。
/// </summary>
public static class NullPriceFixer
{
    /// <summary>保存先の商品の記録を読み、額の空いた購入記録を数える。何も書かない。</summary>
    public static NullPriceScan Scan(string storeRoot)
    {
        var hits = new List<NullPriceHit>();
        var unreadable = new List<string>();
        var scanned = 0;
        foreach (var path in ItemFiles(storeRoot))
        {
            var name = Path.GetFileName(path);
            if (Read(path) is not { } root)
            {
                unreadable.Add(name);
                continue;
            }

            scanned++;
            var count = NullPrices(root).Count();
            if (count > 0)
            {
                hits.Add(new NullPriceHit(name, count));
            }
        }

        return new NullPriceScan(scanned, hits, unreadable);
    }

    /// <summary>
    /// 額の空いた購入記録を 0 に書き換える。書き換える記録は、先に <paramref name="backupDir"/> へ写してから書く。
    /// 写しに失敗したら、その記録は書かない（控えの無い書き換えをしない）。
    /// </summary>
    public static NullPriceApplied Apply(string storeRoot, string backupDir)
    {
        var changed = new List<string>();
        var backupMade = false;
        foreach (var path in ItemFiles(storeRoot))
        {
            if (Read(path) is not { } root)
            {
                continue;
            }

            var targets = NullPrices(root).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            if (!backupMade)
            {
                Directory.CreateDirectory(backupDir);
                backupMade = true;
            }

            File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path)), overwrite: false);

            foreach (var purchase in targets)
            {
                SetZero(purchase);
            }

            // 保存と同じ書き方（一時ファイルから置き換える。書き込みの門も通す）。途中で落ちても、本体は前のままか書き終えた物のどちらか
            JsonStore.Write(path, root);
            changed.Add(Path.GetFileName(path));
        }

        return new NullPriceApplied(changed, backupMade ? backupDir : null);
    }

    /// <summary>
    /// 商品の記録のファイル。保存先の <c>items</c> の直下の <c>{商品ID}.json</c> だけ
    /// （控えの <c>.prev</c> の下・説明の <c>.h2.html</c> は見ない）。
    /// </summary>
    private static IEnumerable<string> ItemFiles(string storeRoot)
    {
        var dir = new AppPaths(storeRoot).ItemsDir;
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Contains(".h2", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonObject? Read(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>額の空いた購入記録：<c>local.purchases</c> の中で、<c>price</c> が無いか <c>null</c> の物。</summary>
    private static IEnumerable<JsonObject> NullPrices(JsonObject root)
    {
        if (root["local"] is not JsonObject local || local["purchases"] is not JsonArray purchases)
        {
            yield break;
        }

        foreach (var node in purchases)
        {
            if (node is JsonObject purchase && (!purchase.TryGetPropertyValue("price", out var price) || price is null))
            {
                yield return purchase;
            }
        }
    }

    /// <summary>
    /// <c>price</c> を 0 にする。欄が無ければ、今の版が書く並び（<c>nameSnapshot</c> の次・<c>kind</c> の前）に足す
    /// ——人が開いて読んだときに、ほかの記録と同じ並びに見えるように。
    /// </summary>
    private static void SetZero(JsonObject purchase)
    {
        if (purchase.ContainsKey("price"))
        {
            purchase["price"] = 0;
            return;
        }

        var keys = purchase.Select(pair => pair.Key).ToList();
        var after = keys.IndexOf("nameSnapshot");
        if (after < 0)
        {
            after = keys.IndexOf("variationId");
        }

        var index = after >= 0 ? after + 1 : keys.IndexOf("kind") is var kind and >= 0 ? kind : keys.Count;
        purchase.Insert(index, "price", 0);
    }
}
