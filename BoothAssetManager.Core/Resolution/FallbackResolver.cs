using System.Text.RegularExpressions;
using BoothAssetManager.Core.Booth;

namespace BoothAssetManager.Core.Resolution;

public sealed class ResolutionCandidate
{
    public required string ItemId { get; init; }

    public string? Name { get; init; }

    public string? ShopName { get; init; }

    public string? ShopSubdomain { get; init; }

    public int Score { get; init; }

    /// <summary>なぜこの候補なのかの根拠。UIでそのまま見せる。</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// 自動で確定してよい水準の裏付けがあるか。
    ///
    /// 閾値を7にしているのは実測の結果。5にすると、同じ作者の別商品
    /// （ショップ名は一致するが商品が違う）が「裏付けあり」に紛れ込んだ。
    /// ショップの一致はショップの証拠であって商品の証拠ではないので、
    /// 商品名側の裏付けも重ねて要求する。
    /// </summary>
    public bool IsStrong => Score >= 7;
}

/// <summary>
/// Zone.Identifier とZIP内テキストで決められなかったファイルに、候補を提示する。
///
/// 実測（Brave経由で落とした既存ライブラリ）では、この2つの手掛かりだけでは48本中1本しか解決できなかった。
/// 一方で、ファイル名からBOOTH内検索を引くと正解が1位に出ること、
/// <c>.unitypackage</c> の作者名前空間がショップ名と一致することが分かったので、
/// 「検索で候補を出し、ローカルの手掛かりで検証する」形にしている。
///
/// 発見（検索）は多少雑でよく、精度は検証側で担保する。
/// </summary>
public sealed class FallbackResolver
{
    private const int MaxCandidates = 3;

    /// <summary>
    /// 検索結果の商品カードに付く属性。
    /// ページには推薦枠やヘッダのリンクも含まれるため、単純に <c>/items/{id}</c> を拾うと
    /// 検索語と無関係な商品が先に並んでしまう（実測で全ての検索語が同じ3件を返した）。
    /// </summary>
    private static readonly Regex ProductIdRegex = new(@"data-product-id=""(\d+)""", RegexOptions.Compiled);

    private static readonly Regex ItemLinkRegex = new(@"/items/(\d+)", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumericRegex = new(@"[^\p{L}\p{N}]", RegexOptions.Compiled);

    private readonly IBoothClient _client;

    public FallbackResolver(IBoothClient client)
    {
        _client = client;
    }

    /// <summary>
    /// 検索結果HTMLから商品IDを表示順に取り出す。
    /// 商品カードの <c>data-product-id</c> を優先し、それが無い場合だけリンクから拾う。
    /// </summary>
    public static IReadOnlyList<string> ExtractSearchResultIds(string html)
    {
        var ids = Collect(ProductIdRegex, html);
        return ids.Count > 0 ? ids : Collect(ItemLinkRegex, html);
    }

    private static List<string> Collect(Regex regex, string html)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in regex.Matches(html))
        {
            var id = match.Groups[1].Value;
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public async Task<IReadOnlyList<ResolutionCandidate>> ProposeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var query = FileNameQuery.ToSearchQuery(filePath);
        if (query.Length == 0)
        {
            return [];
        }

        var hints = Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? UnityPackageInspector.Inspect(filePath)
            : new UnityPackageHints();

        // unitypackage 内のテキストに商品URLが直接書かれていれば、検索するまでもない
        var direct = hints.Clues
            .Where(clue => clue.ItemId is not null)
            .Select(clue => clue.ItemId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var searchResult = await _client.SearchAsync(query, cancellationToken);
        var searchIds = searchResult.IsSuccess && searchResult.Value is not null
            ? ExtractSearchResultIds(searchResult.Value)
            : [];

        var orderedIds = direct
            .Concat(searchIds)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

        var candidates = new List<ResolutionCandidate>();
        for (var rank = 0; rank < orderedIds.Count; rank++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemId = orderedIds[rank];
            var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
            if (!jsonResult.IsSuccess || jsonResult.Value is null)
            {
                continue;
            }

            var booth = BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now);
            candidates.Add(Score(
                itemId,
                booth.Name,
                booth.Shop?.Name,
                booth.Shop?.Subdomain,
                query,
                hints,
                rank,
                direct.Contains(itemId),
                FileNameQuery.SignificantNumbers(filePath)));
        }

        return candidates.OrderByDescending(candidate => candidate.Score).ToList();
    }

    /// <summary>候補の点数付け。UIで根拠をそのまま見せられるよう、理由も一緒に組み立てる。</summary>
    public static ResolutionCandidate Score(
        string itemId,
        string? itemName,
        string? shopName,
        string? shopSubdomain,
        string query,
        UnityPackageHints hints,
        int rank,
        bool fromDirectUrl = false,
        IReadOnlyList<string>? significantNumbers = null)
    {
        var score = 0;
        var reasons = new List<string>();

        if (fromDirectUrl)
        {
            // 同梱物に書かれた商品URLは決定的な根拠なので、単独で確定水準に達させる。
            score += 7;
            reasons.Add("同梱テキストに商品URLが直接書かれていた");
        }

        if (MatchesAuthorNamespace(shopSubdomain, shopName, hints))
        {
            score += 3;
            reasons.Add("unitypackageの作者名前空間がショップ名と一致");
        }

        if (itemName is not null && FileNameQuery.LooksRelated(itemName, query))
        {
            score += 2;
            reasons.Add("商品名がファイル名と一致");
        }

        if (itemName is not null && hints.ProductNamespaces.Any(product => FileNameQuery.LooksRelated(itemName, product)))
        {
            score += 2;
            reasons.Add("商品名がunitypackage内のフォルダ名と一致");
        }

        if (itemName is not null && significantNumbers is not null
            && significantNumbers.Any(number => itemName.Contains(number, StringComparison.Ordinal)))
        {
            score += 1;
            reasons.Add("ファイル名の番号が商品名と一致");
        }

        if (rank == 0)
        {
            score += 1;
            reasons.Add("検索結果の1位");
        }

        return new ResolutionCandidate
        {
            ItemId = itemId,
            Name = itemName,
            ShopName = shopName,
            ShopSubdomain = shopSubdomain,
            Score = score,
            Reasons = reasons,
        };
    }

    private static bool MatchesAuthorNamespace(string? shopSubdomain, string? shopName, UnityPackageHints hints)
    {
        if (hints.AuthorNamespaces.Count == 0)
        {
            return false;
        }

        foreach (var author in hints.AuthorNamespaces)
        {
            var normalizedAuthor = Normalize(author);
            if (normalizedAuthor.Length < 3)
            {
                continue;
            }

            if (Contains(shopSubdomain, normalizedAuthor) || Contains(shopName, normalizedAuthor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string? value, string normalizedAuthor)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        return normalized.Contains(normalizedAuthor, StringComparison.OrdinalIgnoreCase)
            || normalizedAuthor.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>記号や空白の違いを無視して比べられるようにする（SHOP HEILON と shopheilon など）。</summary>
    private static string Normalize(string value) => NonAlphanumericRegex.Replace(value, string.Empty);
}
