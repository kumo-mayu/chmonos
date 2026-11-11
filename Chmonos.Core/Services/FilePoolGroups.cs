using System.Text.RegularExpressions;

namespace Chmonos.Core.Services;

/// <summary>重複回数（選んだ商品の中で持つ商品の数）で絞るときの向き（plan 30「以下・以上・のみ」）。</summary>
public enum HolderCountFilter
{
    /// <summary>n 以下（既定。ユーザ判断 2026-11-11「基本は n 以下を表示」）。</summary>
    AtMost,

    /// <summary>n 以上。</summary>
    AtLeast,

    /// <summary>n のみ。</summary>
    Exactly,
}

/// <summary>名前の頭が同じファイルの組（ファイルの割り振りのサブ画面で畳む単位）。</summary>
/// <param name="Name">組の名前（最初のファイルの頭。大文字小文字は最初の物のまま）。</param>
/// <param name="Files">組のファイル（溜まりの並びのまま）。</param>
/// <param name="PackageNames">中の unitypackage の名前（拡張子なし・重なりは1つ・名前の順）。見出しに添える。</param>
public sealed record PooledGroup(string Name, IReadOnlyList<PooledFile> Files, IReadOnlyList<string> PackageNames);

/// <summary>
/// ファイルの割り振り（plan 30）の組分けと絞り。**読むだけ**。
///
/// 組の名前は、ファイル名の頭から区切りの最初の1つまで（ユーザ指示 2026-11-11「頭文字単位で折りたたみ」）。
/// 区切りは <c>_</c>・<c>-</c>・半角と全角の空白・<c>.</c>・括弧（始まりと終わり）と、版の印（v1.0・Ver1.2・ver.）の前。
/// 英字だけが続く名前は大文字小文字の切れ目で切らない（plan 30。測った結果、切ると別の作者の物どうしが1つの組に寄った）。
///
/// 測った結果（2026-11-11・手元の確かめ用のダウンロード 33 本と中の unitypackage 43 本。名前は記録しない）：
/// どの区切りの候補を入れても組は同じで、効いていたのは <c>_</c> だけだった（下見の名前がほぼ全部「作者や素体の名前_種類_版」の形）。
/// ほかの区切りは作り物の名前（試験 <c>FilePoolGroupsTests</c>）で決めた。外れても組が細かくなるだけで、付け外しには響かない。
/// </summary>
public static partial class FilePoolGroups
{
    /// <summary>区切りの文字。頭に並ぶ物は飛ばす（「【衣装】…」「_名前」で組の名前が空にならないように）。</summary>
    private const string Separators = "_- 　.()（）[]【】「」＿";

    /// <summary>版の印の前。前が英字なら切らない（「Silver2」「Saver」を切らない）。</summary>
    [GeneratedRegex(@"(?<![A-Za-z])(?:v|ver|version)\.?\s?\d|(?<![A-Za-z])ver\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionMark();

    /// <summary>ファイル名の組の名前。名前が区切りだけなら拡張子を除いた名前のまま。</summary>
    public static string GroupName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var start = 0;
        while (start < stem.Length && Separators.Contains(stem[start]))
        {
            start++;
        }

        if (start == stem.Length)
        {
            return stem;
        }

        var end = stem.IndexOfAny(Separators.ToCharArray(), start);
        if (end < 0)
        {
            end = stem.Length;
        }

        var version = VersionMark().Match(stem, start);
        if (version.Success && version.Index > start && version.Index < end)
        {
            end = version.Index;
        }

        return stem[start..end];
    }

    /// <summary>重複回数が絞りに当たるか。</summary>
    public static bool Matches(int holderCount, int threshold, HolderCountFilter filter) => filter switch
    {
        HolderCountFilter.AtLeast => holderCount >= threshold,
        HolderCountFilter.Exactly => holderCount == threshold,
        _ => holderCount <= threshold,
    };

    /// <summary>このファイルの中の unitypackage の名前（それ自体が unitypackage なら自分の名前）。拡張子なし。</summary>
    public static IReadOnlyList<string> PackageNames(PooledFile file)
    {
        if (file.FileName is { } name && name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
        {
            return [Path.GetFileNameWithoutExtension(name)];
        }

        return file.Contents
            .Where(entry => entry.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
            .Select(entry => Path.GetFileNameWithoutExtension(entry.Replace('\\', '/').Split('/')[^1]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 組に分ける。組の名前は大文字小文字を区別せずに比べる（同じ作者でも「SampleShop_A」「sampleshop_B」と揺れる）。
    /// 組は名前の順、名前の無いファイル（場所の記録が無い）は名前の無い1つの組として最後。
    /// </summary>
    public static IReadOnlyList<PooledGroup> Of(IEnumerable<PooledFile> files)
    {
        var groups = new Dictionary<string, (string Name, List<PooledFile> Files)>(StringComparer.OrdinalIgnoreCase);
        var unnamed = new List<PooledFile>();
        foreach (var file in files)
        {
            if (file.FileName is not { } fileName)
            {
                unnamed.Add(file);
                continue;
            }

            var name = GroupName(fileName);
            if (!groups.TryGetValue(name, out var group))
            {
                group = (name, []);
                groups[name] = group;
            }

            group.Files.Add(file);
        }

        var result = groups.Values
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new PooledGroup(group.Name, group.Files, PackagesOf(group.Files)))
            .ToList();
        if (unnamed.Count > 0)
        {
            result.Add(new PooledGroup(string.Empty, unnamed, PackagesOf(unnamed)));
        }

        return result;
    }

    private static IReadOnlyList<string> PackagesOf(IEnumerable<PooledFile> files)
        => files.SelectMany(PackageNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
