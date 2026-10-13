using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 分類の書き込み（タグ・属性・メモ。docs/history/headless-core-2026-10-13.md 段3b）。
/// 名前は一覧（マスタ）の綴りに寄せる（大文字小文字・前後の空白の違いで別の名前を作らない。画面の「一覧に足す」と同じ比べ方）。
/// 一覧に無い名前は既定で断る（参照先の無いタグを作らない）。<c>createMissing</c> なら一覧に足してから付ける（ユーザ判断 2026-10-13）
/// </summary>
internal static class ApiClassify
{
    /// <summary>一覧の綴り。無ければ null。</summary>
    private static string? Spell(IEnumerable<string> names, string name)
        => names.FirstOrDefault(known => string.Equals(known, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private static string Required(string? name, string field)
        => string.IsNullOrWhiteSpace(name) ? throw QueryException.Invalid("invalidName", $"{field} を書いてください。") : name.Trim();

    /// <summary>
    /// タグの名前を一覧に照らし、一覧の綴りにした付け方を返す。一覧に無い名前は、<paramref name="createMissing"/> なら一覧に足し、そうでなければ 400。
    /// 同じ大分類が2回来たら小分類をまとめる。
    /// **一覧は保存先の今の物を読む**（API の写しは取り込みの間は古いまま。古い一覧で決めると、一覧と商品で綴りが分かれた。点検37）。
    /// 足した後は読み直して、一覧が決めた綴りで付ける（大文字小文字だけ違う名前が先に足されていれば、そちらに寄る）
    /// </summary>
    public static async Task<(IReadOnlyList<UserTagAssignment> Tags, bool AddedToMaster)> ResolveTagsAsync(
        Func<UserTagMaster> loadMaster, ApiWriter writer, IReadOnlyList<UserTagInput?> inputs, bool createMissing, CancellationToken cancellationToken)
    {
        // 入力の確かめは、一覧を変える前にまとめて行う（null の要素は入力の誤り。前は 500 になった。点検37）
        var wanted = inputs.Select(input => input is null
                ? throw QueryException.Invalid("invalidTags", "tags に null は書けません。")
                : (Top: Required(input.Top, "top"), Subs: (input.Subs ?? []).Select(sub => Required(sub, "subs の名前")).ToList()))
            .ToList();

        var master = loadMaster();
        var added = false;
        foreach (var (top, subs) in wanted)
        {
            var known = master.Tops.FirstOrDefault(entry => Spell([entry.Name], top) is not null);
            var missing = subs.Where(sub => known is null || Spell(known.Subs.Select(entry => entry.Name), sub) is null).ToList();
            if (known is null && missing.Count == 0)
            {
                await AddMissingTagAsync(writer, top, null, createMissing, cancellationToken);
                added = true;
            }

            foreach (var sub in missing)
            {
                await AddMissingTagAsync(writer, known?.Name ?? top, sub, createMissing, cancellationToken);
                added = true;
            }
        }

        if (added)
        {
            master = loadMaster();
        }

        var merged = new List<(string Top, List<string> Subs)>();
        foreach (var (top, subs) in wanted)
        {
            var known = master.Tops.FirstOrDefault(entry => Spell([entry.Name], top) is not null);
            var spelledTop = known?.Name ?? top;
            var spelledSubs = subs.Select(sub => (known is null ? null : Spell(known.Subs.Select(entry => entry.Name), sub)) ?? sub);
            var index = merged.FindIndex(entry => string.Equals(entry.Top, spelledTop, StringComparison.CurrentCultureIgnoreCase));
            if (index < 0)
            {
                merged.Add((spelledTop, spelledSubs.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()));
            }
            else
            {
                merged[index].Subs.AddRange(spelledSubs.Where(sub => !merged[index].Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase)));
            }
        }

        return (merged.Select(entry => new UserTagAssignment { Top = entry.Top, Subs = entry.Subs }).ToList(), added);
    }

    private static async Task AddMissingTagAsync(ApiWriter writer, string top, string? sub, bool createMissing, CancellationToken cancellationToken)
    {
        if (!createMissing)
        {
            throw QueryException.Invalid(
                "unknownUserTag",
                $"ユーザータグ「{(sub is null ? top : $"{top} / {sub}")}」は一覧にありません。一覧に足して付けるときは createMissing=true を付けてください。");
        }

        await writer.ExecuteAsync(new UiCommand.AddUserTag(top, sub), cancellationToken);
    }

    /// <summary>
    /// 属性の名前を一覧に照らす（一覧に無い名前の扱いと、保存先の今の一覧を読むのはタグと同じ）。値は 0〜100 の整数か null（外して未評価に戻す）。
    /// </summary>
    public static async Task<(IReadOnlyList<(string Name, int? Value)> Values, bool AddedToMaster)> ResolveAttributesAsync(
        Func<AttributeMaster> loadMaster, ApiWriter writer, IReadOnlyDictionary<string, int?> values, bool createMissing, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            throw QueryException.Invalid("invalidValues", "values に属性を1つ以上書いてください。");
        }

        var wanted = values.Select(pair => (Name: Required(pair.Key, "属性の名前"), pair.Value)).ToList();
        foreach (var (name, value) in wanted)
        {
            if (value is < 0 or > 100)
            {
                throw QueryException.Invalid("invalidValue", $"属性「{name}」の値は 0〜100 の整数か null です。");
            }
        }

        List<string> Known() => loadMaster().Attributes.Select(attribute => attribute.Name).ToList();
        var known = Known();
        var added = false;
        foreach (var (name, value) in wanted)
        {
            // 一覧に無い属性を外すのは、何もしないのと同じ（付いていない）
            if (value is null || Spell(known, name) is not null)
            {
                continue;
            }

            if (!createMissing)
            {
                throw QueryException.Invalid("unknownAttribute", $"属性「{name}」は一覧にありません。一覧に足して付けるときは createMissing=true を付けてください。");
            }

            await writer.ExecuteAsync(new UiCommand.AddAttribute(name), cancellationToken);
            added = true;
        }

        if (added)
        {
            known = Known();
        }

        return (wanted.Select(entry => (Spell(known, entry.Name) ?? entry.Name, entry.Value)).ToList(), added);
    }

    /// <summary>タグを1つ足す（大分類だけなら大分類だけ。既にあれば何も変えない）。</summary>
    public static LocalBlock AddTag(LocalBlock local, string top, string? sub)
    {
        var tags = local.UserTags.ToList();
        var index = tags.FindIndex(tag => string.Equals(tag.Top, top, StringComparison.CurrentCultureIgnoreCase));
        if (index < 0)
        {
            tags.Add(new UserTagAssignment { Top = top, Subs = sub is null ? [] : [sub] });
        }
        else if (sub is not null && !tags[index].Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase))
        {
            tags[index] = tags[index] with { Subs = [.. tags[index].Subs, sub] };
        }

        return local with { UserTags = tags };
    }

    /// <summary>タグを1つ外す（小分類を名指ししたらその小分類だけ、無ければ大分類ごと。付いていなければ何も変えない）。</summary>
    public static LocalBlock RemoveTag(LocalBlock local, string top, string? sub)
    {
        var tags = local.UserTags
            .Select(tag => !string.Equals(tag.Top, top.Trim(), StringComparison.CurrentCultureIgnoreCase)
                ? tag
                : sub is null
                    ? null
                    : tag with { Subs = tag.Subs.Where(known => !string.Equals(known, sub.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList() })
            .OfType<UserTagAssignment>()
            .ToList();
        return local with { UserTags = tags };
    }

    /// <summary>名指しした属性だけ書く（null は外して未評価に戻す。キーが無いのと 0 は別）。ほかの属性はそのまま。</summary>
    public static LocalBlock ApplyAttributes(LocalBlock local, IReadOnlyList<(string Name, int? Value)> values)
    {
        var table = new Dictionary<string, int>(local.Attributes, AttributeNames.Comparer);
        foreach (var (name, value) in values)
        {
            if (value is { } set)
            {
                table[name] = set;
            }
            else
            {
                table.Remove(name);
            }
        }

        return local with { Attributes = table };
    }
}
