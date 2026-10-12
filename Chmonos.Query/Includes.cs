namespace Chmonos.Query;

/// <summary>
/// 商品1件の答えに、頼まれたときだけ足す物（<c>?include=avatarLinks,allFiles</c>）。2026-10-12・ユーザ判断：
/// 既定の答えは結果だけにし、どう決まったか・人が何を打ち消したか・長くなる一覧は頼まれたときだけ返す。
/// </summary>
[Flags]
public enum ItemIncludes
{
    None = 0,

    /// <summary>対応アバターの紐付けの帳簿（出どころ・確認済み・候補・消した、検出した日時）。</summary>
    AvatarLinks = 1,

    /// <summary>外した・古い版のファイルも返す（既定は「ある」「見つからない」だけ）。</summary>
    AllFiles = 2,

    /// <summary>zip の中のファイルの一覧。</summary>
    Contents = 4,

    /// <summary>画像の全部（既定はサムネイルだけ）。</summary>
    Images = 8,

    /// <summary>記録の古さ（BOOTH の情報を最後に取った日時）。</summary>
    Meta = 16,
}

/// <summary><c>include</c> の名前を読む。名前は外との約束。知らない名前は黙って飛ばさず誤りで返す（綴りの間違いに気付けるように）。</summary>
public static class Includes
{
    public static IReadOnlyDictionary<string, ItemIncludes> ItemNames { get; } = new Dictionary<string, ItemIncludes>(StringComparer.Ordinal)
    {
        ["avatarLinks"] = ItemIncludes.AvatarLinks,
        ["allFiles"] = ItemIncludes.AllFiles,
        ["contents"] = ItemIncludes.Contents,
        ["images"] = ItemIncludes.Images,
        ["meta"] = ItemIncludes.Meta,
    };

    public static ItemIncludes ParseItem(string? text)
        => Names(text, ItemNames.Keys).Aggregate(ItemIncludes.None, (all, name) => all | ItemNames[name]);

    /// <summary>商品以外の答えの <c>include</c>（改変の <c>detached</c>・共通素体の <c>rejected</c>）。名前が1つの物は、あるかだけを返す。</summary>
    public static bool ParseFlag(string? text, string name) => Names(text, [name]).Count > 0;

    private static IReadOnlyList<string> Names(string? text, IEnumerable<string> known)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowed = known.ToList();
        foreach (var name in names)
        {
            if (!allowed.Contains(name))
            {
                throw QueryException.Invalid("unknownInclude", $"include に使えない名前です: {name}（{string.Join("・", allowed)}）");
            }
        }

        return names;
    }
}
