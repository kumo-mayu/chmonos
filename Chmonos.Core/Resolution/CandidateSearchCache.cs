using Chmonos.Core.Storage;

namespace Chmonos.Core.Resolution;

/// <summary>未確定の自動検索の控えのファイル（<c>unresolved-searches.json</c>）。</summary>
public sealed record CandidateSearchFile
{
    public IReadOnlyList<CandidateSearchEntry> Searches { get; init; } = [];
}

/// <summary>
/// 自動検索の控え1件。**1件は検索した対象ごと**（元zip・展開物の根のフォルダ・ファイル。未確定の画面の <c>SearchTargetPath</c>）で、
/// その対象で引く未確定のファイル（持ち主）のハッシュを並べる。
///
/// ファイルごとに候補を写さないのは、zip の中身の束は全件が同じ元zipの名前で引くから（同じ候補を何百回も書くことになる）。
/// 持ち主が決まる・未確定から消えるたびに外し、持ち主が1件も残らなければ控えごと消す（ユーザ判断 2026-11-11）。
/// </summary>
public sealed record CandidateSearchEntry
{
    /// <summary>検索した対象の場所。</summary>
    public required string Target { get; init; }

    /// <summary>この対象で引く未確定のファイルのハッシュ。</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>検索した日時（人が読むため。古くなっても引き直さない）。</summary>
    public DateTimeOffset SearchedAt { get; init; }

    public IReadOnlyList<ResolutionCandidate> Candidates { get; init; } = [];
}

/// <summary>
/// 未確定の自動検索の結果を、保存先の中の1つのファイルに控える（ユーザ判断 2026-11-11・plan 21）。
///
/// **最初に取った物を使い続ける**：先回り（<see cref="CandidatePrefetch"/>）は控えの無い対象にだけ書き、古くなっても引き直さない
/// （「非公開になる商品も多いので手元にある時点で古い方が信頼しやすく再検索があれば問題ない」）。
/// 置き換えるのは、控えのある対象で人が「再検索」を押したときだけ。
/// **BOOTH に届かなかった結果は書かない**：書くと、届かなかった回が「候補なし」のまま残り、先回りも引き直さない。
///
/// 控えは候補として見せるだけで、人が選んだときだけ登録する（推した値を勝手に入れない）。
/// </summary>
public sealed class CandidateSearchCache(JsonFileStore<CandidateSearchFile> store)
{
    /// <summary>控えを書いた・置き換えた（対象の場所）。開いている画面が、選んでいる対象なら出し直す。裏のスレッドから来る</summary>
    public event Action<string>? Remembered;

    /// <summary>対象の控え。無ければ null。</summary>
    public CandidateSearchEntry? Find(string target)
        => Read().Searches.FirstOrDefault(entry => SameTarget(entry.Target, target));

    /// <summary>控えのある対象の場所（先回りが飛ばす物）。</summary>
    public IReadOnlySet<string> Targets()
        => Read().Searches.Select(entry => entry.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 先回りの結果を控える。**控えが既にあれば書かない**（最初に取った物を使い続ける。先回りの間に人が引いた結果も消さない）。
    /// </summary>
    /// <returns>書いたか。</returns>
    public Task<bool> AddIfMissingAsync(
        string target, IEnumerable<string> files, ResolutionProposal proposal, DateTimeOffset now, CancellationToken cancellationToken = default)
        => WriteAsync(target, files, proposal, now, replace: false, cancellationToken);

    /// <summary>人が押した「再検索」の結果で、**控えのある対象だけ**置き換える。控えの無い対象には書かない（押した結果は画面が開いている間だけ覚える）。</summary>
    /// <returns>書いたか。</returns>
    public Task<bool> ReplaceIfPresentAsync(
        string target, IEnumerable<string> files, ResolutionProposal proposal, DateTimeOffset now, CancellationToken cancellationToken = default)
        => WriteAsync(target, files, proposal, now, replace: true, cancellationToken);

    private async Task<bool> WriteAsync(
        string target, IEnumerable<string> files, ResolutionProposal proposal, DateTimeOffset now, bool replace, CancellationToken cancellationToken)
    {
        var owners = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (proposal.BoothUnreachable)
        {
            return false;
        }

        var written = await store.TryUpdateAsync(
            current =>
            {
                var present = current.Searches.FirstOrDefault(entry => SameTarget(entry.Target, target));
                // 先回りは無いときだけ、再検索は在るときだけ書く
                if ((present is null) == replace)
                {
                    return null;
                }

                // 置き換えで持ち主が渡らなかったとき（検索の間に一覧が読み直された）は、前の持ち主を残す
                var keep = owners.Count > 0 ? owners : present?.Files.ToList() ?? [];
                if (keep.Count == 0)
                {
                    return null;
                }

                var entry = new CandidateSearchEntry
                {
                    Target = target,
                    Files = keep,
                    SearchedAt = now,
                    Candidates = proposal.Candidates,
                };

                return current with
                {
                    Searches = present is null
                        ? [.. current.Searches, entry]
                        : current.Searches.Select(other => ReferenceEquals(other, present) ? entry : other).ToList(),
                };
            },
            cancellationToken);

        if (written)
        {
            Remembered?.Invoke(target);
        }

        return written;
    }

    /// <summary>決まった・外した・未確定から消えたファイルを持ち主から外し、持ち主の居なくなった控えを消す。</summary>
    /// <returns>書いたか。</returns>
    public Task<bool> ForgetAsync(IEnumerable<string> fileHashes, CancellationToken cancellationToken = default)
    {
        var gone = fileHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return gone.Count == 0
            ? Task.FromResult(false)
            : PruneAsync(hash => !gone.Contains(hash), cancellationToken);
    }

    /// <summary>
    /// 未確定の記録に残っているファイルだけを持ち主に残す。決める入口が増えても、ここで突き合わせれば控えが残り続けない
    /// （古いバージョンで片付けた後に戻した保存先・別の画面で片付けた分も拾う）。
    /// </summary>
    /// <returns>書いたか。</returns>
    public Task<bool> KeepOnlyAsync(IReadOnlySet<string> remaining, CancellationToken cancellationToken = default)
        => PruneAsync(remaining.Contains, cancellationToken);

    private Task<bool> PruneAsync(Func<string, bool> keep, CancellationToken cancellationToken)
    {
        // 控えが無いのに書き直さない（ファイルを作らない）
        if (Read().Searches.Count == 0)
        {
            return Task.FromResult(false);
        }

        return store.TryUpdateAsync(
            current =>
            {
                var changed = false;
                var kept = new List<CandidateSearchEntry>(current.Searches.Count);
                foreach (var entry in current.Searches)
                {
                    var files = entry.Files.Where(keep).ToList();
                    if (files.Count == entry.Files.Count)
                    {
                        kept.Add(entry);
                        continue;
                    }

                    changed = true;
                    if (files.Count > 0)
                    {
                        kept.Add(entry with { Files = files });
                    }
                }

                return changed ? current with { Searches = kept } : null;
            },
            cancellationToken);
    }

    /// <summary>
    /// 読む。**壊れた・新しすぎる控えは「控えなし」として読み、ログに1回残す**——未確定の画面は行を選ぶたびに画面のスレッドで読むので、
    /// 投げると選ぶたびに落ちる。書き換え（錠の中の読み直し）は今までどおり投げるので、壊れたファイルを黙って上書きはしない
    /// （控えは引き直せる物だが、人が直せる形を消さない。直すか消すまで先回りも書かない）
    /// </summary>
    private CandidateSearchFile Read()
    {
        try
        {
            return store.Load();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or FormatTooNewException)
        {
            if (Interlocked.Exchange(ref _readFailureLogged, 1) == 0)
            {
                Diagnostics.AppLog.Error("未確定の自動検索の控えを読む", exception);
            }

            return new CandidateSearchFile();
        }
    }

    private int _readFailureLogged;

    private static bool SameTarget(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
