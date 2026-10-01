namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの候補1行を「名前（商品ID）」で組み立て、選ばれた行から読み戻す。
///
/// 名前とIDを1行にまとめているのは、**候補の絞り込みが1本の文字列に対する部分一致**だから。
/// 1行に両方入っていれば、名前でもIDでも同じ欄から引ける。
/// VRChatでは対応商品を探すのに商品IDを使う習慣があり、
/// 名前を思い出せなくてもIDなら分かる場面がある。
/// </summary>
public static class AvatarSuggestionText
{
    public static string Format(string name, string itemId) => $"{name}（{itemId}）";

    /// <summary>
    /// 候補の行から商品IDを取り出す。形が違えば null。
    /// 打ち間違いをそのままIDとして扱わないための門番でもある。
    /// </summary>
    public static string? IdOf(string entry)
    {
        var open = entry.LastIndexOf('（');
        var close = entry.LastIndexOf('）');
        if (open < 0 || close <= open + 1)
        {
            return null;
        }

        var id = entry[(open + 1)..close];
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    /// <summary>候補の行から名前だけを取り出す。要約に出すのは名前の方。</summary>
    public static string NameOf(string entry)
    {
        var open = entry.LastIndexOf('（');
        return open <= 0 ? entry : entry[..open];
    }
}
