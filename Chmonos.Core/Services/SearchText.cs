using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品から検索対象の文字列を作る。
///
/// 対象ごとの元の文字列は商品の記録から引く（写しを持たない）。畳んだ文字列は
/// <see cref="SearchHaystack"/> がその対象を初めて探すときに作って持つ。
/// </summary>
public static class SearchText
{
    /// <summary>
    /// 画面が作った文字列を、記録の物ごとに覚える表。API（アプリと同じプロセス）はここから借り、同じ商品の文字列を二重に持たない
    /// （2026-10-14 に測った：実物に近い2万件で、API で本文まで探すと二重持ちが約185MB）。
    /// 記録は書き換えないので、同じ物なら同じ文字列でよい。保存し直して物が替われば、画面が新しく覚える。
    /// 弱い表なので、記録が手放されれば文字列も一緒に消える（ここが記録を生かし続けない）
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemRecord, SearchHaystack> Remembered = new();

    /// <summary>この記録の文字列として覚える（画面が作った・使い回した物）。</summary>
    public static void Remember(ItemRecord item, SearchHaystack haystack) => Remembered.AddOrUpdate(item, haystack);

    /// <summary>この記録の物について画面が覚えた文字列。無ければ null（借りる側が自分で作る）。</summary>
    public static SearchHaystack? RememberedFor(ItemRecord item) => Remembered.TryGetValue(item, out var haystack) ? haystack : null;

    /// <param name="readings">
    /// 商品名の読みを作るもの。渡さなければ読みの欄は空になる（造語変換が効かないだけ）。
    /// </param>
    public static SearchHaystack Build(ItemRecord item, Search.KanjiReadings? readings = null)
    {
        // 字の表は、ここ（読み込みの裏のスレッド）で先に読んでおく。造語変換を初めて入れたときに
        // 画面のスレッドで KANJIDIC2 を読む（0.3秒）ことにならないように。2回目からは何もしない
        readings?.Prepare();

        // 商品名の読みは、造語変換で初めて照らすときに作る（SearchHaystack.Readings）。
        // 前は切れていても全商品の読みを作っていて、全件の作り直しが 2ms→23ms・割り当て 2MB→8MB に膨らんでいた
        // （2000件。取り込み中は10秒ごとに作り直す・2026-09-24 実測）
        Func<string>? makeReadings = readings is null ? null : () => NameReadings(item, readings);
        var haystack = new SearchHaystack(field => RawValues(item, field), makeReadings) { Item = item };

        // 既定で探す対象だけは先に畳んでおく（読み込みの裏で作り、打つたびに作らない）
        foreach (var field in SearchOptions.DefaultTargets)
        {
            _ = haystack.Folded(field);
        }

        return haystack;
    }

    /// <summary>商品名の読み（改行で区切って繋ぐ）。辞書に載っていない造語（撫で音）はここでしか作れない。</summary>
    internal static string NameReadings(ItemRecord item, Search.KanjiReadings readings)
    {
        var reading = new StringBuilder();
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

        return reading.ToString();
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

            // 条件「カテゴリ」と同じ値（自分で入れたカテゴリか BOOTH のカテゴリ、と BOOTH の親カテゴリ）
            case SearchField.Category:
                Add(item.CategoryName);
                Add(item.Booth.Category?.ParentName);
                break;
        }

        return values;
    }
}
