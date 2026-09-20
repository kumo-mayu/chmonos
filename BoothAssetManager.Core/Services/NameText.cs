namespace BoothAssetManager.Core.Services;

/// <summary>
/// 人が付ける名前（大分類・小分類・属性・共通素体・アバターの表示名・改変の名前・呼び方）の決まり
/// （ユーザ判断 2026-09-20・I13：「一旦そのくらいにしてみましょう」）。
///
/// **長さも文字の種類も、どこにも制限が無かった。**改行も何百文字も入り、
/// 一覧の行と札にそのまま出るので行が壊れる。JSON にも入るので、極端に長いと読み書きが重くなる。
///
/// メモは別扱い（長文を書く場所なので <see cref="MaxMemoLength"/>）。
/// </summary>
public static class NameText
{
    /// <summary>名前の上限。一覧の行・札・候補に出しても読める長さ。</summary>
    public const int MaxNameLength = 60;

    /// <summary>メモの上限。長文は許すが、際限なくは持たない。</summary>
    public const int MaxMemoLength = 10000;

    /// <summary>
    /// 1行の名前として均す。**改行とタブは空白に寄せる**（貼り付けで混ざるだけで、名前としては意味がない）。
    /// 前後の空白は落とし、続いた空白は1つにする。長さは切らない——切ると黙って別の名前になる。
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var folded = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var letter in text)
        {
            var isSpace = letter is '\r' or '\n' or '\t' || char.IsWhiteSpace(letter);
            if (isSpace)
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && folded.Length > 0)
            {
                folded.Append(' ');
            }

            lastWasSpace = false;
            folded.Append(letter);
        }

        return folded.ToString();
    }

    /// <summary>長すぎるか。**切らずに断る**（切ると、打った名前と保存された名前が食い違う）。</summary>
    public static bool IsTooLong(string? text) => Normalize(text).Length > MaxNameLength;

    /// <summary>断るときの言い方。どこでも同じ文にする。</summary>
    public static string TooLongMessage(string what)
        => $"{what}は {MaxNameLength} 文字までです。短くしてから押してください。";
}
