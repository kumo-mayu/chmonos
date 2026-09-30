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
        => new Pass(listFiles).Judge(filePath);

    /// <summary>
    /// 続けて何件も見分ける1回の処理（未確定の行を1回組む間）。**フォルダごとの目印を、この1回の間だけ覚える。**
    ///
    /// 1件ごとに親を <see cref="MaxDepth"/> 段たどって列挙するので、呼ぶ側がフォルダごとに1回にまとめても、
    /// 親の段（取り込み元・その上）は全部のフォルダで同じ物を列挙し直していた（未確定 8万件・803 フォルダで約 4,800 回、
    /// 行を組む時間の約3割。2026-09-30 に測った）。同じフォルダは1回だけ列挙する。
    ///
    /// **処理をまたいで持たない。**またぐと、フォルダの中身が変わった（zip を展開した・目印を消した）のに古い答えを返す。
    /// 覚えるのは目印の名前だけ（ファイル名の一覧は持たない。1つのフォルダに数千件あっても1語）。
    /// 1本のスレッドから使う。
    /// </summary>
    public sealed class Pass
    {
        private readonly Func<string, IReadOnlyList<string>> _list;

        /// <summary>フォルダ → 目印の名前（無ければ null）。パスの大文字小文字は区別しない（Windows の置き場）。</summary>
        private readonly Dictionary<string, string?> _markers = new(StringComparer.OrdinalIgnoreCase);

        public Pass(Func<string, IReadOnlyList<string>>? listFiles = null)
        {
            _list = listFiles ?? SafeListFileNames;
        }

        /// <inheritdoc cref="ArchiveContentDetector.Judge"/>
        public ArchiveContentJudgement Judge(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);

            string? outermost = null;
            string? reason = null;

            for (var depth = 0; depth < MaxDepth && !string.IsNullOrEmpty(directory); depth++)
            {
                var marker = MarkerIn(directory);
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

        private string? MarkerIn(string directory)
        {
            if (!_markers.TryGetValue(directory, out var marker))
            {
                marker = FindMarker(_list(directory));
                _markers[directory] = marker;
            }

            return marker;
        }
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
