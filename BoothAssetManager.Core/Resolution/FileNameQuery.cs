using System.Text.RegularExpressions;

namespace BoothAssetManager.Core.Resolution;

/// <summary>
/// ファイル名から検索語を作る。
///
/// 配布ファイル名は「商品名＋バージョン＋配布形態」で構成されていることが多いので
/// （<c>Kipfel_1.2.0.zip</c> / <c>Wendy_ver1.01.zip</c> / <c>F_撫で音ギミック5_10_Append.zip</c>）、
/// バージョン部分を落として商品名らしい部分だけを残す。
///
/// BOOTH内検索はスペースを AND で読むので、**商品名に無い語が1つ混ざるだけで0〜1件になる。**
/// 正解の分かる319本で測ると、上位3件に正解が出たのは54%で、外れの多くがこれだった
/// （版番号の残り「1 02」、【マリシア対応】のような括弧の付け足し、#タグ、作者の略号「WH_」、
/// 日本語と英字が続けて書かれて1語のままのもの）。ここで落とす語はその実測から選んでいる
/// （experiments/QueryVariantProbe・ResolveAccuracyProbe）。
/// </summary>
public static partial class FileNameQuery
{
    [GeneratedRegex(@"[_\-. ]*(?:v(?:er)?)?\d+(?:[._]\d+)*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersionRegex { get; }

    [GeneratedRegex(@"[_\-. ]+")]
    private static partial Regex SeparatorRegex { get; }

    /// <summary>
    /// 括弧ごと落とすもの。【マリシア対応】（2）[PB] のような付け足しが入る。
    /// 「」『』は入れない——ファイル名では「LuneBlanc」のように商品名そのものを括っていることが多い
    /// </summary>
    [GeneratedRegex(@"[【\[［(（〈《][^【\[［(（〈《】\]］)）〉》]*[】\]］)）〉》]")]
    private static partial Regex BracketedRegex { get; }

    [GeneratedRegex(@"[【\[［(（〈《「『】\]］)）〉》」』]")]
    private static partial Regex BracketCharRegex { get; }

    [GeneratedRegex(@"#\S+")]
    private static partial Regex HashTagRegex { get; }

    /// <summary>日本語と英数字の境目。「撫mofu」「ギミック4」を2語に分ける。</summary>
    [GeneratedRegex(@"(?<=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}ー])(?=[A-Za-z0-9])|(?<=[A-Za-z0-9])(?=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])")]
    private static partial Regex ScriptBoundaryRegex { get; }

    /// <summary>
    /// 英字の語の尻に続けて書かれた番号。「Cape2」「Stand12b」を語と番号に分ける。
    /// 1語のままだと商品名（「Cape 2」や日本語の名前）に無い語として AND 検索を0件にしていた（2026-09-29）。
    /// 番号は版番号の語として落ち、シリーズの番号としては <see cref="SeriesNumbers"/> が元の名前から拾う。
    /// 割るのは**小文字を含む英字3文字以上＋語の尻の番号（＋小文字1字）**だけ。
    /// 全部大文字の型番（「ABC123」）や、番号の後にも字が続く名前（「shop7xy」）は1語のまま——
    /// 最初は境目で全部割り、ショップ名や型番を崩した（2026-09-29 の実測で4本）。
    /// </summary>
    [GeneratedRegex(@"^(?=[A-Za-z]*[a-z])([A-Za-z]{3,})(\d{1,3}[a-z]?)$")]
    private static partial Regex TrailingNumberWordRegex { get; }

    [GeneratedRegex(@"[\s_\-.　・＿~〜+＋,&]+")]
    private static partial Regex TokenSeparatorRegex { get; }

    /// <summary>版番号・連番・x.x.x・v1・r2。商品名ではない。</summary>
    [GeneratedRegex(@"^(?:v|ver|r)?\d+[a-z]?$|^x+$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionTokenRegex { get; }

    /// <summary>1〜2文字の大文字。作者の略号（WH_ / QW_ / S_）であることが多い。</summary>
    [GeneratedRegex(@"^[A-Z]{1,2}$")]
    private static partial Regex AuthorInitialRegex { get; }

    /// <summary>ブラウザが同名を避けて付ける「 (1)」。</summary>
    [GeneratedRegex(@"\s*[(（]\d{1,2}[)）]\s*$")]
    private static partial Regex DuplicateMarkerRegex { get; }

    /// <summary>語に直接付いた番号（「練習用ポーズ集13」の13）。版番号の各桁（_1.2.0）は拾わない。</summary>
    [GeneratedRegex(@"(?<=[^\d._vV])(\d{1,3})(?=$|[_\s(（【])")]
    private static partial Regex SeriesNumberRegex { get; }

    /// <summary>配布形態や同梱物の種類を表すだけで、商品名の一部ではない語。</summary>
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "append", "addon", "update", "fullset", "fullpack", "full", "set", "pack",
        "unitypackage", "unity", "vrc", "vrchat", "sample", "trial", "readme", "installer", "license",
        "lite", "free", "ver", "version", "fix", "fixed", "prefab", "quest", "pc", "android", "ma",

        // 同梱物の種類。商品名の一部のこともあるが、AND 検索では落とした方が当たった（実測）
        "material", "materials", "psd", "texture",

        // 英語の機能語。検索の役に立たないうえ、別表記で引き直すと英語の辞書で「for → 対して」になり、
        // 「対して」を含む無関係な商品が正解より高い点を取った（やわらか影システム…PCSS For VRC、2026-09-11）
        "for", "the", "of", "and", "with", "to", "in", "on", "by", "a", "an",

        // 同じ商品の中の分け方（全部入り・共通・マテリアルだけ・おまけ）。商品名には出てこない（2026-09-29 の実測）
        "all", "common", "mat", "tex", "おまけ", "先にインポート",
        "無料", "改", "改変用", "修正版", "調整版", "更新", "最新", "配布",
    };

    /// <summary>
    /// 名前の下書きから落とす語。**人が読む名前なので、検索語より控えめにする。**
    /// 「Marycia_texture」の texture は商品名の一部なので残す。
    /// </summary>
    private static readonly HashSet<string> DraftNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "append", "update", "fullset", "fullpack", "full", "pack", "installer", "fix", "fixed",
    };

    /// <summary>
    /// 検索語の元になる語。括弧の付け足し・#タグ・版番号・配布形態の語・作者の略号を落とす。
    /// 英字の分かち書き（SinAvatarPen → Sin Avatar Pen）はまだしない——
    /// 「ShapekeyAddon」を割ってから落とすと、商品名と繋がった Addon まで消えてしまう。
    /// </summary>
    public static IReadOnlyList<string> Tokens(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        // 「○○.unitypackage.zip」のような二重の拡張子
        if (name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".unitypackage".Length];
        }

        // 括弧の中が名前だけで、落とすと何も残らないときは括弧だけ外す
        var stripped = BracketedRegex.Replace(name, " ");
        if (TokenSeparatorRegex.Replace(stripped, string.Empty).Length == 0)
        {
            stripped = BracketCharRegex.Replace(name, " ");
        }

        stripped = BracketCharRegex.Replace(HashTagRegex.Replace(stripped, " "), " ");
        stripped = ScriptBoundaryRegex.Replace(TrimTrailingVersion(stripped.Trim()), " ");

        return TokenSeparatorRegex.Split(stripped)
            .Where(token => token.Length > 0)
            .SelectMany(token => TrailingNumberWordRegex.Match(token) is { Success: true } match
                ? [match.Groups[1].Value, match.Groups[2].Value]
                : new[] { token })
            .Where(token => !NoiseTokens.Contains(token))
            .Where(token => !VersionTokenRegex.IsMatch(token))
            .Where(token => !AuthorInitialRegex.IsMatch(token))
            .ToList();
    }

    /// <summary>検索に使う語を作る。作れない場合は空文字。</summary>
    /// <param name="isAvatarName">
    /// アバターの名前の語か（<see cref="AvatarTokens.IsAvatarName"/>）。渡せば検索語から外す。
    /// 「商品名_アバター名」のファイルで AND 検索が0件になるため。**全部が名前なら外さない**（アバターそのものの zip）。
    /// </param>
    public static string ToSearchQuery(string fileNameOrPath, Func<string, bool>? isAvatarName = null)
    {
        var tokens = WithoutAvatarNames(Tokens(fileNameOrPath), isAvatarName);
        if (tokens.Count > 0)
        {
            return string.Join(' ', tokens.Select(SplitCamelCase)).Trim();
        }

        // 全部落ちた（「update.zip」など）。版番号だけ落とした名前で引く方が、空で引くよりはまし
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        return string.IsNullOrWhiteSpace(name) ? string.Empty : TrimTrailingVersion(name).Trim();
    }

    /// <summary>
    /// **分かち書きにする前**の語。辞書を引くときはこちらも要る。
    ///
    /// <c>HeartBeatGimmick</c> を <c>Heart Beat Gimmick</c> に割ってしまうと、
    /// 辞書には <c>heart</c>（心臓）と <c>beat</c>（拍）としてしか引けない。
    /// 割らずに <c>heartbeat</c> で引くと **心拍・心音** が出る——実測で
    /// 「心拍」がBOOTH内検索の1位、「心音」が2位だった。
    ///
    /// 検索語そのものには使わない（割った方が当たる。実測で
    /// 「SinAvatarPen」のままだと0件だった）。辞書を引くときだけ使う。
    /// </summary>
    public static IReadOnlyList<string> UndividedTokens(string fileNameOrPath)
    {
        var results = new List<string>();

        foreach (var token in Tokens(fileNameOrPath))
        {
            if (token.Length <= 2)
            {
                continue;
            }

            results.Add(token);

            // 隣り合う2語をつないだ形も試す。
            // `HeartBeatGimmick` を丸ごと引いても辞書には無いが、
            // 隣り合う `HeartBeat` なら 心拍・心音 が出る。
            // 3語以上つないだ形は、複合語として辞書に載ることがまず無い
            var parts = SplitCamelCase(token).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 1 < parts.Length; i++)
            {
                results.Add(parts[i] + parts[i + 1]);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// いちばん特徴のある1語。AND 検索が全滅したときに、これだけで引き直す。
    /// 語が2つ以上あれば**最初の語**（英字は4文字以上、日本語は2文字以上なら）。配布ファイルは「商品名_色や版の名前」の順が多く、
    /// 最長の語を選ぶと後ろの付け足し（色・セット・アバター名の別の綴り）が選ばれていた。正解の分かる472本で、
    /// 商品名に無い語が混ざって1ページに正解が無かった外れの9割で、最初の語は商品名にあり、最長の語は無かった（2026-09-29）。
    /// 最初の語が短ければ、日本語は2文字以上、英字は4文字以上で最長のもの（日本語は1文字の情報量が多いので倍に数える）。
    /// **1語だけのファイルは空**（引き直さない）。分かち書きした中の最長の語（HeartBeatGimmick → Gimmick）で引くと、
    /// 候補に正解が来る本数は変わらず、その語だけを名前に持つ別の商品が画面の1位を6本奪った（正解の分かる318本、2026-09-29）。
    /// </summary>
    /// <param name="isAvatarName">
    /// 渡せばアバターの名前を選ばない。英字のアバター名は7字前後と長いことが多く、
    /// 最長の1語を選ぶと商品名の語より先に選ばれて、そのアバターの商品ばかりが出ていた（2026-09-29）。
    /// </param>
    public static string MostDistinctiveToken(string fileNameOrPath, Func<string, bool>? isAvatarName = null)
    {
        var tokens = WithoutAvatarNames(Tokens(fileNameOrPath), isAvatarName);
        if (tokens.Count < 2)
        {
            return string.Empty;
        }

        var first = SplitCamelCase(tokens[0]);
        if (first.All(char.IsAscii) ? first.Length >= 4 : first.Length >= 2)
        {
            return first;
        }

        return LongestToken(tokens);
    }

    private static string LongestToken(IReadOnlyList<string> tokens)
        => tokens
            .SelectMany(token => SplitCamelCase(token).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(token => token.All(char.IsAscii) ? token.Length >= 4 : token.Length >= 2)
            .OrderByDescending(token => token.All(char.IsAscii) ? token.Length : token.Length * 2)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// ファイル名から、商品名の下書きを作る。
    /// 拡張子・末尾のバージョン・ダウンロードの重複番号「 (1)」・配布形態の語を落とすだけ——**分かち書きにはしない。**
    /// 人がそのまま直して使うものなので、検索語のように崩すと直す手間が増える。
    /// </summary>
    public static string ToNameDraft(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // pochio_v1.3.2_update (1) のように、重複番号・配布形態の語・版番号が交互に付くので繰り返す
        var current = DuplicateMarkerRegex.Replace(name, string.Empty).Trim(' ', '_', '-', '.');
        for (var i = 0; i < 4; i++)
        {
            var next = TrimTrailingVersion(current).Trim(' ', '_', '-', '.');
            var cut = next.LastIndexOfAny(['_', '-', ' ', '.']);
            if (cut > 0 && DraftNoiseTokens.Contains(next[(cut + 1)..]))
            {
                next = next[..cut].Trim(' ', '_', '-', '.');
            }

            if (next == current)
            {
                break;
            }

            current = next;
        }

        return current;
    }

    /// <summary><see cref="Tokens"/> からアバターの名前の語を外したもの。商品名を指している語。</summary>
    public static IReadOnlyList<string> ProductTokens(string fileNameOrPath, Func<string, bool>? isAvatarName)
        => WithoutAvatarNames(Tokens(fileNameOrPath), isAvatarName);

    /// <summary>アバターの名前の語を外す。全部が名前なら外さない（アバターそのものの zip は名前で引くしかない）。</summary>
    private static IReadOnlyList<string> WithoutAvatarNames(IReadOnlyList<string> tokens, Func<string, bool>? isAvatarName)
    {
        if (isAvatarName is null)
        {
            return tokens;
        }

        var kept = tokens.Where(token => !isAvatarName(token)).ToList();
        return kept.Count > 0 ? kept : tokens;
    }

    /// <summary>末尾のバージョンを繰り返し落とす（Tori_v1_1_1 → Tori）。</summary>
    private static string TrimTrailingVersion(string name)
    {
        var trimmed = name;
        for (var i = 0; i < 4; i++)
        {
            var next = TrailingVersionRegex.Replace(trimmed, string.Empty);
            if (next == trimmed)
            {
                break;
            }

            trimmed = next;
        }

        return trimmed;
    }

    /// <summary>
    /// 続けて書かれた英単語を分かち書きにする（SinAvatarPen → Sin Avatar Pen）。
    /// 実測で「SinAvatarPen」のまま検索すると0件だったが、
    /// 実際の商品名は「真・アバターペンシステム Sin Avatar Pen System」で語が分かれている。
    /// </summary>
    private static string SplitCamelCase(string token)
    {
        if (token.Length < 4 || !token.All(char.IsAscii))
        {
            return token;
        }

        var builder = new System.Text.StringBuilder(token.Length + 4);
        for (var i = 0; i < token.Length; i++)
        {
            var current = token[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(token[i - 1]) && char.IsLetter(token[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>
    /// バージョンを落とす前のファイル名から、3桁以上の数字を取り出す。
    /// 「FREYSIA.101」と「FREYSIA.112」のように、番号だけが違う商品を区別するために使う
    /// （検索語からは版番号を落とすので、そのままでは同点になってしまう）。
    /// 1桁2桁は「v1.2.0」のような版番号と区別できないので、ここでは拾わない（<see cref="SeriesNumbers"/>）。
    /// </summary>
    public static IReadOnlyList<string> SignificantNumbers(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        return SeparatorRegex.Split(name)
            .Where(token => token.Length >= 3 && token.All(char.IsAsciiDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 語に直接付いた1〜3桁の番号（「練習用ポーズ集13」「ヘイロー012」）。
    /// 連番のシリーズ物は、番号を捨てた検索語だと十数位に沈む（実測）。
    /// 版番号と取り違えないよう、直前が数字・「.」「_」・v でないものに限る。
    /// </summary>
    public static IReadOnlyList<string> SeriesNumbers(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        return SeriesNumberRegex.Matches(name)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 商品名とファイル名がどれくらい近いかの粗い判定。候補の並べ替えに使う。
    ///
    /// ラテン文字の語は語境界で照合する。単純な部分一致にすると、
    /// 「Tori」が「Mistoria」「VECTORIAL」「Victorian」にまで当たってしまうため
    /// （実測で誤った候補が上位に来た）。
    /// 日本語には語の区切りが無いので、非ASCIIを含む語はそのまま部分一致で見る。
    /// </summary>
    public static bool LooksRelated(string itemName, string query)
    {
        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return SeparatorRegex.Split(query)
            .Where(token => token.Length >= 2)
            .Any(token => ContainsWord(itemName, token));
    }

    /// <summary>
    /// 語として含むか。ラテン文字は語境界で、日本語は部分一致で見る。
    /// 「Nemo」が「Nemoria」に当たらないようにするのに使う。
    /// </summary>
    public static bool ContainsWord(string text, string token)
    {
        var isAscii = token.All(char.IsAscii);
        if (!isAscii)
        {
            return text.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeOk = index == 0 || !IsWordCharacter(text[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= text.Length || !IsWordCharacter(text[afterIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = afterIndex;
        }

        return false;
    }

    /// <summary>ラテン文字と数字だけを「語の一部」とみなす（日本語文字は区切りとして扱う）。</summary>
    private static bool IsWordCharacter(char character)
        => char.IsAscii(character) && char.IsLetterOrDigit(character);
}
