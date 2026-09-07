using System.Text.RegularExpressions;

namespace BoothAssetManager.Core.Resolution;

/// <summary>
/// ファイル名から検索語を作る。
///
/// 配布ファイル名は「商品名＋バージョン＋配布形態」で構成されていることが多いので
/// （<c>Kipfel_1.2.0.zip</c> / <c>Wendy_ver1.01.zip</c> / <c>F_撫で音ギミック5_10_Append.zip</c>）、
/// バージョン部分を落として商品名らしい部分だけを残す。
/// </summary>
public static partial class FileNameQuery
{
    [GeneratedRegex(@"[_\-. ]*(?:v(?:er)?)?\d+(?:[._]\d+)*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersionRegex { get; }

    [GeneratedRegex(@"[_\-. ]+")]
    private static partial Regex SeparatorRegex { get; }

    /// <summary>配布形態を表すだけで、商品名の一部ではない語。</summary>
    private static readonly string[] NoiseTokens =
    [
        "append", "addon", "add-on", "update", "fullset", "fullpack", "full", "set", "pack",
        "unitypackage", "unity", "vrc", "vrchat", "sample", "trial", "readme",
    ];

    /// <summary>検索に使う語を作る。作れない場合は空文字。</summary>
    public static string ToSearchQuery(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // 末尾のバージョンを繰り返し落とす（Tori_v1_1_1 → Tori）
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

        var tokens = SeparatorRegex.Split(trimmed)
            .Where(token => token.Length > 0)
            .Where(token => !NoiseTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Select(SplitCamelCase)
            .ToList();

        return tokens.Count == 0 ? trimmed.Trim() : string.Join(' ', tokens).Trim();
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
    /// 1桁2桁は「v1.2.0」のような版番号と区別できないので対象にしない。
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
            .Any(token => ContainsToken(itemName, token));
    }

    private static bool ContainsToken(string itemName, string token)
    {
        var isAscii = token.All(char.IsAscii);
        if (!isAscii)
        {
            return itemName.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        var index = 0;
        while ((index = itemName.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeOk = index == 0 || !IsWordCharacter(itemName[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= itemName.Length || !IsWordCharacter(itemName[afterIndex]);

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
