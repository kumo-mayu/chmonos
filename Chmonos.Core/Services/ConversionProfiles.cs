using System.Text;
using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>unitypackage の中にあった、変換ツールのプロファイル1つ（slug ごと）。</summary>
/// <param name="Slug">ツールが付ける短い名前（<c>config_template2{slug}.json</c> の {slug}）。最初に見た書き方のまま。</param>
/// <param name="Forward">順方向（ほかのアバターの衣装を、このアバターに合わせる）のファイルがある。</param>
/// <param name="Reverse">逆方向（このアバターの衣装を、ほかのアバターに合わせる）のファイルがある。</param>
public sealed record ProfileSlug(string Slug, bool Forward, bool Reverse);

/// <summary>
/// 変換ツールのプロファイルを、unitypackage の中のパスから見分ける（<c>docs/spec/conversion.md</c>「判定」）。
///
/// **パス・ファイル名の決まりはここ1か所に集める**（ツールの更新で名前が変わると、黙って0件になるため。外からの点検 2026-11-06）。
/// 測った（<c>docs/research/mochifitter.md</c>・友人の写し 18 商品）：向きは <c>config_template2{slug}.json</c>（順）と
/// <c>config_{slug}2template.json</c>（逆）で見ると、18 商品とも一覧・商品名の向きと合った。<c>posediff_*</c> は逆方向だけの物にも順の名前の物が入っていて誤る。
/// 置き場は <c>Assets/OutfitRetargetingSystem/Editor/</c> の直下とは限らない（サブフォルダの物もある）ので、その下のどこかで見る。
/// </summary>
public static class ProfileFiles
{
    /// <summary>プロファイルが入る場所の一番上（<see cref="UnityPackageSummary.Roots"/> の形）。これを持たない包みは読まなくてよい。</summary>
    public const string Root = "Assets/OutfitRetargetingSystem";

    private const string EditorFolder = "/OutfitRetargetingSystem/Editor/";

    private static readonly Regex ForwardName = new(@"^config_template2(?<slug>.+)\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReverseName = new(@"^config_(?<slug>.+)2template\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>入る先の一番上に、プロファイルの場所があるか（中のパスを読まずに見分ける前ふるい）。</summary>
    public static bool MayHoldProfiles(IEnumerable<string> roots)
        => roots.Any(root => root.StartsWith(Root, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// パスの並びから、プロファイルを slug ごとに。同じ slug の順と逆は1つにまとめる（大文字小文字は区別しない。ファイルによって揃っていない）。
    /// 並びは最初に見た順。プロファイルが無ければ空。
    /// </summary>
    public static IReadOnlyList<ProfileSlug> Scan(IEnumerable<string> paths)
    {
        var found = new List<ProfileSlug>();
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.IndexOf(EditorFolder, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var name = normalized[(normalized.LastIndexOf('/') + 1)..];
            var forward = ForwardName.Match(name);
            var reverse = ReverseName.Match(name);
            string slug;
            bool isForward;
            if (forward.Success)
            {
                slug = forward.Groups["slug"].Value;
                isForward = true;
            }
            else if (reverse.Success)
            {
                slug = reverse.Groups["slug"].Value;
                isForward = false;
            }
            else
            {
                continue;
            }

            // 「config_template2template.json」のような、ひな形どうしの物は数えない
            if (string.Equals(slug, "template", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = found.FindIndex(profile => string.Equals(profile.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                found.Add(new ProfileSlug(slug, isForward, !isForward));
            }
            else
            {
                found[index] = isForward ? found[index] with { Forward = true } : found[index] with { Reverse = true };
            }
        }

        return found;
    }
}

/// <summary>
/// プロファイルの slug を、アバター・共通素体の名前と照らす（<c>conversion.md</c>「つなぎ先」の (d)(e)）。
/// slug は英字の短い名前（<c>tayuta</c>・<c>salt</c>・<c>mamefriends</c>）で、商品名と直には合わない。
/// 名前の中の英字の語（「ラスク（Rusk）」の rusk）と、slug をかなに読んだ形（tayuta → タユタ）の両方で照らす。
/// 大文字小文字・空白・記号は無視する。
/// </summary>
public static class ConversionNames
{
    /// <summary>slug から作る照らしの鍵（英字と数字だけの小文字・数字を外した形・かなに読んだ形）。</summary>
    public static IReadOnlySet<string> SlugKeys(string slug)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var alnum = AsciiKey(slug);
        if (alnum.Length > 0)
        {
            keys.Add(alnum);
        }

        // 版の数字の付いた slug（beryl4 など）は、数字を外した形でも照らす
        var letters = new string(alnum.Where(char.IsAsciiLetter).ToArray());
        if (letters.Length >= 2)
        {
            keys.Add(letters);
            foreach (var reading in Search.RomajiReading.Readings(letters))
            {
                keys.Add(Search.RomajiReading.ToKatakana(reading));
            }
        }

        return keys;
    }

    /// <summary>名前（アバター名・別名・素体の名前）から作る照らしの鍵。名前全体・中の英字の語・中のかなの並び。</summary>
    public static IReadOnlySet<string> NameKeys(IEnumerable<string?> names)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var whole = AsciiKey(name);
            if (whole.Length >= 2)
            {
                keys.Add(whole);
            }

            foreach (Match word in AsciiWord.Matches(name))
            {
                var key = AsciiKey(word.Value);
                if (key.Length >= 2)
                {
                    keys.Add(key);
                }
            }

            foreach (Match kana in KanaRun.Matches(name))
            {
                var key = ToKatakana(kana.Value).Replace("ー", string.Empty, StringComparison.Ordinal);
                if (key.Length >= 2)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>slug が、名前の鍵のどれかと合うか。</summary>
    public static bool Matches(IReadOnlySet<string> slugKeys, IReadOnlySet<string> nameKeys)
        => slugKeys.Any(key => nameKeys.Contains(key) || nameKeys.Contains(key.Replace("ー", string.Empty, StringComparison.Ordinal)));

    private static readonly Regex AsciiWord = new("[A-Za-z][A-Za-z0-9]*(?:[ _-][A-Za-z0-9]+)*", RegexOptions.CultureInvariant);

    private static readonly Regex KanaRun = new("[ぁ-ゖァ-ヺー]+", RegexOptions.CultureInvariant);

    private static string AsciiKey(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string ToKatakana(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c is >= 'ぁ' and <= 'ゖ' ? (char)(c + ('ァ' - 'ぁ')) : c);
        }

        return builder.ToString();
    }
}

/// <summary>推したつなぎ先1つと、その手掛かり（変換扱いの画面で「商品の対応素体から」と見せる）。</summary>
public sealed record ConversionSuggestion(ProfileSlug Profile, ConversionTarget? Target, IReadOnlyList<ConversionClue> Clues, bool Conflicting);

/// <summary>つなぎ先の手掛かりの種類（<c>conversion.md</c>「つなぎ先」の (a)〜(e)）。</summary>
public enum ConversionClueKind
{
    /// <summary>(a) 商品のカテゴリが「3Dキャラクター」：その商品自身（アバター本体に同梱のプロファイル）。</summary>
    ItemIsAvatar,

    /// <summary>
    /// (b) プロファイルの <c>avatar_data</c> の FBX の場所が、持っているアバターの unitypackage の中身にある：そのアバター（2026-11-09）。
    /// 素体用のプロファイルでも代表のアバターの FBX を指すので、素体の手掛かりと合わされば素体用として推す（Agree）。
    /// </summary>
    FbxAvatar,

    /// <summary>(c) 商品から検出された対応の共通素体。</summary>
    DetectedBase,

    /// <summary>(d) 商品から検出された対応アバター（1体だけ、または slug と名前が合った1体）。</summary>
    DetectedAvatar,

    /// <summary>(e) slug が共通素体の名前・別名と合った。</summary>
    SlugIsBaseName,
}

/// <summary>手掛かり1つ。アバター（商品 ID）か共通素体（名前）を指す。</summary>
public sealed record ConversionClue(ConversionClueKind Kind, string? AvatarItemId, string? BaseName);

/// <summary>
/// プロファイルのつなぎ先を推す（<c>conversion.md</c>「判定」の 3）。**手掛かりを全部集め、食い違わなければ推し、食い違えば推さない**
/// （ユーザ指摘 2026-11-06：「当たったところで完全に止めるのは危険」。アバター本体の商品が、ほかのアバターや素体のプロファイルを同梱することもある）。
/// 推した値は確認待ち（<see cref="ConversionTarget.Confirmed"/> は偽）。入れるのは人が決めたとき。
/// </summary>
public static class ConversionInference
{
    private const string AvatarCategory = "3Dキャラクター";

    /// <summary>商品の中のプロファイル（slug ごと）について、つなぎ先を推す。</summary>
    /// <param name="fbxAvatars">slug → FBX の場所が中身にあるアバターの商品 ID（手掛かり (b)。<see cref="ConversionScan.FbxAvatarsOf"/>）。</param>
    public static IReadOnlyList<ConversionSuggestion> Suggest(
        ItemRecord item,
        IReadOnlyList<ProfileSlug> profiles,
        AvatarRegistry registry,
        AvatarCompatibilityIndex compatibility,
        IReadOnlyDictionary<string, string>? fbxAvatars = null)
    {
        var entries = registry.Entries.ToDictionary(entry => entry.ItemId, StringComparer.Ordinal);
        var groups = registry.BaseGroups.Where(group => !group.Rejected && !string.IsNullOrWhiteSpace(group.Name)).ToList();

        var detectedAvatars = item.Local.Avatars
            .Where(link => !link.Rejected && link.Source != AvatarLinkSource.H2Link && !string.Equals(link.AvatarItemId, item.Id, StringComparison.Ordinal))
            .Select(link => link.AvatarItemId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var detectedBases = item.Local.AvatarBases
            .Where(link => !link.Rejected && !string.IsNullOrWhiteSpace(link.BaseName))
            .Select(link => link.BaseName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var isAvatar = string.Equals(item.Booth.Category?.Name, AvatarCategory, StringComparison.Ordinal);

        var result = new List<ConversionSuggestion>();
        foreach (var profile in profiles)
        {
            var slugKeys = ConversionNames.SlugKeys(profile.Slug);
            var clues = new List<ConversionClue>();

            // (b) FBX の場所が持っているアバターの中身と同じ（slug → アバターの商品 ID は呼び手が控えから引いて渡す）
            if (fbxAvatars is not null && fbxAvatars.TryGetValue(profile.Slug, out var fbxAvatar))
            {
                clues.Add(new ConversionClue(ConversionClueKind.FbxAvatar, fbxAvatar, null));
            }

            if (isAvatar)
            {
                clues.Add(new ConversionClue(ConversionClueKind.ItemIsAvatar, item.Id, null));
            }

            foreach (var baseName in detectedBases)
            {
                clues.Add(new ConversionClue(ConversionClueKind.DetectedBase, null, baseName));
            }

            // (d) 対応アバター：1体ならそのアバター。2体以上なら slug と名前が合う物だけ（合わなければ手掛かりにしない）
            if (detectedAvatars.Count == 1)
            {
                clues.Add(new ConversionClue(ConversionClueKind.DetectedAvatar, detectedAvatars[0], null));
            }
            else
            {
                foreach (var avatar in detectedAvatars)
                {
                    if (entries.TryGetValue(avatar, out var entry) && ConversionNames.Matches(slugKeys, ConversionNames.NameKeys(NamesOf(entry))))
                    {
                        clues.Add(new ConversionClue(ConversionClueKind.DetectedAvatar, avatar, null));
                    }
                }
            }

            foreach (var group in groups)
            {
                if (ConversionNames.Matches(slugKeys, ConversionNames.NameKeys([group.Name, .. group.Aliases.Select(alias => alias.Text)])))
                {
                    clues.Add(new ConversionClue(ConversionClueKind.SlugIsBaseName, null, group.Name));
                }
            }

            var target = Agree(clues, compatibility, out var conflicting);
            result.Add(new ConversionSuggestion(
                profile,
                target is null ? null : target with { Forward = profile.Forward, Reverse = profile.Reverse, Slug = profile.Slug, Confirmed = false },
                clues,
                conflicting));
        }

        return result;
    }

    private static IEnumerable<string?> NamesOf(AvatarRegistryEntry entry)
        => [entry.DisplayName, entry.BoothName, .. entry.Aliases.Select(alias => alias.Text)];

    /// <summary>
    /// 手掛かりが1つのつなぎ先にまとまるか。素体を指す手掛かりがあり、アバターを指す手掛かりがどれもその素体の仲間なら、素体用としてまとめる
    /// （<c>conversion.md</c>「素体用か」：まめふれんずのように、代表のアバターと素体の両方が当たる）。まとまらなければ null と、食い違いの印。
    /// </summary>
    private static ConversionTarget? Agree(IReadOnlyList<ConversionClue> clues, AvatarCompatibilityIndex compatibility, out bool conflicting)
    {
        conflicting = false;
        if (clues.Count == 0)
        {
            return null;
        }

        var bases = clues.Where(clue => clue.BaseName is not null).Select(clue => clue.BaseName!).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        var avatars = clues.Where(clue => clue.AvatarItemId is not null).Select(clue => clue.AvatarItemId!).Distinct(StringComparer.Ordinal).ToList();

        if (bases.Count > 1 || (bases.Count == 0 && avatars.Count > 1))
        {
            conflicting = true;
            return null;
        }

        if (bases.Count == 1)
        {
            var baseName = bases[0];
            if (avatars.All(avatar => string.Equals(compatibility.BaseNameOf(avatar), baseName, StringComparison.CurrentCultureIgnoreCase)))
            {
                return new ConversionTarget { BaseName = baseName };
            }

            conflicting = true;
            return null;
        }

        return new ConversionTarget { AvatarItemId = avatars[0] };
    }
}
