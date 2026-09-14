namespace BoothAssetManager.Core.Services;

/// <summary>
/// パスの小さな比べ方（技術的負債 6：同じ物が3か所にあった）。
///
/// 親のパスを取る関数は1つにしていない。フォルダの木（<see cref="FolderTree"/>）はドライブの直下を根として扱い、
/// フォルダビューは共有（<c>\\nas\share</c>）の直下で止めるので、同じ名前でも答えが違う。
/// タグ・属性の名前の比べ方（表示の文化で大文字小文字を無視）も、パスとは違うのでここに入れない。
/// </summary>
public static class PathText
{
    /// <summary>同じ場所か。Windows のパスなので大文字小文字は無視し、末尾の区切りは見ない。</summary>
    public static bool Same(string left, string right)
        => string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
