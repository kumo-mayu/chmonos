using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// やりかけの記録（<c>pending-operations.json</c>）に書く「指紋」の作り方（外部の点検 2026-10-06・L108）。**作り方はここ1か所。**
///
/// 記録には ID と名前しか無かったので、続きは「今の様子」だけで済んだかを読んでいた。すると
/// 操作の前から同じ値の購入を持っていた移す先を「合わせ済み」と取り違え（元の購入が1件落ちた）、
/// 消せずに残った記録が、後で同じ ID で登録し直した商品や、同じ名前で作り直したタグ・属性に当たっていた。
/// 指紋は中身から計算した値で、続きは「記録した時と同じ物か」をこれで見分ける。
///
/// 指紋は記録にだけ書き、商品の JSON には欄を足さない（人が読んで直す所に、計算で出せる値を置かない）。
/// 衝突を気にする用途ではないので、SHA-256 の頭の 64bit（16桁）で足りる——比べるのは、同じ商品・同じ一覧の前と後だけ。
/// </summary>
public static class OperationFingerprint
{
    /// <summary>
    /// 商品の <c>local</c> の指紋。
    ///
    /// 取得が書く欄（最終取得・次回予定・404の回数・販売終了の印）と、購入記録の「BOOTHにあるか」の印（取り直した
    /// バリエーションの一覧で付け直される）は数えない。どれも IDの変更が合わせる中身ではなく（移した先の値のまま）、
    /// 起動の裏の取得が続きより先に書くことがある。数えると、人が触っていないのに「変わった」と読んで続きを止めてしまう。
    /// それ以外の欄は、人が触った・取り込みが足した・作り直された、のどれでも変わるので、全部を数える。
    /// </summary>
    public static string Of(LocalBlock local) => Hash(local with
    {
        LastFetchedAt = null,
        NextFetchDueAt = null,
        ConsecutiveNotFoundCount = 0,
        IsDelisted = false,
        Purchases = [.. local.Purchases.Select(purchase => purchase with { ExistsOnBooth = true })],
    });

    /// <summary>
    /// タグの名前の変更を始める前の指紋（一覧の丸ごと・古い名前を持つ商品ごとの付け方）。
    /// <paramref name="sub"/> が null なら大分類の変更。
    /// </summary>
    public static RenameFingerprint ForUserTagRename(
        UserTagMaster master,
        IEnumerable<ItemRecord> items,
        string top,
        string? sub,
        string newName)
    {
        var target = newName.Trim();
        var holders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var holds = item.Local.UserTags.Any(assignment => sub is null
                ? IsOld(assignment.Top, top, target)
                : Same(assignment.Top, top) && assignment.Subs.Any(name => IsOld(name, sub, target)));
            if (holds)
            {
                holders[item.Id] = Hash(item.Local.UserTags);
            }
        }

        var hasOld = sub is null
            ? master.Tops.Any(entry => IsOld(entry.Name, top, target))
            : master.Tops.Any(entry => Same(entry.Name, top) && entry.Subs.Any(name => IsOld(name.Name, sub, target)));
        return new RenameFingerprint(Hash(master), hasOld, holders);
    }

    /// <summary>属性の名前の変更を始める前の指紋（一覧の丸ごと・古い名前の値を持つ商品ごとの値）。</summary>
    public static RenameFingerprint ForAttributeRename(
        AttributeMaster master,
        IEnumerable<ItemRecord> items,
        string oldName,
        string newName)
    {
        var target = newName.Trim();
        var holders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Local.Attributes.Keys.Any(key => IsOld(key, oldName, target)))
            {
                holders[item.Id] = Hash(item.Local.Attributes);
            }
        }

        var hasOld = master.Attributes.Any(entry => IsOld(entry.Name, oldName, target));
        return new RenameFingerprint(Hash(master), hasOld, holders);
    }

    /// <summary>
    /// 名前の変更の続きを当ててよいか（記録した指紋と、今の指紋を比べる）。
    ///
    /// 段は ①一覧 → ②商品を1件ずつ → ③保存した検索・設定。
    /// - 一覧に古い名前が今もあるなら、①はまだ——一覧は丸ごと記録した時のままのはず。違えば、人が触ったか、
    ///   済んだ後で同じ名前を作り直した（中身だけでは作り直した物と見分けられないので、一覧の丸ごとで見る）。
    /// - 古い名前を持つ商品は、記録した時に持っていた商品で、付け方もその時のままのはず（書き換え済みの商品は古い名前を持たない）。
    ///   記録に無い商品が持つ・付け方が違う物は、後で付けた名前なので当てない。
    /// </summary>
    public static bool AllowsResume(RenameFingerprint recorded, RenameFingerprint current)
    {
        if (current.MasterHasOldName && current.Master != recorded.Master)
        {
            return false;
        }

        return current.Holders.All(holder =>
            recorded.Holders.TryGetValue(holder.Key, out var before) && before == holder.Value);
    }

    /// <summary>
    /// 古い名前か。大文字と小文字だけの変更（vrchat → VRChat）は、変えた後の名前も同じ名前に見えるので、新しい綴りそのものは外す。
    /// </summary>
    private static bool IsOld(string name, string oldName, string newName)
        => Same(name, oldName) && !string.Equals(name, newName, StringComparison.Ordinal);

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// 保存と同じ書き方（<see cref="JsonStore.Options"/>）で書き出し、欄の並びを名前順にそろえてから計算する。
    /// 辞書（役割・属性）の並びは読み書きで変わり得るが、中身は同じなので同じ指紋にする。
    /// </summary>
    private static string Hash<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonStore.Options);
        var text = Canonical(node)?.ToJsonString() ?? "null";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(digest.AsSpan(0, 8));
    }

    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, Canonical(pair.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Canonical)]),
        null => null,
        _ => node.DeepClone(),
    };
}

/// <summary>
/// 名前の変更の指紋。<see cref="Master"/> は一覧の丸ごと、<see cref="Holders"/> は古い名前を持つ商品ごとの付け方（商品ID → 指紋）。
/// </summary>
/// <param name="MasterHasOldName">一覧に古い名前があったか（記録には書かない。今の様子と比べるときに使う）。</param>
public sealed record RenameFingerprint(string Master, bool MasterHasOldName, IReadOnlyDictionary<string, string> Holders);
