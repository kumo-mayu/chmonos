namespace Chmonos.Core.Services;

/// <summary>
/// Unity プロジェクトの場所の正規化。改変に結ぶとき（API の <c>PUT /v1/modifications/{id}/unity-project</c>）と、
/// 検索の条件「Unity プロジェクト」で照らすときの両方がここを通る（2026-10-14・Unity の API-GAPS 3-11）。
/// Unity の <c>Application.dataPath</c> は <c>/</c> 区切りで返し、末尾に区切りを付けて書く人もいる。
/// 綴りだけ違う同じ場所を、黙って別の場所として扱わないため
/// </summary>
public static class UnityProjectPath
{
    /// <summary>
    /// 前後の空白を落とし、区切りを <c>\</c> に揃え（完全なパスなら <c>..</c> や重なった区切りも畳む）、末尾の区切りを落とす。空なら null。
    /// 大文字小文字はそのまま（照らす側が区別しない）
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(normalized))
        {
            try
            {
                // 完全なパスなら今のフォルダに左右されない（文字の上だけで畳む）
                normalized = Path.GetFullPath(normalized);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // 畳めない綴りは区切りを揃えただけで照らす（当たらないだけで、誤りにはしない）
            }
        }

        return Path.TrimEndingDirectorySeparator(normalized);
    }
}
