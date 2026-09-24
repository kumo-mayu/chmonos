using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>手で直した JSON の食い違い1件。</summary>
public sealed record HandEditIssue
{
    /// <summary>どのファイルの話か（人が開ける名前で書く）。</summary>
    public required string Where { get; init; }

    /// <summary>何が食い違っているか。**直し方が分かる言い方**で書く。</summary>
    public required string What { get; init; }
}

/// <summary>
/// **手で直した JSON の食い違いを見つけて知らせる**（ユーザ判断 2026-09-21・J2/L6）。
///
/// JSONは人が開いて直せる形を保つ方針なので、手で直した結果おかしくなることは起きる。
/// 起きたときに**黙って落ちない・黙って別のファイルに書かない**ことと、
/// 「どこの何がどう食い違っているか」を人に伝えることを分けて持つ。
///
/// **こちらから直さない**（`docs/spec/data-model.md`：公開前は合っていないデータの方を問題にする）。
/// 直し方（消す・書き換える・取り込み直す）は人が決める。
/// </summary>
public static class HandEditCheck
{
    /// <summary>同じ鍵が2つ以上あるものを探す。大文字小文字・かなの違いは同じ鍵として見る（照合がそうなっているため）。</summary>
    private static IReadOnlyList<string> Duplicates(IEnumerable<string> names)
        => names
            .GroupBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}（{group.Count()} 件）")
            .ToList();

    private static string Join(IReadOnlyList<string> names)
        => names.Count <= 4
            ? string.Join("・", names)
            : string.Join("・", names.Take(4)) + $"…ほか {names.Count - 4} 件";

    /// <summary>
    /// マスタ・登録簿・商品のファイル名を見て、食い違いを集める。
    ///
    /// 見るのは**落ちる原因になる物**だけ：同じ鍵が2つあると画面が例外で止まり、
    /// 商品IDとファイル名がずれていると、以後その商品への保存が別のファイルに書かれる。
    /// </summary>
    public static async Task<IReadOnlyList<HandEditIssue>> FindAsync(
        DataStore store,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<HandEditIssue>();

        void Add(string where, string what, IReadOnlyList<string> names)
        {
            if (names.Count > 0)
            {
                issues.Add(new HandEditIssue { Where = where, What = $"{what}：{Join(names)}" });
            }
        }

        var tags = store.UserTags.Load();
        Add("userTags.json", "同じ名前の大分類が2つ以上あります", Duplicates(tags.Tops.Select(top => top.Name)));
        foreach (var top in tags.Tops)
        {
            Add("userTags.json", $"「{top.Name}」の中に同じ名前の小分類が2つ以上あります",
                Duplicates(top.Subs.Select(sub => sub.Name)));
        }

        Add("attributes.json", "同じ名前の属性が2つ以上あります",
            Duplicates(store.Attributes.Load().Attributes.Select(definition => definition.Name)));

        var registry = store.Avatars.Load();
        Add("avatar-registry.json", "同じ商品IDの行が2つ以上あります",
            Duplicates(registry.Entries.Select(entry => entry.ItemId)));
        Add("avatar-registry.json", "同じ名前の共通素体が2つ以上あります",
            Duplicates(registry.BaseGroups.Select(group => group.Name)));

        foreach (var entry in registry.Entries)
        {
            Add("avatar-registry.json", $"「{entry.ItemId}」に同じ別名が2つ以上あります",
                Duplicates(entry.Aliases.Select(alias => alias.Text)));
        }

        // 商品IDとファイル名のずれ（L6）。**書く先はJSONの中のIDなので、ずれていると
        // 以後の保存が別のファイルに書かれ、画面の変更が反映されない**（元のファイルは古いまま残る）
        // 全件の読み込みと同じ写しから引く（起動のたびに2000件を読み直していた）。読めないファイルは「読めなかった商品」として別に数えている
        var mismatched = (await store.Items.FindMisnamedAsync(cancellationToken))
            .Select(pair => $"{pair.FileId}.jsonの中のidが「{pair.ItemId}」")
            .ToList();

        Add("items/", "ファイル名と中の商品IDが違います", mismatched);

        return issues;
    }
}
