// JapaneseBridgeProbe
// ローマ字/英語のファイル名トークンを日本語の検索語候補に変換する「橋渡し」の実験ツール。
// すべてキー不要の資源だけで構成している:
//   1. ローマ字 → ひらがな : NuGet MyNihongo.KanaConverter（オフライン）
//   2. ひらがな → 漢字/カタカナ候補 : Google Transliterate API (非公式・キー不要)
//   3. 英語 → 日本語語彙 : Jisho API (https://jisho.org/api/v1/search/words)
//
// 使い方:
//   dotnet run --project experiments/JapaneseBridgeProbe -- tamakurage heartbeat ring tori "fountain pen"
//
// 実測（本リポジトリの調査時）:
//   tamakurage → たまくらげ → タマクラゲ（BOOTH検索 rank 1）
//   heartbeat  → Jisho: 心拍/心音 →「心音ギミック」（rank 1）
//   ring       → Jisho: 指輪 →「指輪モデル」→ JSON検証で Ⅶ を持つ 1 件に絞れた
//   tori       → とり → 鳥 →「鳥 3Dモデル」（rank 1）
// 注意: 非日本語の単語（kipfel 等）はローマ字変換が崩れるため、候補の一つとして扱い検証で落とす前提。
// 注意: HTTP で日本語を送るときは必ず UTF-8 でURLエンコードする（Windows の bash/curl は CP932 で壊れる）。

using System.Net.Http;
using System.Text;
using System.Text.Json;
using MyNihongo.KanaConverter;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: JapaneseBridgeProbe <word> [<word> ...]");
    return 1;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) BoothZipInspector-experiment");

foreach (var word in args)
{
    Console.WriteLine($"=== {word}");

    // 1. ローマ字 → ひらがな（認識できない文字はスキップ）
    string? hiragana = null;
    try { hiragana = word.ToLowerInvariant().ToHiragana(UnrecognisedCharacterPolicy.Skip); }
    catch (Exception ex) { Console.WriteLine($"  hiragana: ERR {ex.GetType().Name}"); }
    if (!string.IsNullOrEmpty(hiragana))
        Console.WriteLine($"  hiragana : {hiragana}");

    // 2. ひらがな → 漢字/カタカナ候補（Google Transliterate）
    if (!string.IsNullOrEmpty(hiragana))
    {
        try
        {
            var url = "https://www.google.com/transliterate?langpair=ja-Hira|ja&text=" + Uri.EscapeDataString(hiragana);
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var candidates = new List<string>();
            foreach (var segment in doc.RootElement.EnumerateArray())
                candidates.Add(string.Join("/", segment[1].EnumerateArray().Take(4).Select(c => c.GetString())));
            Console.WriteLine($"  transliterate : {string.Join(" | ", candidates)}");
        }
        catch (Exception ex) { Console.WriteLine($"  transliterate: ERR {ex.Message}"); }
    }

    // 3. 英語（またはローマ字）→ 日本語語彙（Jisho）
    try
    {
        var url = "https://jisho.org/api/v1/search/words?keyword=" + Uri.EscapeDataString(word);
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var words = new List<string>();
        foreach (var data in doc.RootElement.GetProperty("data").EnumerateArray().Take(5))
        {
            var jp = data.GetProperty("japanese")[0];
            var w = jp.TryGetProperty("word", out var wv) ? wv.GetString() : null;
            var r = jp.TryGetProperty("reading", out var rv) ? rv.GetString() : null;
            words.Add(w is null ? r ?? "?" : $"{w}({r})");
        }
        Console.WriteLine($"  jisho : {(words.Count == 0 ? "(none)" : string.Join(" ", words))}");
    }
    catch (Exception ex) { Console.WriteLine($"  jisho: ERR {ex.Message}"); }
}
return 0;
