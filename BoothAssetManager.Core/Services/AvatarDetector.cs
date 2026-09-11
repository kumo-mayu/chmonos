using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>「対応アバター」節から拾った1件。名前とIDが同じ行に並ぶので両方取れる。</summary>
public sealed record SupportSectionHit
{
    public required string ItemId { get; init; }

    /// <summary>同じ行でIDの手前にあった文字列。別名の材料にする。</summary>
    public string? NameHint { get; init; }
}

/// <summary>説明文から読み取れたもの。</summary>
public sealed record DescriptionScan
{
    /// <summary>「対応アバター」を宣言する見出しの下にあったID。最も強い信号。</summary>
    public IReadOnlyList<SupportSectionHit> Support { get; init; } = [];

    /// <summary>それ以外の見出しの下にあったID。適合率が低いので要確認へ回す。</summary>
    public IReadOnlyList<string> Other { get; init; } = [];

    /// <summary>「対応アバター」の見出しがあったか。</summary>
    public bool HasSupportHeading { get; init; }
}

/// <summary>照合に使う名前の索引。正規化した表記からアバター／素体を引く。</summary>
public sealed class AvatarNameIndex
{
    private readonly Dictionary<string, HashSet<string>> _avatars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _bases = new(StringComparer.Ordinal);

    /// <summary>照合に使うには短すぎる表記。1文字の別名は何にでも当たる。</summary>
    private const int MinAliasLength = 2;

    /// <param name="include">索引に載せる項目。アバターと判定できたものだけを渡す。</param>
    public static AvatarNameIndex Build(
        AvatarRegistry registry,
        Func<AvatarRegistryEntry, bool>? include = null)
    {
        var index = new AvatarNameIndex();

        foreach (var entry in registry.Entries)
        {
            if (include is not null && !include(entry))
            {
                continue;
            }

            foreach (var name in NamesOf(entry))
            {
                index.Add(index._avatars, name, entry.ItemId);
            }
        }

        foreach (var group in registry.BaseGroups)
        {
            index.Add(index._bases, group.Name, group.Name);

            foreach (var alias in group.Aliases.Where(alias => !alias.Rejected))
            {
                index.Add(index._bases, alias.Text, group.Name);
            }
        }

        // 複数のアバターに当たる表記は識別に使えない。
        // 「3Dモデル」のような語が別名として紛れ込んでも、ここで自然に落ちる
        foreach (var ambiguous in index._avatars.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).ToList())
        {
            index._avatars.Remove(ambiguous);
        }

        return index;
    }

    /// <summary>
    /// このアバターを照合するときの呼び名。**毎回、事実から導き直す。**
    ///
    /// 以前は「当たったタグ」を別名として覚えて積み上げていた。タグを部分一致で引いていたので、
    /// 「ミルティナ対応」が「ティナ」の別名に、「Sio」「Expression Menu」が「si」の別名になり、
    /// 次の回からは完全一致で当たって誤りが固定されていた（所持207件の実データで、
    /// 自分の正式名に出てこない別名が110個。作者の15件では1件も出ていなかった）。
    ///
    /// 使うのは次のものだけ：
    /// ・BOOTHの正式名の「」『』の中と「-Latin-」の表記（rurune、Rinasciita）
    /// ・表示名（利用者が直した名前もここにある。汎用語は索引に入る前に落ちる）
    /// ・別名のうち、自分の正式名に現れるもの。手で足した別名は利用者の意思なので現れなくても使う
    /// 正式名が取れていない項目（販売終了など）は確かめようがないので、別名を今までどおり使う。
    /// </summary>
    public static IEnumerable<string> NamesOf(AvatarRegistryEntry entry)
    {
        var booth = (entry.BoothName ?? string.Empty).Normalize(NormalizationForm.FormKC);
        var own = AvatarText.Normalize(entry.BoothName);

        foreach (Match quoted in Regex.Matches(booth, @"[「『｢]([^「」『』｢｣]{1,24})[」』｣]"))
        {
            yield return quoted.Groups[1].Value;
        }

        foreach (Match latin in Regex.Matches(booth, @"-\s?([A-Za-z][A-Za-z .]{1,20}?)\s?-"))
        {
            yield return latin.Groups[1].Value;
        }

        if (entry.DisplayName is { } display)
        {
            yield return display;
        }

        // 人が「この表記は違う」と消したものは照合に使わない。
        // 行は残っている（消したという事実を次の検出まで持ち越すため）
        foreach (var alias in entry.Aliases.Where(alias => !alias.Rejected))
        {
            var manual = string.Equals(alias.Source, nameof(AvatarLinkSource.Manual), StringComparison.Ordinal);
            if (manual || own.Length == 0 || own.Contains(AvatarText.StripForMatch(alias.Text), StringComparison.Ordinal))
            {
                yield return alias.Text;
            }
        }
    }

    private void Add(Dictionary<string, HashSet<string>> into, string? text, string key)
    {
        var normalized = AvatarText.StripForMatch(text);
        if (normalized.Length < MinAliasLength || AvatarText.IsGenericName(text) || AvatarText.IsGenericName(normalized))
        {
            return;
        }

        if (!into.TryGetValue(normalized, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            into[normalized] = set;
        }

        set.Add(key);
    }

    /// <summary>
    /// この語がアバターの呼び名そのものか。**タグはこちらで引く。**
    ///
    /// タグは1語で1つのものを指すので、含んでいるかで引くと別のアバターに当たる
    /// （「ミルティナ」⊃「ティナ」「ルティ」、「Sio」⊃「si」）。
    /// 接尾辞（対応・専用・用…）と敬称（ちゃん・くん…）は落として比べる。
    /// </summary>
    public IReadOnlyCollection<string> FindExact(string? text)
    {
        var key = AvatarText.StripForMatch(text);
        return _avatars.TryGetValue(key, out var ids) ? ids : [];
    }

    /// <summary>
    /// この文字列に名前が含まれているアバターを返す。種類名（「✧しなの対応✧」「[愛莉] Airi」）のように
    /// 装飾の付いた文に使う。
    ///
    /// 次の2つは数えない。どちらも所持207件の実データで誤りの元になっていた。
    /// ・より長いアバター名の一部としてしか現れないもの（「ミルティナ」の中の「ティナ」）
    /// ・英字の名前が単語の途中に埋まっているだけのもの（「Satellite」の中の「tell」）
    /// </summary>
    public IReadOnlyCollection<string> FindAvatars(string? text) => Find(_avatars, text);

    /// <summary>この文字列に名前が含まれている素体グループを返す。</summary>
    public IReadOnlyCollection<string> FindBases(string? text) => Find(_bases, text);

    private static IReadOnlyCollection<string> Find(Dictionary<string, HashSet<string>> from, string? text)
    {
        var normalized = AvatarText.Normalize(text);
        if (normalized.Length == 0 || from.Count == 0)
        {
            return [];
        }

        var lower = (text ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var hits = from.Keys.Where(alias => normalized.Contains(alias, StringComparison.Ordinal)).ToList();
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alias in hits)
        {
            // 自分より長い別の名前の中にしか出てこないなら、その長い方の話
            if (hits.Any(other => other.Length > alias.Length
                && other.Contains(alias, StringComparison.Ordinal)
                && !from[other].SetEquals(from[alias])))
            {
                continue;
            }

            if (IsAsciiWord(alias) && !Regex.IsMatch(lower, $"(^|[^a-z0-9]){Regex.Escape(alias)}([^a-z0-9]|$)"))
            {
                continue;
            }

            foreach (var key in from[alias])
            {
                found.Add(key);
            }
        }

        return found;
    }

    private static bool IsAsciiWord(string text) => text.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    public bool IsEmpty => _avatars.Count == 0 && _bases.Count == 0;
}

/// <summary>
/// 名前の正規化。
///
/// 除外リストではなく許可リストにしてある。実データの商品名とvariation名から
/// 114種類の装飾文字が出ており、裾野が長く増え続けるため、
/// 「日本語・英数字・長音符だけ残す」方が保つ。
///
/// 長音符「ー」は必ず残す。Unicode上は Script=Common で記号に見えるが、
/// セーラー・ルーナリットのようにカタカナ名の内部にあるので落とすと名前が壊れる。
/// </summary>
public static class AvatarText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // NFKC は 𝐸𝑣𝑒𝑟𝐴𝑓𝑡𝑒𝑟 のような装飾英字や全角英数を素の文字に戻す。
        // 実測では装飾除去そのものより効果が大きかった
        var builder = new StringBuilder(text.Length);

        foreach (var ch in SafeNormalize(text).ToLowerInvariant())
        {
            if (ch == 'ー' || char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// NFKC に通す。**壊れた UTF-16（片割れのサロゲート）でも例外にしない。**
    ///
    /// 絵文字は2つの UTF-16 単位でできていて、文字列を途中で切ったり、記号を1つずつ並べた
    /// 正規表現で落としたりすると片方だけが残る。<see cref="string.Normalize()"/> はそれを受けると
    /// 例外を投げるので、検出が1件の商品で丸ごと止まる（評価台で実際に止まった）。
    /// 片割れは文字として意味を持たないので落として続ける。
    /// </summary>
    private static string SafeNormalize(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    builder.Append(ch).Append(text[++i]);
                }
                else if (!char.IsSurrogate(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormKC);
        }
    }

    private static readonly string[] SupportSuffixes =
        ["対応版", "対応", "専用", "向け", "用", "版"];

    private static readonly string[] Honorifics = ["ちゃん", "くん", "君", "さん", "様", "3d"];

    /// <summary>
    /// 照合に使う形。正規化して、接尾辞（対応・専用・用…）と敬称（ちゃん・くん…）を落とす。
    /// 「しなのちゃん対応」「マヌカ3D」「くうた君対応」を、呼び名そのものと同じにする。
    /// 落とした結果が2文字未満になる場合は落とさない（「用」「君」だけの語を空にしない）。
    /// </summary>
    public static string StripForMatch(string? text)
    {
        var normalized = Normalize(text);

        for (var round = 0; round < 3; round++)
        {
            var before = normalized;
            foreach (var suffix in SupportSuffixes.Concat(["対応衣装"]).Concat(Honorifics))
            {
                var normalizedSuffix = Normalize(suffix);
                if (normalized.Length >= normalizedSuffix.Length + 2
                    && normalized.EndsWith(normalizedSuffix, StringComparison.Ordinal))
                {
                    normalized = normalized[..^normalizedSuffix.Length];
                    break;
                }
            }

            if (normalized == before)
            {
                break;
            }
        }

        return normalized;
    }

    /// <summary>「くうた対応」「マヌカ用」から接尾辞を落とす。タグの照合に使う。</summary>
    public static string StripSupportSuffix(string? text)
    {
        var normalized = Normalize(text);

        foreach (var suffix in SupportSuffixes)
        {
            var normalizedSuffix = Normalize(suffix);
            if (normalized.Length > normalizedSuffix.Length
                && normalized.EndsWith(normalizedSuffix, StringComparison.Ordinal))
            {
                return normalized[..^normalizedSuffix.Length];
            }
        }

        return normalized;
    }

    /// <summary>
    /// アバター名にはなり得ない語。
    ///
    /// 別名は「商品名に含まれるタグ」から作るが、
    /// 「【くうた-Kuuta-】オリジナル3Dモデル」のような商品名だと
    /// 「3Dモデル」まで条件を満たしてしまい、全商品に当たる別名ができる（実際に起きた）。
    /// </summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "3d", "3dcg", "3dモデル", "3dモデルデータ", "3dキャラクター", "3d衣装", "3dデータ",
        "オリジナル", "オリジナル3d", "オリジナル3dモデル", "オリジナルモデル", "モデル",
        "アバター", "avatar", "vrchat", "vrc", "vrchat向け", "vrc想定モデル", "vrc対応",
        "quest", "quest対応", "pc版", "blender", "unity", "unitypackage", "fbx", "vrm",
        "衣装", "テクスチャ", "ギミック", "シェイプキー", "blendshape", "無料", "商用利用可",

        // 所持207件の実データで、別名や表示名に紛れて誤りの元になっていた語（2026-09-11）。
        // 「VR」は【VRChatアバター】の中に、「男性」は「男性アバター」の中に現れて、
        // 最短の別名として表示名にまでなっていた
        "vr", "男性", "女性", "男性アバター", "女性アバター", "男性モデル", "人外", "天使", "悪魔", "ドラゴン", "山羊",
        "対応", "標準版", "mobile", "mobile対応", "素体", "素体無料", "3dアバター", "vrcアバター", "vrchatアバター",
        "vrchat想定アバター", "vrc対応3dアバター", "vrchat対応3dモデル", "vrchat想定オリジナル3dモデル",
        "セットアップ", "セットアップコーデ", "cluster", "head",
    };

    /// <summary>別名として使うには一般的すぎるか。</summary>
    public static bool IsGenericName(string? text) => GenericNames.Contains(Normalize(text));

    /// <summary>
    /// 表示に使う短い名前を決める。
    ///
    /// BOOTHの正式名は「【くうた-Kuuta-】オリジナル3Dモデル #Kuuta3D」のように
    /// 飾りと定型句が多く、一覧や絞り込みの表示に使うと読めない。
    /// 商品名の中に現れるタグ（＝別名）のうち最も短いものが、たいてい呼び名そのものになる。
    /// </summary>
    public static string ShortenName(string? boothName, IEnumerable<string>? aliases = null)
    {
        var name = (boothName ?? string.Empty).Trim();

        // 「」『』で括られていれば、それが名前そのものであることが多い。
        // ここを先に見ないと、短いだけのタグを名前にしてしまう
        // （「サメっ子オリジナル3Dモデル「rurune」-ルルネ-」がタグの「サメ」になった）
        var quoted = Regex.Match(name, @"[「『]([^「」『』]{2,24})[」』]");
        if (quoted.Success && !IsBracketNoise(quoted.Groups[1].Value))
        {
            return quoted.Groups[1].Value.Trim();
        }

        var candidate = aliases?
            .Where(alias => !string.IsNullOrWhiteSpace(alias)
                && alias.Length >= 2
                && !IsGenericName(alias)
                && Normalize(name).Contains(Normalize(alias), StringComparison.Ordinal))
            .OrderBy(alias => alias.Length)
            .ThenBy(alias => alias, StringComparer.CurrentCulture)
            .FirstOrDefault();

        if (candidate is not null)
        {
            return candidate;
        }

        // 別名が無ければ、先頭の【…】の中身を採る。名前がそこに入っていることが多い。
        // ただし「【無料】」のような宣伝語のこともあるので、その場合は外して次を見る
        for (var i = 0; i < 4; i++)
        {
            var bracketed = Regex.Match(name, @"^\s*[【「『\[（(]([^】」』\]）)]{1,24})[】」』\]）)]\s*");
            if (!bracketed.Success)
            {
                break;
            }

            var inside = bracketed.Groups[1].Value.Trim();

            if (!IsBracketNoise(inside) && inside.Length >= 2)
            {
                return inside;
            }

            name = name[bracketed.Length..].Trim();
        }

        return name;
    }

    /// <summary>
    /// 商品名の頭の【…】に入りがちな、名前ではない語。
    /// 「【無料】lilToon」を「無料」と呼んでしまわないために要る。
    /// </summary>
    private static readonly HashSet<string> BracketNoiseWords = new(StringComparer.Ordinal)
    {
        "無料", "有料", "期間限定", "限定", "新作", "セール", "値下げ", "再販", "予約",
        "販売中", "更新", "new", "free", "sale", "ma対応", "pb対応", "quest対応",
        "vrchat想定", "vrc想定", "vrchat", "vrc", "vrchat向け", "3d", "3dモデル",
    };

    private static bool IsBracketNoise(string inside)
    {
        var normalized = Normalize(inside);
        return normalized.Length == 0
            || BracketNoiseWords.Contains(normalized)
            || IsGenericName(inside);
    }

    /// <summary>
    /// 一覧のタイルに出す頭文字。飾り記号を飛ばして最初の文字を採る
    /// （そのままだと「【」ばかり並ぶ）。
    /// </summary>
    public static string InitialOf(string? text)
    {
        foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(ch))
            {
                return ch.ToString();
            }
        }

        return "?";
    }

    private static readonly Regex BasePattern = new(
        @"^(?<name>.{1,16}?)(共通素体|素体)(対応版|対応)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// タグから共通素体の名前を取り出す。「珍飯亭共通素体」「えも研素体対応」→「珍飯亭」「えも研」。
    /// 「共通素体」「素体」だけのタグは、どのグループか分からないので拾わない。
    /// </summary>
    public static string? ExtractBaseName(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var match = BasePattern.Match(tag.Trim());
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        return name.Length == 0 || name == "共通" ? null : name;
    }
}

/// <summary>
/// 商品ページの説明文と、タグ・variationから対応アバターの手掛かりを読む。
///
/// ネットワークもファイルも触らない。判定規則をここに閉じ込めてテストできるようにするため。
/// </summary>
public static class AvatarDetector
{
    private static readonly Regex SectionPattern = new(
        "<h2[^>]*>(?<heading>[\\s\\S]*?)</h2>(?<body>[\\s\\S]*?)(?=<h2[^>]*>|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);

    // 「対応アバター」節ではURLが <a> ではなく素のテキストで置かれている（実測）。
    // リンク抽出では取れないので、本文そのものに当てる
    private static readonly Regex ItemUrlPattern = new(
        // URL全体を飲み込む。手前の文字列を別名の材料にするので、scheme とホストも含めて消したい
        @"(?:https?://)?(?:[a-z0-9-]+\.)*booth\.pm/(?:[a-z]{2}/)?items/(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 保存してある <c>items/{id}.h2.html</c> を読む。
    ///
    /// 見出しごとに区切り、「対応アバター」を宣言する節と、読まない節（クレジット等）を分ける。
    /// クレジット節のリンクは宣伝画像に使ったモデルへの謝辞で、実測では34件中0件しかアバターではなかった。
    /// </summary>
    public static DescriptionScan ScanDescription(
        string? html,
        string selfItemId,
        IReadOnlyList<string> supportHeadings,
        IReadOnlyList<string> ignoredHeadings)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new DescriptionScan();
        }

        var support = new List<SupportSectionHit>();
        var seenSupport = new HashSet<string>(StringComparer.Ordinal);
        var other = new HashSet<string>(StringComparer.Ordinal);
        var hasSupportHeading = false;
        var lastEnd = 0;

        foreach (Match section in SectionPattern.Matches(html))
        {
            // 最初の見出しより前は「概要」扱い。対応表明が置かれることがある
            if (section.Index > lastEnd)
            {
                CollectOther(html[lastEnd..section.Index], selfItemId, other);
            }

            lastEnd = section.Index + section.Length;

            var heading = StripTags(section.Groups["heading"].Value);
            var body = section.Groups["body"].Value;

            if (Contains(heading, supportHeadings))
            {
                hasSupportHeading = true;
                CollectSupport(body, selfItemId, support, seenSupport);
                continue;
            }

            if (Contains(heading, ignoredHeadings))
            {
                continue;
            }

            CollectOther(body, selfItemId, other);
        }

        if (lastEnd < html.Length)
        {
            CollectOther(html[lastEnd..], selfItemId, other);
        }

        // 対応節で拾えたものは、弱い方から重複して出さない
        foreach (var hit in support)
        {
            other.Remove(hit.ItemId);
        }

        return new DescriptionScan
        {
            Support = support,
            Other = [.. other],
            HasSupportHeading = hasSupportHeading,
        };
    }

    private static void CollectSupport(
        string body,
        string selfItemId,
        List<SupportSectionHit> into,
        HashSet<string> seen)
    {
        // 行ごとに見る。「アバター名 <URL>」が1行になっているので、名前も一緒に取れる
        var text = StripTags(body.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase));

        foreach (var line in text.Split('\n'))
        {
            foreach (Match match in ItemUrlPattern.Matches(line))
            {
                var id = match.Groups["id"].Value;
                if (id == selfItemId || !seen.Add(id))
                {
                    continue;
                }

                var hint = line[..match.Index].Trim();
                into.Add(new SupportSectionHit
                {
                    ItemId = id,
                    NameHint = hint.Length == 0 ? null : hint,
                });
            }
        }
    }

    private static void CollectOther(string body, string selfItemId, HashSet<string> into)
    {
        foreach (Match match in ItemUrlPattern.Matches(StripTags(body)))
        {
            var id = match.Groups["id"].Value;
            if (id != selfItemId)
            {
                into.Add(id);
            }
        }

        // <a href="/items/123"> の形も拾う（節によってはリンクになっている）
        foreach (Match match in Regex.Matches(body, @"/items/(?<id>\d+)", RegexOptions.IgnoreCase))
        {
            var id = match.Groups["id"].Value;
            if (id != selfItemId)
            {
                into.Add(id);
            }
        }
    }

    private static bool Contains(string heading, IReadOnlyList<string> words)
        => words.Any(word => heading.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static string StripTags(string html)
        => System.Net.WebUtility.HtmlDecode(TagPattern.Replace(html, " "));

    private static readonly Regex AnyItemUrl = new(
        @"booth\.pm/(?:[a-z]{2}/)?items/(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>本文中で見出しの代わりに使われる行。「🌙 【対応アバター】」「◆対応アバターリスト」など。</summary>
    private static readonly Regex MarkerLine = new(
        @"^[^\p{L}\p{N}]*(?:【|\[|［|■|◆|◇|●|○|〇|<|＜|《)?\s*(?:対応アバター|対応モデル|対応一覧|Supported|Compatible)[^\p{L}\p{N}]*$|^[^\p{L}\p{N}]*【[^】]*対応[^】]*】[^\p{L}\p{N}]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>クレジット・サムネ・使用素材の見出し。ここの一覧は対応の宣言ではない。</summary>
    private static readonly Regex CreditHeading = new(
        "クレジット|credit|サムネ|使用|お借り|撮影|着用|協力|素材|thanks|規約|更新",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>「対応」を含んでいても、アバターの一覧ではない見出し。</summary>
    private static readonly Regex NotAvatarSupport = new("非対応|対応シェーダー|対応環境|対応バージョン|対応機種", RegexOptions.Compiled);

    /// <summary>平文でURLと同じ行に書かれたクレジット（「使用アバター：URL」）。</summary>
    private static readonly Regex CreditLine = new("使用アバター|着用アバター|サムネ|撮影|お借り|使用させ|レプリカ|参考に", RegexOptions.Compiled);

    /// <summary>見出しが無くても一覧とみなす行数。クレジットは1〜3体のことが多い（試験データで3〜4行にすると誤りが増えた）。</summary>
    public const int MinListRun = 5;

    private sealed record Section(string Heading, IReadOnlyList<string> Lines, bool IsPlain);

    /// <summary>
    /// h2 を使わずに書かれた対応の一覧を読む。<see cref="ScanDescription"/> の取りこぼしを補う。
    ///
    /// 所持207件の実データ（正解付きの試験データ）で、次の3つの形の取りこぼしがあった：
    /// ・本文中の「🌙 【対応アバター】」の1行の下に「-マヌカ」のような名前だけを並べる
    /// ・h2 の無い商品で、平文の説明文に「・名前 − URL」の対応リストを書く
    /// ・空の見出しや「🍀【JP】」の下に、アバターのURLが長く並ぶ
    /// 読んだ結果、再現率が約96%から約98%に上がり、適合率は99%台のまま変わらなかった。
    ///
    /// 返すのは商品IDだけ。アバターかどうかは後で登録簿（category）で決まるので、
    /// アバターでない商品のURLが混ざっても対応にはならない。
    /// </summary>
    public static IReadOnlyList<string> ScanLists(
        string? html,
        string? description,
        string selfItemId,
        AvatarNameIndex index,
        IReadOnlyList<string> supportHeadings,
        IReadOnlyList<string> ignoredHeadings)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { selfItemId };

        void Take(IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (seen.Add(id))
                {
                    found.Add(id);
                }
            }
        }

        bool IsCredit(string heading) => CreditHeading.IsMatch(heading) || Contains(heading, ignoredHeadings);

        foreach (var section in Sections(html, description))
        {
            // ① 対応の見出し（h2 か見出し代わりの行）の下は、URLと名前だけの行を全部拾う
            if (Contains(section.Heading, supportHeadings) && !NotAvatarSupport.IsMatch(section.Heading) && !IsCredit(section.Heading))
            {
                foreach (var line in section.Lines)
                {
                    // 対応一覧のすぐ下に「使用アバター：URL」のようなクレジットが続く書き方がある
                    if (AnyItemUrl.IsMatch(line) && CreditLine.IsMatch(line))
                    {
                        continue;
                    }

                    Take(IdsOnLine(line, index));
                }

                continue;
            }

            if (IsCredit(section.Heading))
            {
                continue;
            }

            // ② 平文の説明文の対応リスト。見出しが無いので、説明のどこかに「対応」とあるときだけ読む
            if (section.IsPlain && (description ?? string.Empty).Contains("対応", StringComparison.Ordinal))
            {
                Take(PlainSupport(section.Lines));
            }

            // ③ 見出しが無くても、アバターの行が長く続く一覧は対応の一覧とみなす
            Take(LongRuns(section.Lines, index));
        }

        return found;
    }

    private static IEnumerable<Section> Sections(string? html, string? description)
    {
        var raw = new List<Section>();
        if (!string.IsNullOrEmpty(html))
        {
            var heading = string.Empty;
            var last = 0;
            foreach (Match match in Regex.Matches(html, "<h2[^>]*>(?<heading>[\\s\\S]*?)</h2>", RegexOptions.IgnoreCase))
            {
                raw.Add(new Section(heading, LinesOf(html[last..match.Index]), false));
                heading = StripTags(match.Groups["heading"].Value).Trim();
                last = match.Index + match.Length;
            }

            raw.Add(new Section(heading, LinesOf(html[last..]), false));
        }

        raw.Add(new Section(string.Empty, (description ?? string.Empty).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList(), true));

        // 見出し代わりの行でさらに区切る。URLを含む行や長い文は見出しとみなさない
        foreach (var section in raw)
        {
            var heading = section.Heading;
            var lines = new List<string>();
            foreach (var line in section.Lines)
            {
                if (line.Length <= 40 && !AnyItemUrl.IsMatch(line) && MarkerLine.IsMatch(line))
                {
                    yield return new Section(heading, lines, section.IsPlain);
                    heading = line;
                    lines = [];
                    continue;
                }

                lines.Add(line);
            }

            yield return new Section(heading, lines, section.IsPlain);
        }
    }

    private static List<string> LinesOf(string html)
        => StripTags(Regex.Replace(html, @"<br\s*/?>|</(p|div|li)>", "\n", RegexOptions.IgnoreCase))
            .Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

    /// <summary>この行が指すアバター（URLの商品か、名前だけの行の呼び名）。</summary>
    private static IEnumerable<string> IdsOnLine(string line, AvatarNameIndex index)
    {
        var urls = AnyItemUrl.Matches(line).Select(match => match.Groups["id"].Value).ToList();
        if (urls.Count > 0)
        {
            return urls;
        }

        // 名前だけの行：「-マヌカ」「🖤 Airi」「『ルミナ』 LUMINA」。飾りを落として完全一致で見る。
        // 絵文字は2つの UTF-16 単位でできているので、記号を1つずつ並べた文字クラスで落とさない
        var bare = Regex.Replace(line, @"^[^\p{L}\p{N}『「(（+]+", string.Empty).Trim();
        if (bare.Length is 0 or > 40)
        {
            return [];
        }

        return Regex.Split(bare, @"\s+|[/／・、,，]")
            .Select(part => part.Trim('『', '』', '「', '」', '(', ')', '（', '）', '-', '：', ':'))
            .SelectMany(index.FindExact)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> PlainSupport(IReadOnlyList<string> lines)
    {
        var inCredit = false;
        foreach (var line in lines)
        {
            var hasUrl = AnyItemUrl.IsMatch(line);
            if (hasUrl && CreditLine.IsMatch(line))
            {
                continue;
            }

            if (!hasUrl && CreditHeading.IsMatch(line))
            {
                inCredit = true;
                continue;
            }

            if (!hasUrl && (line.Contains("対応", StringComparison.Ordinal) || line.Contains("Supported", StringComparison.OrdinalIgnoreCase)))
            {
                inCredit = false;
                continue;
            }

            if (inCredit)
            {
                continue;
            }

            foreach (Match match in AnyItemUrl.Matches(line))
            {
                yield return match.Groups["id"].Value;
            }
        }
    }

    /// <summary>
    /// アバターの行（URLか名前だけ）が <see cref="MinListRun"/> 行以上続くまとまり。
    /// 「『ルミナ』 LUMINA」の次の行にURLが来る形があるので、2行までの隙間は続きとみなす。
    /// </summary>
    private static IEnumerable<string> LongRuns(IReadOnlyList<string> lines, AvatarNameIndex index)
    {
        var run = new List<string>();
        var gap = 0;

        foreach (var line in lines)
        {
            var ids = IdsOnLine(line, index).ToList();
            if (ids.Count > 0)
            {
                run.AddRange(ids);
                gap = 0;
                continue;
            }

            // クレジットの小見出しが一覧の途中に来たら、そこで切る
            var cut = CreditHeading.IsMatch(line) && line.Length <= 30;
            if (cut || ++gap > 2)
            {
                foreach (var id in Flush(run))
                {
                    yield return id;
                }

                gap = 0;
            }
        }

        foreach (var id in Flush(run))
        {
            yield return id;
        }
    }

    private static List<string> Flush(List<string> run)
    {
        var distinct = run.Distinct(StringComparer.Ordinal).ToList();
        run.Clear();
        return distinct.Count >= MinListRun ? distinct : [];
    }

    /// <summary>
    /// タグから共通素体の宣言を取り出す。「珍飯亭共通素体対応」→「珍飯亭」。
    /// </summary>
    public static IReadOnlyList<string> ScanBaseTags(IEnumerable<string> tags)
        => tags.Select(AvatarText.ExtractBaseName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// タグとvariation名を既知の名前と突き合わせる。
    ///
    /// タグは「くうた対応」のように接尾辞が付くので落としてから、**完全一致で**引く。
    /// variation名は「✧しなの対応✧」のように装飾が前後に付くので、含有で引く。
    /// </summary>
    public static (IReadOnlyCollection<string> FromTags, IReadOnlyCollection<string> FromVariations) ScanNames(
        AvatarNameIndex index,
        IReadOnlyList<string> tags,
        IEnumerable<string> variationNames)
    {
        var fromTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            foreach (var id in index.FindExact(tag))
            {
                fromTags.Add(id);
            }
        }

        var fromVariations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in variationNames)
        {
            foreach (var id in index.FindAvatars(name))
            {
                if (!fromTags.Contains(id))
                {
                    fromVariations.Add(id);
                }
            }
        }

        return (fromTags, fromVariations);
    }
}
