using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Resolution;

/// <summary>
/// ファイル名の語のうち、**アバターの名前**のもの。自動検索の検索語から外し、並べ直しにだけ使う。
///
/// 衣装やアクセサリーの配布ファイルは「商品名_アバター名」の形が多い（アバターごとに zip を分けて配る）。
/// 商品名の方には英字のアバター名が入っていないことが多く、BOOTH の検索はスペースを AND で読むので、
/// アバター名が1語混ざるだけで0件になっていた（正解の分かる318本で、検索結果に正解の無かった外れの
/// 半分近くがこの形。2026-09-29）。
///
/// 名前は対応アバターの検出と同じ呼び名（<see cref="AvatarNameIndex.NamesOf"/>）から取り、
/// 「対応」「専用」「用」を落として**語そのもの**が名前か見る。「ミルティナ」の中の「ティナ」のような部分では当たらない。
///
/// 検出の索引（<see cref="AvatarNameIndex"/>）は使わない。索引は2項目以上に当たる表記を落とすが
/// （同じアバターの別版・同名の別アバター）、ここではどれかのアバターの名前であれば足りる。
/// 索引で見ると英字のアバター名が5つ落ち、並べ直しにも効かなかった（2026-09-29）。
/// </summary>
public sealed class AvatarTokens
{
    /// <summary>2文字の英字（Mo・Ai）は商品名の語と見分けられない。</summary>
    private const int MinLatinLength = 3;

    /// <summary>ローマ字の読みで名前に当てるときの最短の字数。2字（まお・えも）は英単語の読みと重なりやすい。</summary>
    private const int MinReadingLength = 3;

    /// <summary>
    /// 商品名の中から名前を探すときの、かなの名前の最短の字数。2字のかな（エク）は長い語の一部に埋まりやすい（エクストラ）。
    /// </summary>
    private const int MinKanaInTextLength = 3;

    /// <summary>名前（StripForMatch した形）→ その名前を持つアバターの商品ID。</summary>
    private readonly Dictionary<string, HashSet<string>> _names;

    /// <summary>名前全体の読み（ひらがな）→ アバターの商品ID。</summary>
    private readonly Dictionary<string, HashSet<string>> _readings;

    private AvatarTokens(Dictionary<string, HashSet<string>> names, Dictionary<string, HashSet<string>> readings)
    {
        _names = names;
        _readings = readings;
    }

    /// <summary>アバターと判定できた項目だけで組む（衣装などの項目の名前で商品名の語を落とさない）。</summary>
    /// <param name="kanjiReadings">
    /// 漢字の名前の読みを作るもの。渡せば「星羅」のような名前にもローマ字（seira）で当てる。
    /// 登録簿の名前はかな・漢字だけのことが多く、ファイル名のローマ字の名前は字面では当たらなかった（2026-09-29）。
    /// </param>
    public static AvatarTokens From(AvatarRegistry registry, Search.KanjiReadings? kanjiReadings = null)
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            foreach (var name in AvatarNameIndex.NamesOf(entry))
            {
                var key = AvatarText.StripForMatch(name);
                if (key.Length >= 2
                    && !(key.All(char.IsAscii) && key.Length < MinLatinLength)
                    && !AvatarText.IsGenericName(name)
                    && !AvatarText.IsGenericName(key))
                {
                    Add(names, key, entry.ItemId);
                }
            }
        }

        // 名前**全体**の読み。かなだけの名前はひらがなにそろえ、漢字の名前は1区間で読めるときだけ読みを作る
        // （「オリジナル3Dモデル」の一部の区間の読みで当てない）
        var readings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (key, ids) in names)
        {
            IEnumerable<string> keyReadings = key.All(IsKana)
                ? [key]
                : kanjiReadings is not null && key.All(c => IsKanji(c) || IsKana(c))
                    ? kanjiReadings.Of(key)
                    : [];

            foreach (var reading in keyReadings.Select(ToHiragana).Where(reading => reading.Length >= MinReadingLength))
            {
                foreach (var id in ids)
                {
                    Add(readings, reading, id);
                }
            }
        }

        return new AvatarTokens(names, readings);
    }

    /// <summary>この1語がアバターの名前そのものか。英字の語はローマ字の読みでも名前全体に当てる。</summary>
    public bool IsAvatarName(string token) => AvatarsNamedBy(token).Count > 0;

    /// <summary>この1語が名前そのものであるアバターの商品ID（別版・同名のアバターがあれば全部）。</summary>
    public IReadOnlyCollection<string> AvatarsNamedBy(string token)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (_names.TryGetValue(AvatarText.StripForMatch(token), out var ids))
        {
            found.UnionWith(ids);
        }

        if (token.Length >= MinLatinLength)
        {
            foreach (var reading in Search.RomajiReading.Readings(token))
            {
                if (_readings.TryGetValue(reading, out var byReading))
                {
                    found.UnionWith(byReading);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// この文（検索結果の商品名）に名前の出てくるアバターの商品ID。
    /// 英字の名前は語の境目で（「Satellite」の中の「tell」に当てない）、かなは3字以上で探す。
    /// </summary>
    public IReadOnlyCollection<string> AvatarsIn(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return found;
        }

        var folded = Nfkc.Fold(text).ToLowerInvariant();
        foreach (var (key, ids) in _names)
        {
            var hit = key.All(char.IsAscii)
                ? FileNameQuery.ContainsWord(folded, key)
                : (!key.All(IsKana) || key.Length >= MinKanaInTextLength) && folded.Contains(key, StringComparison.Ordinal);

            if (hit)
            {
                found.UnionWith(ids);
            }
        }

        return found;
    }

    private static void Add(Dictionary<string, HashSet<string>> into, string key, string id)
    {
        if (!into.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            into[key] = set;
        }

        set.Add(id);
    }

    private static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー';

    private static bool IsKanji(char c) => c is >= '一' and <= '鿿' or '々';

    private static string ToHiragana(string text)
        => string.Concat(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c));
}
