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
    /// <param name="readings">
    /// 商品名の読みを作るもの。渡さなければ読みの欄は空になる（表記をまたぐ検索が効かないだけ）。
    /// </param>
    public static SearchHaystack Build(ItemRecord item, Search.KanjiReadings? readings = null)
    {
        var primary = new StringBuilder();

        // 名前は両方入れる。ユーザが付けた名前でも、BOOTHの名前でも探せるように
        Append(primary, item.Local.DisplayName);
        Append(primary, item.Booth.Name);
        // ショップも両方入れる。自分で入れた名前でも、BOOTHの名前でも探せるように
        Append(primary, item.Local.Shop?.Name);
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

        // 商品名の読み。辞書に載っていない造語（撫で音）はここでしか作れない
        var reading = new StringBuilder();
        if (readings is not null)
        {
            foreach (var name in new[] { item.Local.DisplayName, item.Booth.Name })
            {
                if (name is not { Length: > 0 })
                {
                    continue;
                }

                foreach (var text in readings.Of(name))
                {
                    Append(reading, text);
                }
            }
        }

        return new SearchHaystack
        {
            Primary = SearchQuery.Normalize(primary.ToString()),
            Body = SearchQuery.Normalize(body.ToString()),
            Paths = SearchQuery.Normalize(paths.ToString()),
            Readings = SearchQuery.Normalize(reading.ToString()),
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
