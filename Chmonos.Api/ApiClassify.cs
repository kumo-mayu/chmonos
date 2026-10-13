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
    /// 同じ大分類が2回来たら小分類をまとめる
    /// </summary>
    public static async Task<(IReadOnlyList<UserTagAssignment> Tags, bool AddedToMaster)> ResolveTagsAsync(
        QuerySnapshot snapshot, ApiWriter writer, IEnumerable<UserTagInput> inputs, bool createMissing, CancellationToken cancellationToken)
    {
        var master = snapshot.UserTagMaster;
        var merged = new List<(string Top, List<string> Subs)>();
        var added = false;
        foreach (var input in inputs)
        {
            var topName = Required(input.Top, "top");
            var top = master.Tops.FirstOrDefault(known => Spell([known.Name], topName) is not null);
            var spelledTop = top?.Name ?? topName;
            var subs = new List<string>();
            foreach (var sub in input.Subs ?? [])
            {
                var subName = Required(sub, "subs の名前");
                var spelledSub = top is null ? null : Spell(top.Subs.Select(known => known.Name), subName);
                if (spelledSub is null)
                {
                    await AddMissingTagAsync(writer, spelledTop, subName, createMissing, cancellationToken);
                    added = true;
                }

                subs.Add(spelledSub ?? subName);
            }

            if (top is null && subs.Count == 0)
            {
                await AddMissingTagAsync(writer, spelledTop, null, createMissing, cancellationToken);
                added = true;
            }

            var index = merged.FindIndex(entry => string.Equals(entry.Top, spelledTop, StringComparison.CurrentCultureIgnoreCase));
            if (index < 0)
            {
                merged.Add((spelledTop, subs.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()));
            }
            else
            {
                merged[index].Subs.AddRange(subs.Where(sub => !merged[index].Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase)));
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

    /// <summary>属性の名前を一覧に照らす（一覧に無い名前の扱いはタグと同じ）。値は 0〜100 の整数か null（外して未評価に戻す）。</summary>
    public static async Task<(IReadOnlyList<(string Name, int? Value)> Values, bool AddedToMaster)> ResolveAttributesAsync(
        QuerySnapshot snapshot, ApiWriter writer, IReadOnlyDictionary<string, int?> values, bool createMissing, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            throw QueryException.Invalid("invalidValues", "values に属性を1つ以上書いてください。");
        }

        var known = snapshot.AttributeMaster.Attributes.Select(attribute => attribute.Name).ToList();
        var resolved = new List<(string, int?)>();
        var added = false;
        foreach (var (name, value) in values)
        {
            var trimmed = Required(name, "属性の名前");
            if (value is < 0 or > 100)
            {
                throw QueryException.Invalid("invalidValue", $"属性「{trimmed}」の値は 0〜100 の整数か null です。");
            }

            if (Spell(known, trimmed) is { } spelled)
            {
                resolved.Add((spelled, value));
                continue;
            }

            if (value is null)
            {
                // 一覧に無い属性を外すのは、何もしないのと同じ（付いていない）
                resolved.Add((trimmed, null));
                continue;
            }

            if (!createMissing)
            {
                throw QueryException.Invalid("unknownAttribute", $"属性「{trimmed}」は一覧にありません。一覧に足して付けるときは createMissing=true を付けてください。");
            }

            await writer.ExecuteAsync(new UiCommand.AddAttribute(trimmed), cancellationToken);
            added = true;
            resolved.Add((trimmed, value));
        }

        return (resolved, added);
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
