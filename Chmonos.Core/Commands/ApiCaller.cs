namespace Chmonos.Core.Commands;

/// <summary>
/// 外から使う API の今の要求が、どのソフトから来たか（ログの1行に添える名前。plan 31「書き込みのログに、どのソフトが書いたかを残す」）。
/// 要求の間だけ持つ値（<see cref="AsyncLocal{T}"/>）にした：命令の口（<c>ApiWriteHooks.Execute</c>）の形を変えずに済み、
/// 要求の中から裏へ渡した作業（Unity へ送った後の足跡など）にも引き継がれる。中身（商品 ID など）ではないのでログに書いてよい
/// </summary>
public static class ApiCaller
{
    private static readonly AsyncLocal<string?> s_label = new();

    /// <summary>今の要求のソフトの名前（API の外・名前が無ければ null）。</summary>
    public static string? Label
    {
        get => s_label.Value;
        set => s_label.Value = value;
    }

    /// <summary>ログの1行の末尾に付ける「（ソフト名）」。名前が無ければ空。</summary>
    public static string LogSuffix => Label is { Length: > 0 } label ? $"（{label}）" : string.Empty;
}
