using Chmonos.Core.Search;

namespace Chmonos.Core.Resolution;

/// <summary>
/// ファイル名と商品名が「読みで」一致しているかを見る。
///
/// ラテン文字のファイル名は、日本語商品の**ローマ字表記**であることが多い
/// （`tori_v1.zip` → 『Bird/鳥』、`SinAvatarPen.zip` → 『真・アバターペンシステム』）。
/// 文字を突き合わせるだけでは当たらないので、読みに直してから比べる。
///
/// 英訳の側も見る。`shark_avatar.zip` と『サメっ子』は当たってほしい組で、
/// 外れ（`Sin`→罪業）が混じっても**ここでは通信が増えない**——
/// 既に取ってある商品名と比べるだけなので、外れは「当たらない」で終わる。
/// （BOOTHへ引き直す側は1語ごとに通信が増えるので、そちらでは順序を付けている）
/// </summary>
public static class ReadingMatch
{
    /// <summary>照合に使う読みの最短の長さ。短いと無関係な語に当たる。</summary>
    private const int MinReadingLength = 2;

    /// <summary>
    /// このファイル名の語が、この商品名と読みで一致するか。
    /// 一致した読みを返す（根拠としてそのまま画面に出せる）。
    /// </summary>
    /// <param name="extraTokens">
    /// 分かち書きにする前の綴り。<c>heartbeat</c> は割ると 心臓・拍 にしかならないが、
    /// 割らずに引くと 心音 が出る。
    /// </param>
    public static string? Find(
        string query,
        string itemName,
        SearchBridge? bridge,
        KanjiReadings? readings,
        IReadOnlyList<string>? extraTokens = null)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        var nameReadings = readings?.Of(itemName) ?? [];
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Concat(extraTokens ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            if (!RomajiReading.LooksRomaji(token) || token.Length < MinReadingLength)
            {
                continue;
            }

            // ① 別表記が商品名にそのまま出てくるか。
            //    とり → 鳥 が『Bird/鳥』に、さめ → サメ が『サメっ子』に出てくる
            if (bridge is not null)
            {
                foreach (var candidate in bridge.Expand(token))
                {
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
