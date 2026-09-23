using System.Text.RegularExpressions;
using BoothAssetManager.Core.Booth;

namespace BoothAssetManager.Core.Resolution;

/// <summary>
/// 自動検索の進み具合。<see cref="Total"/> が0なら件数の分からない段階（準備中）。
/// 1件ずつ間隔を空けて取りに行くので、黙って待たせると止まったように見える。
/// </summary>
public sealed record ResolveProgress(string Phase, int Current, int Total)
{
    public bool HasTotal => Total > 0;
}

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
/// <summary>
/// 候補と、**BOOTH に届いたかどうか**（ユーザ判断 2026-09-20・E3）。
/// 届かなかったのを「候補がありません」と同じ文で出すと、次の一手（商品IDを直接入れる）が
/// 誤った前提に立つ——実際は待ってもう一度押せばよい。
/// </summary>
public sealed record ResolutionProposal(IReadOnlyList<ResolutionCandidate> Candidates, bool BoothUnreachable);

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
    private readonly Search.SearchBridge? _bridge;
    private readonly Search.KanjiReadings? _readings;

    /// <param name="bridge">読みから別表記を作るもの。渡さなければ読みの照合をしないだけ。</param>
    /// <param name="readings">商品名の読みを作るもの。造語の照合に要る。</param>
    public FallbackResolver(
        IBoothClient client,
        Search.SearchBridge? bridge = null,
        Search.KanjiReadings? readings = null)
    {
        _client = client;
        _bridge = bridge;
        _readings = readings;
    }

    /// <summary>検索結果の商品カード1枚。名前とショップが載っているので、通信せずに並べ直せる。</summary>
    public sealed record SearchCard(string ItemId, string Name, string ShopSubdomain);

    private static readonly Regex CardTagRegex = new(@"<li[^>]*class=""item-card[^""]*""[^>]*>", RegexOptions.Compiled);

    /// <summary>検索結果HTMLから商品カードを表示順に取り出す。</summary>
    public static IReadOnlyList<SearchCard> ExtractSearchCards(string html)
    {
        var cards = new List<SearchCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match tag in CardTagRegex.Matches(html))
        {
            var id = Attribute(tag.Value, "data-product-id");
            if (id.Length > 0 && seen.Add(id))
            {
                cards.Add(new SearchCard(id, Attribute(tag.Value, "data-product-name"), Attribute(tag.Value, "data-product-brand")));
            }
        }

        return cards;
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, name + @"=""([^""]*)""");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    /// <summary>
    /// 検索結果の1ページ（最大60件）を、ファイル名との近さで並べ直す。**通信は増えない。**
    ///
    /// 確かめる（商品JSONを取る）のは上位3件だけなので、並びがそのまま当たり外れになる。
    /// BOOTHの並びは人気寄りで、連番のシリーズ物は番号を捨てた検索語だと十数位に沈み、
    /// ファイル名の頭や尻に付いたショップ名（sampleflow_ / _samplecat）も効かない。
    /// 点が同じならBOOTHの並びを保つ。
    /// </summary>
    public static IReadOnlyList<SearchCard> Rerank(IReadOnlyList<SearchCard> cards, string filePath)
    {
        var tokens = FileNameQuery.Tokens(filePath);
        var numbers = FileNameQuery.SeriesNumbers(filePath).Concat(FileNameQuery.SignificantNumbers(filePath)).Distinct().ToList();
        var raw = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

        return cards
            .Select((card, position) =>
            {
                var score = tokens.Count(token => FileNameQuery.LooksRelated(card.Name, token)) * 2;

                if (numbers.Any(number => ContainsNumber(card.Name, number)))
                {
                    score += 3;
                }

                if (card.ShopSubdomain.Length >= 3 && raw.Contains(card.ShopSubdomain.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    score += 3;
                }

                return (card, position, score);
            })
            .OrderByDescending(entry => entry.score)
            .ThenBy(entry => entry.position)
            .Select(entry => entry.card)
            .ToList();
    }

    /// <summary>
    /// 裏付けのある候補が出なかったときに引き直す語を、試す順に返す。
    /// <list type="number">
    ///   <item><b>いちばん特徴のある1語</b>。AND 検索は商品名に無い語が1つ混ざるだけで全滅する。
    ///   整えても残る余計な語（アバター名・「Basic」など）を丸ごと外せる</item>
    ///   <item><b>別の表記</b>（<see cref="AlternateQueries"/>）。ローマ字や英単語の商品名</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> RetryQueries(string filePath, string query, Search.SearchBridge? bridge)
    {
        var results = new List<string>();

        var distinctive = FileNameQuery.MostDistinctiveToken(filePath);
        if (distinctive.Length > 0 && !string.Equals(distinctive, query, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(distinctive);
        }

        foreach (var alternate in AlternateQueries.For(filePath, query, bridge))
        {
            if (!string.Equals(alternate, query, StringComparison.OrdinalIgnoreCase)
                && !results.Contains(alternate, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(alternate);
            }
        }

        return results;
    }

    /// <summary>数字として含むか。「13」が「113」や「2013」に当たらないように、前後が数字でないことを見る。</summary>
    private static bool ContainsNumber(string text, string number)
        => Regex.IsMatch(text, $@"(?<![0-9]){Regex.Escape(number)}(?![0-9])");

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

    public async Task<ResolutionProposal> ProposeAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        IProgress<ResolveProgress>? progress = null)
    {
        var query = FileNameQuery.ToSearchQuery(filePath);
        if (query.Length == 0)
        {
            return new ResolutionProposal([], false);
        }

        // 大きいzipだとここだけで数秒かかるので、何をしているかは伝える
        progress?.Report(new ResolveProgress("アーカイブの中を調べています", 0, 0));

        // **裏で読む。**unitypackage は最後まで展開しないと中身のパスが揃わず、1GB で約3秒かかる（実測）。
        // ここは最初の await より前なので、そのまま呼ぶと呼んだ側（未確定の画面）のスレッドで走り、その間画面が止まる
        var hints = Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => UnityPackageInspector.Inspect(filePath), cancellationToken)
            : new UnityPackageHints();

        // unitypackage 内のテキストに商品URLが直接書かれていれば、検索するまでもない
        var direct = hints.Clues
            .Where(clue => clue.ItemId is not null)
            .Select(clue => clue.ItemId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        progress?.Report(new ResolveProgress($"BOOTHを検索しています（{query}）", 0, 0));

        var (searchIds, searched) = await SearchIdsAsync(query, filePath, cancellationToken);

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

            // 1件ずつ間隔を空けて取るので、ここが一番待たされる。件数を出す
            progress?.Report(new ResolveProgress("候補を1件ずつ確認しています", rank, orderedIds.Count));

            var candidate = await ScoreCandidateAsync(
                itemId, query, filePath, hints, rank, direct, cancellationToken);

            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        // ── 裏付けのある候補が1件も出なければ、1語だけで、次に別の表記で引き直す ──
        //
        // 整えた検索語でも商品名に無い語が残ると AND 検索が全滅する。
        // ローマ字のファイル名が日本語の商品を指していると、そのままでは当たらない。
        // 実測では Tori → 「鳥」1位、HeartBeat → 「心拍」1位 が別の表記で拾えた。
        //
        // **当たっているときは引き直さない。**1回につきBOOTHへの問い合わせが
        // 1本増えるうえ、出てきた候補ごとに商品JSONも取ることになる。
        // 先の検索で弱い候補が3件出ていても、引き直しの候補は別枠で3件まで確かめる
        // （枠を共有すると、弱い候補で埋まった時点で引き直しが何も足さなくなる）
        if (!candidates.Any(candidate => candidate.IsStrong))
        {
            var seen = orderedIds.ToHashSet(StringComparer.Ordinal);

            foreach (var alternate in RetryQueries(filePath, query, _bridge))
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new ResolveProgress($"別の語で探しています（{alternate}）", 0, 0));

                var extraIds = (await SearchIdsAsync(alternate, filePath, cancellationToken)).Ids
                    .Where(id => seen.Add(id))
                    .Take(MaxCandidates)
                    .ToList();

                for (var rank = 0; rank < extraIds.Count; rank++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ResolveProgress("候補を1件ずつ確認しています", rank, extraIds.Count));

                    var extra = await ScoreCandidateAsync(
                        extraIds[rank], alternate, filePath, hints, rank, direct, cancellationToken);

                    if (extra is not null)
                    {
                        candidates.Add(extra);
                    }
                }

                // 裏付けが出たらそこで止める。念のためもう1語、はしない
                if (candidates.Any(candidate => candidate.IsStrong))
                {
                    break;
                }
            }
        }

        // 検索そのものが届かなかったときは、候補が0件でも「無い」と言わせない（E3）
        return new ResolutionProposal(
            candidates.OrderByDescending(candidate => candidate.Score).ToList(),
            BoothUnreachable: !searched && direct.Count == 0);
    }

    /// <summary>
    /// 検索して、ファイル名との近さで並べ直したIDを返す。カードが読めなければBOOTHの並びのまま。
    /// **届いたかどうかも返す**（E3：届かなかったのを0件と同じに扱っていた）。
    /// </summary>
    private async Task<(IReadOnlyList<string> Ids, bool Searched)> SearchIdsAsync(
        string query, string filePath, CancellationToken cancellationToken)
    {
        var result = await _client.SearchAsync(query, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ([], false);
        }

        var cards = ExtractSearchCards(result.Value);
        return (cards.Count > 0
            ? Rerank(cards, filePath).Select(card => card.ItemId).ToList()
            : ExtractSearchResultIds(result.Value), true);
    }

    /// <summary>候補1件を取って点数を付ける。取れなければ null。</summary>
    private async Task<ResolutionCandidate?> ScoreCandidateAsync(
        string itemId,
        string query,
        string filePath,
        UnityPackageHints hints,
        int rank,
        List<string> direct,
        CancellationToken cancellationToken)
    {
        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            return null;
        }

        // 読めない応答は、取れなかったときと同じく候補から外す
        if (BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, itemId: itemId) is not { } booth)
        {
            return null;
        }

        // 商品名はもう取ってあるので、読みの照合に通信は要らない
        var readingMatch = booth.Name is null
            ? null
            : ReadingMatch.Find(query, booth.Name, _bridge, _readings, FileNameQuery.UndividedTokens(filePath));

        return Score(
            itemId,
            booth.Name,
            booth.Shop?.Name,
            booth.Shop?.Subdomain,
            query,
            hints,
            rank,
            direct.Contains(itemId),
            FileNameQuery.SignificantNumbers(filePath).Concat(FileNameQuery.SeriesNumbers(filePath)).Distinct().ToList(),
            readingMatch);
    }

    /// <summary>候補の点数付け。UIで根拠をそのまま見せられるよう、理由も一緒に組み立てる。</summary>
    /// <param name="readingMatch">
    /// ファイル名と商品名が読みで一致した語。<see cref="ReadingMatch.Find"/> の結果。
    /// ラテン文字のファイル名は日本語商品のローマ字表記であることが多く、
    /// 文字の突き合わせだけでは当たらない。
    /// </param>
    public static ResolutionCandidate Score(
        string itemId,
        string? itemName,
        string? shopName,
        string? shopSubdomain,
        string query,
        UnityPackageHints hints,
        int rank,
        bool fromDirectUrl = false,
        IReadOnlyList<string>? significantNumbers = null,
        string? readingMatch = null)
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

        // 商品名の一致と同じ重み。読みで一致するのは、表記が違うだけで
        // 同じものを指していることが多い（tori ↔ 鳥、Sin ↔ 真）
        if (readingMatch is not null)
        {
            score += 2;
            reasons.Add($"ファイル名が商品名と読みで一致（{readingMatch}）");
        }

        if (itemName is not null && hints.ProductNamespaces.Any(product => FileNameQuery.LooksRelated(itemName, product)))
        {
            score += 2;
            reasons.Add("商品名がunitypackage内のフォルダ名と一致");
        }

        if (itemName is not null && significantNumbers is not null
            && significantNumbers.Any(number => ContainsNumber(itemName, number)))
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
