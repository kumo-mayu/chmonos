
using System.Text;
using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 取り直して見つけた変化が、使っている人に促す「中身の更新」か（docs/plan.md の 7・ユーザ判断 2026-11-06）。
///
/// 手掛かりは強い通知と同じ「更新履歴の見出しの変化」（<see cref="BoothChanges.HasStrongChange"/>）。
/// そのうち、**足した行が全部「対応アバターの追加」なら促さない**：別のアバター用の Prefab や変換プロファイルの公開は、
/// 今使っている人のファイルを変えない。友人の写し2つで強い通知 29 件のうち、追加だけは 3 件だった [実測]。
///
/// 見分けは**迷えば促す側**に倒す（見落とすより、1回余計に促す方が害が小さい）。
/// 行が1つでも「追加」と言い切れなければ促す。足した行が上限で切れている・行の差を持たない古い形の知らせも促す。
/// アバターの新しい版への対応（「〇〇 Ver1.3.0 に対応」）は更新に数える（ユーザ判断 2026-11-06：アバターを上げた人には要る）。
/// </summary>
/// <summary>特定のアバター・共通素体向けの更新の、向き先（<see cref="UsedUpdateKind.TargetsOf"/>）。</summary>
public sealed record UpdateTargets(IReadOnlyList<string> Avatars, IReadOnlyList<string> Bases);

public static class UsedUpdateKind
{
    /// <summary>
    /// 促す更新か。更新履歴の見出しが動いていて、足した行が対応アバターの追加だけではないとき。
    /// </summary>
    /// <param name="avatarNames">
    /// その商品の今の対応アバターの名前（表示名・BOOTH の名前・別名）。⑦で検出し直した後の物を渡す。
    /// 「・〇〇」のように名前だけを並べた行を、追加と見分けるのに使う。
    /// </param>
    public static bool ShouldPrompt(IReadOnlyList<NotificationDiff> diffs, IEnumerable<string> avatarNames)
        => BoothChanges.HasStrongChange(diffs) && !IsAvatarAdditionOnly(diffs, avatarNames);

    /// <summary>
    /// 更新履歴の見出しで足した行が、全部「対応アバターの追加」（と日付・版の数字だけの行）か。追加の行が1つも無ければ偽。
    /// </summary>
    public static bool IsAvatarAdditionOnly(IReadOnlyList<NotificationDiff> diffs, IEnumerable<string> avatarNames)
    {
        var names = avatarNames
            .Select(Fold)
            .Where(name => name.Length >= MinNameLength)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var additions = 0;
        foreach (var diff in diffs.Where(diff => Booth.H2SectionExtractor.IsUpdateHistoryHeading(diff.Field)))
        {
            // 行の差が無い（古い形）・足した行が上限で切れた物は、見えない行があるので言い切れない
            if (diff.Lines is null || diff.MoreAdded is > 0)
            {
                return false;
            }

            foreach (var line in diff.Lines.Where(line => line.Kind == NotificationLineKind.Added))
            {
                switch (Classify(line.Text, names))
                {
                    case LineKind.Neutral:
                        continue;
                    case LineKind.AvatarAddition:
                        additions++;
                        continue;
                    default:
                        return false;
                }
            }
        }

        return additions > 0;
    }

    /// <summary>
    /// 更新がどのアバター向けか（ユーザ判断 2026-11-09：「〇〇くんの瞳の縁を再修正」は、そのアバターで使っている人にだけ促す）。
    /// 更新の行（追加でも日付だけでもない行）が**全部**どれかの対応アバター・共通素体の名前を含むときだけ、その名前の物を返す。
    /// 名前の無い更新の行が1行でもあれば、全部向けとして null（迷えば促す側）。足した行が切れている・行の差が無いときも null。
    /// </summary>
    /// <param name="avatarNames">対応アバターの商品ID → 名前（表示名・BOOTH の名前・別名）。</param>
    /// <param name="baseNames">対応している共通素体の名前 → 名前（素体の名前・別名）。</param>
    public static UpdateTargets? TargetsOf(
        IReadOnlyList<NotificationDiff> diffs,
        IReadOnlyDictionary<string, IReadOnlyList<string>> avatarNames,
        IReadOnlyDictionary<string, IReadOnlyList<string>> baseNames)
    {
        static Dictionary<string, List<string>> Folded(IReadOnlyDictionary<string, IReadOnlyList<string>> names)
            => names.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(Fold).Where(name => name.Length >= MinNameLength).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var avatars = Folded(avatarNames);
        var bases = Folded(baseNames);
        var all = avatars.Values.Concat(bases.Values).SelectMany(names => names).Distinct(StringComparer.Ordinal).ToList();

        var forAvatars = new HashSet<string>(StringComparer.Ordinal);
        var forBases = new HashSet<string>(AvatarBaseNames.Comparer);
        foreach (var diff in diffs.Where(diff => Booth.H2SectionExtractor.IsUpdateHistoryHeading(diff.Field)))
        {
            if (diff.Lines is null || diff.MoreAdded is > 0)
            {
                return null;
            }

            foreach (var line in diff.Lines.Where(line => line.Kind == NotificationLineKind.Added))
            {
                if (Classify(line.Text, all) != LineKind.Other)
                {
                    continue;
                }

                var folded = Fold(line.Text);
                var hitAvatars = avatars.Where(pair => pair.Value.Any(name => folded.Contains(name, StringComparison.Ordinal))).Select(pair => pair.Key).ToList();
                var hitBases = bases.Where(pair => pair.Value.Any(name => folded.Contains(name, StringComparison.Ordinal))).Select(pair => pair.Key).ToList();
                if (hitAvatars.Count == 0 && hitBases.Count == 0)
                {
                    return null;
                }

                forAvatars.UnionWith(hitAvatars);
                forBases.UnionWith(hitBases);
            }
        }

        return forAvatars.Count == 0 && forBases.Count == 0
            ? null
            : new UpdateTargets([.. forAvatars.Order(StringComparer.Ordinal)], [.. forBases.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// 名前として照らす長さの下限。1字の別名（「A」など）は普通の語に紛れて当たるので使わない。
    /// </summary>
    private const int MinNameLength = 2;

    internal enum LineKind
    {
        /// <summary>空・日付だけ・版の数字だけ。どちらとも言わない（更新の行に添えて必ず書かれる）。</summary>
        Neutral,

        /// <summary>対応アバターの追加。</summary>
        AvatarAddition,

        /// <summary>それ以外。1行でもあれば促す。</summary>
        Other,
    }

    internal static LineKind Classify(string text, IReadOnlyList<string> foldedNames)
    {
        var line = Fold(text);
        line = LeadingNoise.Replace(line, string.Empty).Trim();
        if (line.Length == 0)
        {
            return LineKind.Neutral;
        }

        // 修正・機能の追加・道具への対応は、追加の語が並んでいても更新
        if (UpdateWords.IsMatch(line) || ToolNames.IsMatch(line))
        {
            return LineKind.Other;
        }

        // 版の後に「対応」が来れば、アバターの新しい版への対応（「〇〇 Ver1.3.0 に対応」）。
        // それ以外の版は、足した物そのものの版（「〇〇用プロファイル Ver.1.0.0 を公開」）なので外して読む
        if (AvatarVersionSupport.IsMatch(line))
        {
            return LineKind.Other;
        }

        line = VersionToken.Replace(line, " ");
        if (AdditionPattern.IsMatch(line) || IsNameList(line, foldedNames))
        {
            return LineKind.AvatarAddition;
        }

        return LineKind.Other;
    }

    /// <summary>
    /// アバターの名前を並べただけの行（「・〇〇、△△」「〇〇 / △△」）。名前を除いて、つなぎの記号と語しか残らないときだけ。
    /// 名前が入っているだけで追加と見なすと、「〇〇用の新しいテクスチャ」のような更新を見落とす。
    /// </summary>
    private static bool IsNameList(string line, IReadOnlyList<string> foldedNames)
    {
        var rest = line;
        var found = false;

        // 長い名前から外す（「〇〇」と「〇〇改」があれば「〇〇改」を先に）
        foreach (var name in foldedNames.OrderByDescending(name => name.Length))
        {
            if (rest.Contains(name, StringComparison.Ordinal))
            {
                rest = rest.Replace(name, " ", StringComparison.Ordinal);
                found = true;
            }
        }

        return found && NameListGlue.Replace(rest, string.Empty).Length == 0;
    }

    /// <summary>名前の並びの間に来るつなぎ（記号・「と」「and」・敬称の「ちゃん」「くん」）。</summary>
    private static readonly Regex NameListGlue = new(
        @"[\s、,，/／&＆+＋・()（）「」『』""'“”]|と|ちゃん|くん|さん|様|and",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 照らす前の形：全角・半角をそろえ（NFKC）、「é」などの付いた字を外し、小文字にする
    /// （登録簿の名前と更新履歴の書き方が「Céleste」「celeste」のように違うことがある）。
    /// </summary>
    internal static string Fold(string text)
    {
        var decomposed = Nfkc.Fold(text).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            // 外すのはラテン文字の付け記号（U+0300〜036F）だけ。濁点・半濁点（U+3099・309A）も同じ分類なので、
            // 分類で外すと「プロファイル」が「フロファイル」になって型に当たらない
            if (c is < '̀' or > 'ͯ')
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// 行の頭の記号・日付・この商品の版（「・2026/11/01 ver1.2 」「- v1.0.1:」「✧ 」「9/16 」）。
    /// 記号は約物と記号の分類で全部（販売者ごとに飾りが違う。友人の写しで「✧」「↪」「［］」を見た）。
    /// </summary>
    private static readonly Regex LeadingNoise = new(
        @"^(?:[\s\p{P}\p{S}]+|\d{1,4}[/\.\-年]\d{1,2}(?:[/\.\-月]\d{1,2}日?)?|(?:ver(?:sion)?\.?|v)\s*\d+(?:\.\d+)*\w*|更新(?:履歴)?)+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>版の数字（「ver1.3.0」「v1.2」「1.3版」「バージョン2」）。</summary>
    private static readonly Regex VersionToken = new(
        @"(?:ver(?:sion)?\.?\s*|\bv)\d+(?:\.\d+)*|\d+(?:\.\d+)+\s*版|バージョン\s*\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// アバターの新しい版への対応（「〇〇 Ver1.3.0 に対応」「〇〇 Ver 1.2.0 のバージョン検出に対応」「support for 〇〇 v2」）。
    /// 版の後に対応の語が来る形。ユーザ判断 2026-11-06：アバターを上げた人には要る更新なので促す。
    /// </summary>
    private static readonly Regex AvatarVersionSupport = new(
        @"(?:(?:ver(?:sion)?\.?\s*|\bv)\d+(?:\.\d+)*|\d+(?:\.\d+)+\s*版|バージョン\s*\d+).{0,20}?(?:対応|대응)|support(?:s|ed)?\s+(?:for\s+)?\S+\s+(?:ver(?:sion)?\.?\s*|v)\d",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>修正・作り直し・機能の語（日・英・韓）。並んでいれば追加だけではない。</summary>
    private static readonly Regex UpdateWords = new(
        @"修正|不具合|バグ|改善|改良|変更|調整|更新|軽量|最適化|作り直|差し替|機能|削除|fix|bug|improv|chang|updat|adjust|remov|tweak|수정|개선|변경|버그",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 「〜に対応」と書くがアバターでない物（道具・シェーダー・SDK）。これへの対応は更新に数える。
    /// </summary>
    private static readonly Regex ToolNames = new(
        @"modular\s*avatar|\bma\b|vrcfury|liltoon|poiyomi|unity|sdk|vcc|alcom|physbone|シェーダ|shader",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 対応アバターの追加の書き方（日・英・韓）。登録簿にまだ無いアバターでも型で拾う
    /// （2026-11-06 の照らし合わせで、プロファイルの公開1件は名前では拾えなかった）。
    /// </summary>
    private static readonly Regex AdditionPattern = new(
        // 「用」と「追加」の間は、足した物の名前（Prefab・プロファイル）と飾りだけを許す。何でも許すと「〇〇用の新しいテクスチャを追加」も拾う
        @"対応アバター(?:を|の)?追加|(?:用|向け)[^。]{0,12}?(?:prefab|プレハブ|プロファイル|データ|素体|衣装|fbx)[^。]{0,6}?(?:追加|同梱|公開)|(?:用|向け)の?を?(?:追加|同梱|公開)"
        + @"|に対応|対応(?:しました|完了|追加|開始|リリース|版)|\S対応$"
        + @"|support(?:s|ed)?\s+(?:for\b|\w)|added\s+(?:prefab|support|\w+\s+prefab)|\bfor\s+\S+\s+(?:added|prefab)"
        // 「추가」（追加）だけでは色や機能の追加と見分けられないので、型には入れない（名前があれば名前で拾う）
        + @"|대응",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
