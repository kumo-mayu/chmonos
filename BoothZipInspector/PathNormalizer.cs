namespace BoothZipInspector;

/// <summary>
/// ターミナルへのドラッグ＆ドロップ等で入力されたパス文字列を正規化する。
/// 前後の引用符は除去するが、パス内部の空白は維持する。
/// </summary>
public static class PathNormalizer
{
    public static string Normalize(string? input)
    {
        if (input is null)
        {
            return string.Empty;
        }

        var trimmed = input.Trim();

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }
        else if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }
}
