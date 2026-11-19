namespace Chmonos.Core.Models;

/// <summary>
/// 商品に付いた属性の3つの状態（付いていない／付いているが値はまだ／値がある）の決まりを1か所に置く
/// （ユーザ判断 2026-11-20・`docs/spec/data-model.md`「属性」）。
///
/// 値は <see cref="LocalBlock.Attributes"/>、値はまだの名前は <see cref="LocalBlock.PendingAttributes"/> に持つ。
/// 書き手ごとに両方を直すと、片方だけ直して「値を外したのに値はまだが戻る」などが起きるので、書くときはここを通す。
/// 名前の比べ方は <see cref="AttributeNames.Comparer"/>（大文字と小文字は同じ属性）。
/// </summary>
public static class ItemAttributes
{
    /// <summary>付いているか（値がある・値はまだのどちらか）。</summary>
    public static bool IsAttached(LocalBlock local, string name)
        => local.Attributes.ContainsKey(name) || IsPending(local, name);

    /// <summary>付いているが値はまだか。</summary>
    public static bool IsPending(LocalBlock local, string name)
        => local.PendingAttributes?.Contains(name, AttributeNames.Comparer) == true;

    /// <summary>値はまだの名前（無ければ空）。</summary>
    public static IReadOnlyList<string> Pending(LocalBlock local) => local.PendingAttributes ?? [];

    /// <summary>
    /// 値を付けずに足す。既に付いている（値がある・値はまだ）名前は何もしない。
    /// 足す物が無ければ null（呼んだ側は書かずに済ませ、件数で知らせる）。
    /// </summary>
    public static LocalBlock? AddPending(LocalBlock local, IEnumerable<string> names)
    {
        var pending = Pending(local).ToList();
        var added = false;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || IsAttachedIn(local.Attributes, pending, name))
            {
                continue;
            }

            pending.Add(name);
            added = true;
        }

        return added ? local with { PendingAttributes = pending } : null;
    }

    /// <summary>値を付ける。値はまだの名前からは外す（値の方へ移る）。</summary>
    public static LocalBlock WithValue(LocalBlock local, string name, int value)
    {
        var table = new Dictionary<string, int>(local.Attributes, AttributeNames.Comparer) { [name] = value };
        return local with { Attributes = table, PendingAttributes = Except(Pending(local), name) };
    }

    /// <summary>外して付いていない状態に戻す（値も、値はまだも）。付いていなければ null。</summary>
    public static LocalBlock? Without(LocalBlock local, string name)
    {
        if (!IsAttached(local, name))
        {
            return null;
        }

        var table = new Dictionary<string, int>(local.Attributes, AttributeNames.Comparer);
        table.Remove(name);
        return local with { Attributes = table, PendingAttributes = Except(Pending(local), name) };
    }

    /// <summary>
    /// 値はまだの名前を付け替える（属性の改名・統合）。古い名前が無ければ null。
    /// 新しい名前が既に付いていれば、古い方を落とすだけ（値がある側が勝つ。2回当てても同じ結果）。
    /// </summary>
    public static IReadOnlyList<string>? RenamePending(IReadOnlyList<string>? pending, string oldName, string newName)
    {
        if (pending is null || !pending.Contains(oldName, AttributeNames.Comparer))
        {
            return null;
        }

        var result = new List<string>(pending.Count);
        foreach (var name in pending)
        {
            var renamed = AttributeNames.Comparer.Equals(name, oldName) ? newName : name;
            if (!result.Contains(renamed, AttributeNames.Comparer))
            {
                result.Add(renamed);
            }
        }

        return result;
    }

    /// <summary>名前を1つ落とす。空になれば null（書き出さない）。</summary>
    public static IReadOnlyList<string>? Except(IReadOnlyList<string>? pending, string name)
    {
        if (pending is null)
        {
            return null;
        }

        var rest = pending.Where(entry => !AttributeNames.Comparer.Equals(entry, name)).ToList();
        return rest.Count == 0 ? null : rest;
    }

    /// <summary>
    /// 読み取り口の形：値のある名前を除き、空なら null。手で両方に書いた JSON でも値が勝つ。
    /// ほとんどの商品は値はまだを持たないので、その時は何も作らずに返す（全件の検索で呼ばれる）。
    /// </summary>
    internal static IReadOnlyList<string>? Visible(IReadOnlyList<string>? stored, IReadOnlyDictionary<string, int> values)
    {
        if (stored is null || stored.Count == 0)
        {
            return null;
        }

        if (values.Count == 0 || !stored.Any(values.ContainsKey))
        {
            return stored;
        }

        var rest = stored.Where(name => !values.ContainsKey(name)).ToList();
        return rest.Count == 0 ? null : rest;
    }

    /// <summary>
    /// 書き込み口の形：同じ名前（大文字と小文字だけ違う物を含む）は先の方を残し、空の名前と空の一覧は落とす。
    /// 手で書いた <c>null</c> も空として受ける。
    /// </summary>
    internal static IReadOnlyList<string>? Distinct(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, AttributeNames.Comparer))
            {
                result.Add(name);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static bool IsAttachedIn(IReadOnlyDictionary<string, int> values, List<string> pending, string name)
        => values.ContainsKey(name) || pending.Contains(name, AttributeNames.Comparer);
}
