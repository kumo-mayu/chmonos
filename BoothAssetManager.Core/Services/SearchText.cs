using System.Text;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 商品から検索対象の文字列を作る。
///
/// 対象ごとの元の文字列は商品の記録から引く（写しを持たない）。畳んだ文字列は
/// <see cref="SearchHaystack"/> がその対象を初めて探すときに作って持つ。
/// </summary>
public static class SearchText
{
    /// <param name="readings">
    /// 商品名の読みを作るもの。渡さなければ読みの欄は空になる（造語変換が効かないだけ）。
    /// </param>
    public static SearchHaystack Build(ItemRecord item, Search.KanjiReadings? readings = null)
    {
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
                    reading.Append(text).Append('\n');
                }
            }
        }

        var haystack = new SearchHaystack(field => RawValues(item, field), SearchQuery.Normalize(reading.ToString()));

        // 既定で探す対象だけは先に畳んでおく（読み込みの裏で作り、打つたびに作らない）
        foreach (var field in SearchOptions.DefaultTargets)
        {
            _ = haystack.Folded(field);
        }

        return haystack;
    }

    /// <summary>対象ごとの元の文字列。空の値は含めない。</summary>
    public static IReadOnlyList<string> RawValues(ItemRecord item, SearchField field)
    {
        var values = new List<string>();

        void Add(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        switch (field)
        {
            case SearchField.Name:
                // 名前は両方入れる。ユーザが付けた名前でも、BOOTHの名前でも探せるように
                Add(item.Local.DisplayName);
                Add(item.Booth.Name);
                break;

            case SearchField.Shop:
                Add(item.Local.Shop?.Name);
                Add(item.Booth.Shop?.Name);
                break;

            case SearchField.Subdomain:
                Add(item.Booth.Shop?.Subdomain);

                // 手で名前だけ入れたショップの鍵（local-…）は、人が打つサブドメインではない
                if (item.Local.Shop is { } shop && !LocalShopKey.IsLocal(shop.Subdomain))
                {
                    Add(shop.Subdomain);
                }

                break;

            case SearchField.Memo:
                Add(item.Local.Memo);
                break;

            case SearchField.Main:
                Add(item.Booth.Description);
                foreach (var section in item.Booth.H2Sections)
                {
                    Add(section.Heading);
                    Add(section.Text);
                }

                break;

            case SearchField.Path:
                foreach (var file in item.Local.OwnedFiles)
                {
                    foreach (var path in file.Paths)
                    {
                        Add(path);
                    }
                }

                foreach (var folder in item.Local.LocalFolders)
                {
                    Add(folder.Path);
                }

                break;

            case SearchField.Id:
                Add(item.Id);
                break;

            case SearchField.Variation:
                foreach (var variation in item.Booth.Variations)
                {
                    Add(variation.Name);
                }

                // BOOTH から消えた種類も、買ったときの名前で探せるように
                foreach (var purchase in item.Local.Purchases)
                {
                    Add(purchase.NameSnapshot);
                }

                break;

            case SearchField.File:
                foreach (var file in item.Local.OwnedFiles)
                {
                    foreach (var path in file.Paths)
                    {
                        Add(System.IO.Path.GetFileName(path));
                    }
                }

                foreach (var folder in item.Local.LocalFolders)
                {
                    Add(System.IO.Path.GetFileName(folder.Path.TrimEnd('\\', '/')));
                }

                break;

            case SearchField.Content:
                foreach (var file in item.Local.OwnedFiles)
                {
                    foreach (var entry in file.Contents)
                    {
                        Add(entry);
                    }
                }

                break;

            case SearchField.Tag:
                foreach (var tag in item.Booth.Tags)
                {
                    Add(tag);
                }

                break;
        }

        return values;
    }
}
