using Chmonos.Core.Booth;

namespace Chmonos.Core.Resolution;

/// <summary>
/// BOOTH で見つからなくなった商品の、新しいIDの候補1件（「IDを変える」の自動検索）。
/// **選んでもIDの欄に入るだけ**で、移すのは今の「調べる」→「この内容で移す」の流れ（推定した値を勝手に入れない）。
/// </summary>
public sealed record ReplacementCandidate
{
    public required string ItemId { get; init; }

    public string? Name { get; init; }

    public string? ShopName { get; init; }

    public string? ShopSubdomain { get; init; }

    /// <summary>今の商品と同じショップか。作者が出し直した物はたいてい同じショップに出る。ショップごと移った物は外れるので、札にとどめて候補からは外さない。</summary>
    public bool IsSameShop { get; init; }

    /// <summary>未確定の自動検索と同じ点（<see cref="FallbackResolver.Score"/>）。並べる順に使う。</summary>
    public int Score { get; init; }

    /// <summary>どの手掛かりで見つけたか（「前の商品名」か、ファイルの名前）。何から辿ったかを隠さない。</summary>
    public required string FoundBy { get; init; }

    /// <summary>1枚目の画像。取れなければ null（絵が無いだけで候補からは外さない）。</summary>
    public byte[]? Image { get; init; }
}

/// <param name="Searches">BOOTH の検索を引いた回数。上限（<see cref="FallbackResolver.MaxReplacementSearches"/>）を試験で確かめるために返す。</param>
public sealed record ReplacementProposal(IReadOnlyList<ReplacementCandidate> Candidates, bool BoothUnreachable, int Searches);

public sealed partial class FallbackResolver
{
    /// <summary>
    /// 検索を引く回数の上限。前の商品名で1回、足りなければ手元のファイルの名前で3本まで（ユーザ判断 2026-10-06）。
    /// 未確定の自動検索は1ファイルで引き直しを重ねるが、こちらは手掛かりが複数あるので、引き直しの代わりに別のファイルで引く。
    /// </summary>
    public const int MaxReplacementSearches = 4;

    /// <summary>ファイルの名前で引く本数の上限。</summary>
    private const int MaxReplacementFileQueries = MaxReplacementSearches - 1;

    /// <summary>
    /// 窓に並べる候補の数。絵を1枚ずつ取るので、並べる分だけに絞る（取る絵の数もこれで決まる）。
    /// 5にしたのは、1回の検索で確かめる3件に、別の手掛かりで出た分が並ぶ余地を残すため
    /// </summary>
    public const int MaxReplacementShown = 5;

    /// <summary>
    /// 新しいIDを探す。**何も書かない。**問い合わせは門（<see cref="BoothClient"/>）を通るので1本ずつ・1.5秒以上空く。
    /// <list type="number">
    ///   <item>前の商品名で検索（1回）。名前の語が全部そろう候補か、確度の高い候補が出たらそこで止める</item>
    ///   <item>出なければ、手元のファイルの名前で（zip・unitypackage を先に）。検索語の重なる物は1本にまとめ、3本まで。同じく出たら止める</item>
    /// </list>
    /// 各検索の上位3件だけ商品JSONを取って点を付ける（未確定の自動検索と同じ数・同じ点）。今のIDは出さない。
    /// 絵は付けない（<see cref="FindReplacementWithImagesAsync"/>）。
    /// </summary>
    /// <param name="fromId">今の商品のID。候補から外す。</param>
    /// <param name="previousName">前の商品名（自分で付けた名前か、BOOTHの名前）。無ければファイルの名前から始める。</param>
    /// <param name="shopSubdomain">今の商品のショップ。同じショップの札に使う。</param>
    /// <param name="paths">手元のファイルとフォルダの場所。</param>
    public async Task<ReplacementProposal> FindReplacementAsync(
        string fromId,
        string? previousName,
        string? shopSubdomain,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default,
        IProgress<ResolveProgress>? progress = null)
        => (await FindReplacementCoreAsync(fromId, previousName, shopSubdomain, paths, cancellationToken, progress)).Proposal;

    /// <summary>候補を探して、並べる分に1枚目の絵を付ける（「IDを変える」の自動検索が呼ぶ1本）。絵は並べる分だけ取る。</summary>
    public async Task<ReplacementProposal> FindReplacementWithImagesAsync(
        string fromId,
        string? previousName,
        string? shopSubdomain,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default,
        IProgress<ResolveProgress>? progress = null)
    {
        var (proposal, firstImages) = await FindReplacementCoreAsync(fromId, previousName, shopSubdomain, paths, cancellationToken, progress);

        var withImages = new List<ReplacementCandidate>();
        for (var index = 0; index < proposal.Candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = proposal.Candidates[index];
            if (!firstImages.TryGetValue(candidate.ItemId, out var url))
            {
                withImages.Add(candidate);
                continue;
            }

            progress?.Report(new ResolveProgress("候補の画像を取得しています", index, proposal.Candidates.Count));
            var image = await _client.GetBinaryAsync(url, cancellationToken);
            withImages.Add(image.IsSuccess && image.Value is { Length: > 0 } bytes ? candidate with { Image = bytes } : candidate);
        }

        return proposal with { Candidates = withImages };
    }

    private async Task<(ReplacementProposal Proposal, Dictionary<string, string> FirstImages)> FindReplacementCoreAsync(
        string fromId,
        string? previousName,
        string? shopSubdomain,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken,
        IProgress<ResolveProgress>? progress)
    {
        var avatars = _avatarRegistry is null ? null : AvatarTokens.From(_avatarRegistry(), _readings);
        var isAvatarName = SiblingTokens.NotProductName(avatars, null);

        var found = new List<(ResolutionCandidate Candidate, string FoundBy)>();

        // 確かめた候補の1枚目の画像の場所。商品JSONを取った時点で分かるので、絵を取るときに取り直さない。
        // 解決器は未確定の自動検索と共有なので、覚えるのは呼び出しごと
        var firstImages = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { fromId };
        var searches = 0;
        var reached = false;
        var usedQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 名前は検索語の整え方（括弧の付け足し・版番号・配布形態の語を落とす）をファイル名と共にする
        var namePath = previousName is { Length: > 0 } name ? AsPseudoFileName(name) : null;
        var nameQuery = namePath is null ? string.Empty : FileNameQuery.ToSearchQuery(namePath, isAvatarName);

        var stopped = false;
        if (nameQuery.Length > 0)
        {
            usedQueries.Add(nameQuery);
            stopped = await StageAsync(nameQuery, namePath!, new UnityPackageHints(), "前の商品名", $"前の商品名で探しています（{nameQuery}）");
        }

        if (!stopped)
        {
            var fileQueries = new List<(string Query, string Path)>();
            foreach (var path in ArchivesFirst(paths))
            {
                if (fileQueries.Count >= MaxReplacementFileQueries)
                {
                    break;
                }

                var query = FileNameQuery.ToSearchQuery(path, isAvatarName);
                if (query.Length > 0 && usedQueries.Add(query))
                {
                    fileQueries.Add((query, path));
                }
            }

            foreach (var (query, path) in fileQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(path.TrimEnd('\\', '/'));

                // 大きい zip は中を読むだけで数秒かかるので裏で読む（未確定の自動検索と同じ）
                var hints = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                    ? await Task.Run(() => UnityPackageInspector.Inspect(path), cancellationToken)
                    : new UnityPackageHints();

                if (await StageAsync(query, path, hints, $"「{fileName}」の名前", $"ファイル名で探しています（{query}）"))
                {
                    break;
                }
            }
        }

        var shown = found
            .Select((entry, order) => (entry.Candidate, entry.FoundBy, order))
            .OrderByDescending(entry => entry.Candidate.Score)
            .ThenByDescending(entry => IsSame(entry.Candidate.ShopSubdomain))
            .ThenBy(entry => entry.order)
            .Take(MaxReplacementShown)
            .Select(entry => new ReplacementCandidate
            {
                ItemId = entry.Candidate.ItemId,
                Name = entry.Candidate.Name,
                ShopName = entry.Candidate.ShopName,
                ShopSubdomain = entry.Candidate.ShopSubdomain,
                IsSameShop = IsSame(entry.Candidate.ShopSubdomain),
                Score = entry.Candidate.Score,
                FoundBy = entry.FoundBy,
            })
            .ToList();

        // 1本も届かなかったときだけ「届かなかった」と言う（E3。0件と言い分ける）
        return (new ReplacementProposal(shown, BoothUnreachable: searches > 0 && !reached, searches), firstImages);

        // 1本の検索と、その上位3件の確かめ。止めてよい候補が出たら true
        async Task<bool> StageAsync(string query, string path, UnityPackageHints hints, string foundBy, string phase)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ResolveProgress(phase, 0, 0));

            searches++;
            var (ids, searched) = await SearchIdsAsync(query, path, avatars, null, cancellationToken);
            reached |= searched;

            // zip に書かれた商品URLは検索より確かな手掛かり（未確定の自動検索と同じ）。今のIDを指していれば外れる
            var direct = hints.Clues
                .Where(clue => clue.ItemId is not null)
                .Select(clue => clue.ItemId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var toCheck = direct.Concat(ids).Where(seen.Add).Take(MaxCandidates).ToList();
            var numbers = FileNameQuery.SignificantNumbers(path).Concat(FileNameQuery.SeriesNumbers(path)).Distinct().ToList();

            for (var index = 0; index < toCheck.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ResolveProgress("候補を1件ずつ確認しています", index, toCheck.Count));

                var itemId = toCheck[index];
                if (await CheckReplacementAsync(itemId, query, path, hints, IndexOf(ids, itemId), direct.Contains(itemId), numbers, cancellationToken)
                    is { } checkedCandidate)
                {
                    found.Add((checkedCandidate.Candidate, foundBy));
                    if (checkedCandidate.ImageUrl is { Length: > 0 } imageUrl)
                    {
                        firstImages[itemId] = imageUrl;
                    }
                }
            }

            return found.Any(entry => entry.Candidate.IsStrong || HasAllWords(entry.Candidate.Name, query));
        }

        bool IsSame(string? candidateShop) => shopSubdomain is { Length: > 0 } mine
            && string.Equals(candidateShop, mine, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(ResolutionCandidate Candidate, string? ImageUrl)?> CheckReplacementAsync(
        string itemId,
        string query,
        string path,
        UnityPackageHints hints,
        int rank,
        bool fromDirectUrl,
        IReadOnlyList<string> numbers,
        CancellationToken cancellationToken)
    {
        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
        if (!jsonResult.IsSuccess || jsonResult.Value is null
            || BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, itemId: itemId) is not { } booth)
        {
            return null;
        }

        var readingMatch = booth.Name is null
            ? null
            : ReadingMatch.Find(query, booth.Name, _bridge, _readings, FileNameQuery.UndividedTokens(path));

        var candidate = Score(
            itemId, booth.Name, booth.Shop?.Name, booth.Shop?.Subdomain, query, hints, rank, fromDirectUrl, numbers, readingMatch);
        return (candidate, booth.Images.FirstOrDefault()?.OriginalUrl);
    }

    /// <summary>zip・unitypackage を先に。配布者の付けた名前そのものなので、展開した中のファイル名より商品名に近い。</summary>
    private static IEnumerable<string> ArchivesFirst(IReadOnlyList<string> paths)
        => paths
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((path, order) => (path, order, rank: Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".zip" or ".unitypackage" => 0,
                "" => 2,
                _ => 1,
            }))
            .OrderBy(entry => entry.rank)
            .ThenBy(entry => entry.order)
            .Select(entry => entry.path);

    /// <summary>
    /// 商品名をファイル名として整えられる形にする。パスに使えない字は区切りにし、末尾に拡張子を足す
    /// （足さないと、名前の「Ver1.0」の「.0」が拡張子として切られる）。
    /// </summary>
    private static string AsPseudoFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(character => invalid.Contains(character) ? ' ' : character).ToArray()).Trim();
        return safe + ".name";
    }
}
