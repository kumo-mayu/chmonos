using BoothAssetManager.Core.Search;

namespace BoothAssetManager.Core.Resolution;

/// <summary>
/// ファイル名と商品名が「読みで」一致しているかを見る。
///
/// ラテン文字のファイル名は、日本語商品の**ローマ字表記**であることが多い
/// （`tori_v1.zip` → 『Bird/鳥』、`SinAvatarPen.zip` → 『真・アバターペンシステム』）。
/// 文字を突き合わせるだけでは当たらないので、読みに直してから比べる。
///
/// **英訳の側は見ない。**作者は `bird_v1.zip` とは名付けない。実測でも、
/// 英語の経路は `Sin`→罪業、`Ring`→土俵 のように誤った語しか作らなかった。
///
/// ここは<b>既に取ってある商品名</b>と比べるだけなので、通信は増えない。
/// </summary>
public static class ReadingMatch
{
    /// <summary>照合に使う読みの最短の長さ。短いと無関係な語に当たる。</summary>
    private const int MinReadingLength = 2;

    /// <summary>
    /// このファイル名の語が、この商品名と読みで一致するか。
    /// 一致した読みを返す（根拠としてそのまま画面に出せる）。
    /// </summary>
    public static string? Find(
        string query,
        string itemName,
        SearchBridge? bridge,
        KanjiReadings? readings)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        var nameReadings = readings?.Of(itemName) ?? [];

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!RomajiReading.LooksRomaji(token) || token.Length < MinReadingLength)
            {
                continue;
            }

            // ① 読みから作った表記が、商品名にそのまま出てくるか。
            //    とり → 鳥 が『Bird/鳥』に出てくる、さめ → サメ が『サメっ子』に出てくる
            if (bridge is not null)
            {
                foreach (var candidate in bridge.Expand(token))
                {
                    if (candidate.Via == BridgeRoute.English)
                    {
                        continue;
                    }

                    if (candidate.Text.Length >= MinReadingLength
                        && itemName.Contains(candidate.Text, StringComparison.Ordinal))
                    {
                        return candidate.Text;
                    }
                }
            }

            // ② 商品名の読みと、ファイル名の読みが重なるか。
            //    辞書に無い造語はこちらでしか当たらない（Sin ↔ 真、nadeoto ↔ 撫で音）
            foreach (var reading in RomajiReading.Readings(token))
            {
                if (reading.Length < MinReadingLength)
                {
                    continue;
                }

                foreach (var nameReading in nameReadings)
                {
                    if (nameReading.Contains(reading, StringComparison.Ordinal))
                    {
                        return reading;
                    }
                }
            }
        }

        return null;
    }
}
