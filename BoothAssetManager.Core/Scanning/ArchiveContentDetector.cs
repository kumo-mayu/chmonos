namespace BoothAssetManager.Core.Scanning;

/// <summary>展開物の中身かどうかの判定結果。</summary>
public sealed record ArchiveContentJudgement
{
    public static readonly ArchiveContentJudgement NotContent = new();

    public bool IsContent { get; init; }

    /// <summary>展開物の根とみなしたフォルダ。まとめて扱う単位になる。</summary>
    public string? ProductFolder { get; init; }

    /// <summary>そう判断した理由。UIでそのまま見せる。</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 「配布物そのもの」ではなく「配布物を展開した中身」であるファイルを見分ける。
///
/// <see cref="UnpackedFolderDetector"/> は同じ場所に同名のアーカイブがあることを条件にしており、
/// zipを消した後の展開先を拾えない。実測でも rurune_v1.1.3 がこれに当たり、
/// texture 配下の psd と png が36件、未確定に溜まっていた。
///
/// 名前の見た目で決めない。フォルダ名の流行りは商品ごとに違うので、
/// 「その木の中に配布物の形をしたファイルがある」という事実だけを根拠にする。
/// </summary>
public static class ArchiveContentDetector
{
    /// <summary>
    /// 展開物の中にあることを示すファイル。
    /// いずれも「配布された一式の一部」であって、単体で売られる形ではない。
    /// </summary>
    private static readonly string[] MarkerExtensions = [".unitypackage"];

    /// <summary>親をたどる深さの上限。これを超えると別の商品の木に踏み込む恐れがある。</summary>
    private const int MaxDepth = 6;

    /// <summary>
    /// このファイルが展開物の中身か。
    /// 親をたどって目印を持つフォルダを探し、いちばん外側で見つかったものを根とする
    /// （入れ子になっている場合、外側の方が配布の単位に近いため）。
    /// </summary>
    public static ArchiveContentJudgement Judge(string filePath, Func<string, IReadOnlyList<string>>? listFiles = null)
    {
        var list = listFiles ?? SafeListFileNames;
        var directory = Path.GetDirectoryName(filePath);

        string? outermost = null;
        string? reason = null;

        for (var depth = 0; depth < MaxDepth && !string.IsNullOrEmpty(directory); depth++)
        {
            var marker = FindMarker(list(directory));
            if (marker is not null)
            {
                outermost = directory;
                reason = $"「{marker}」と同じフォルダの中にあるので、配布物を展開したものとみなしました";
            }

            directory = Path.GetDirectoryName(directory);
        }

        return outermost is null
            ? ArchiveContentJudgement.NotContent
            : new ArchiveContentJudgement { IsContent = true, ProductFolder = outermost, Reason = reason };
    }

    private static string? FindMarker(IReadOnlyList<string> fileNames)
    {
        foreach (var name in fileNames)
        {
            if (MarkerExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            {
                return name;
            }

            // 同梱のBOOTHリンク。配布物に付いてくるもので、単体では出回らない
            if (Path.GetExtension(name).Equals(".url", StringComparison.OrdinalIgnoreCase)
                && name.Contains("BOOTH", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SafeListFileNames(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
