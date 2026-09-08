using System.Text;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 商品から検索対象の文字列を作る。
///
/// 正規化（NFKC＋小文字）が高くつくので、商品ごとに1度だけ作って持ち回る前提。
/// 入力1文字ごとに作り直すと、全商品ぶんの説明文を毎回畳むことになる。
/// </summary>
public static class SearchText
{
    /// <summary>
    /// 既定で探す範囲。商品名／ショップ名／サブドメイン／メモ／BOOTHタグ。
    ///
    /// 本文とパスを既定から外しているのは、当たりすぎて
    /// 「なぜこれが出たのか」が分からなくなるため。必要なときだけトグルで広げる。
    /// </summary>
    public static SearchHaystack Build(ItemRecord item)
    {
        var primary = new StringBuilder();
        Append(primary, item.Booth.Name);
        Append(primary, item.Booth.Shop?.Name);
        Append(primary, item.Booth.Shop?.Subdomain);
        Append(primary, item.Local.Memo);

        foreach (var tag in item.Booth.Tags)
        {
            Append(primary, tag);
        }

        var body = new StringBuilder();
        Append(body, item.Booth.Description);

        foreach (var section in item.Booth.H2Sections)
        {
            Append(body, section.Heading);
            Append(body, section.Text);
        }

        var paths = new StringBuilder();
        foreach (var file in item.Local.LocalFiles)
        {
            foreach (var path in file.Paths)
            {
                Append(paths, path);
            }
        }

        return new SearchHaystack
        {
            Primary = SearchQuery.Normalize(primary.ToString()),
            Body = SearchQuery.Normalize(body.ToString()),
            Paths = SearchQuery.Normalize(paths.ToString()),
        };
    }

    /// <summary>
    /// 改行で区切って繋ぐ。区切らずに繋ぐと、隣り合った値の末尾と先頭が
    /// 1つの語のように見えて、打っていない組み合わせに当たってしまう。
    /// </summary>
    private static void Append(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.Append(value).Append('\n');
        }
    }
}
