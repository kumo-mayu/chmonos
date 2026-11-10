using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.App.Services;

/// <summary>
/// 未確定の自動検索の先回り（設定「未確定の候補を先に検索しておく」・既定はオフ。ユーザ判断 2026-11-10・2026-11-11・plan 21）。
/// 決まり（優先度⑧・取り込みの間は止める・新しい順・控えの扱い）は <see cref="CandidatePrefetch"/>。ここは、いつ始めて止めるかと、
/// 画面と同じ対象・同じ手掛かりで引くための組み立て。
///
/// <list type="bullet">
/// <item>**起動したときの⑦が終わってから始める**。「起動したとき、裏で取得を始める」がオフなら起動したときは始めない
///   （その設定は裏で BOOTH へ行く作業を止める物で、先回りもその1つ）。設定を入れたときは、その場で始める。</item>
/// <item>設定を切ったら、走っている検索を止める。閉じるときも止める。**設定を書いただけでは始めない**（切から入に変わったときだけ）。</item>
/// <item>取り込みが終わったら、走り終えていても始め直す（増えた未確定を引く）。この起動で走らせてよいときだけ。</item>
/// <item>取り込みが始まったら止め、終わったら続ける（画面の無い起動の取り込みも同じ係から知らせが来る）。</item>
/// </list>
/// 人の操作ではないので <c>UiCommand</c> を通さない（起動時の裏の作業と同じ例外）。通すと入口が「人が押した」優先度を掛ける。
/// </summary>
public sealed partial class StartupWork
{
    private readonly object _prefetchSync = new();
    private CandidatePrefetch? _prefetch;
    private CancellationTokenSource? _prefetchStop;
    private Task? _prefetchRun;

    /// <summary>起動したときの裏の取得（⑦まで）が済んだか。済むまでは、設定が入っていても先回りを始めない。</summary>
    private bool _startupFetchDone;

    /// <summary>
    /// この起動で先回りを走らせてよいか。⑦が終わったとき（裏の取得がオンの起動）か、設定を切から入に変えたときに立つ。
    /// **設定を書いただけでは立てない**：設定の保存は別の項目（取り込みの履歴など）でも起きるので、入のまま書くたびに始めると、
    /// 「起動したとき、裏で取得を始める」がオフの起動でも、何かを保存した時点で始まってしまう
    /// </summary>
    private volatile bool _prefetchArmed;

    /// <summary>直前に見た設定の値（切から入に変わったかを見る）。</summary>
    private bool _prefetchSetting;

    /// <summary>先回りが引く行（直前に組んだ一覧）。対象ごとの手掛かり（兄弟・置き方）を、人が押したときと同じに組むため</summary>
    private PrefetchPlan _plan = PrefetchPlan.Empty;

    /// <summary>
    /// 組んだ一覧から取り出した、引くときに要る物。
    /// </summary>
    /// <param name="Listed">一覧にあるファイルの場所（兄弟の zip の間で変わる語を外す。<see cref="ResolveViewModel.ListedPathsIn"/>）。</param>
    /// <param name="Folders">対象が展開物の根のフォルダか（置き方の段の数え方が変わる）。</param>
    /// <param name="Items">商品（置き方からショップのサブドメインを引く）。</param>
    internal sealed record PrefetchPlan(
        IReadOnlyList<string> Listed, IReadOnlySet<string> Folders, IReadOnlyList<ItemRecord> Items)
    {
        public static PrefetchPlan Empty { get; } = new([], new HashSet<string>(), []);
    }

    internal CandidatePrefetch Prefetch => _prefetch ??= new CandidatePrefetch(
        services.CandidateSearches,
        PlanPrefetchAsync,
        ProposeForPrefetchAsync,
        () => services.Settings.PrefetchCandidateSearch);

    /// <summary>走っている先回り（試験が終わるのを待つ）。</summary>
    internal Task? PrefetchRunning => _prefetchRun;

    private void ListenForPrefetch()
    {
        _prefetchSetting = services.Settings.PrefetchCandidateSearch;
        services.SettingsStore.Changed += settings =>
        {
            var turnedOn = settings.PrefetchCandidateSearch && !_prefetchSetting;
            var turnedOff = !settings.PrefetchCandidateSearch && _prefetchSetting;
            _prefetchSetting = settings.PrefetchCandidateSearch;
            if (turnedOn)
            {
                _prefetchArmed = true;
                StartPrefetch();
            }
            else if (turnedOff)
            {
                _prefetchArmed = false;
                StopPrefetch();
            }
        };

        services.Imports.Started += _ => Prefetch.Pause();
        services.Imports.Finished += () =>
        {
            Prefetch.Resume();

            // 走り終えた後の取り込みで増えた未確定も引く（走っている途中なら、続きが一覧を組み直して拾う）
            StartPrefetch();
        };
        if (services.Imports.Running is not null)
        {
            Prefetch.Pause();
        }
    }

    /// <summary>起動したときの裏の取得が済んだ（か、設定で切られていて走らない）。</summary>
    private void MarkStartupFetchDone(bool startPrefetch)
    {
        _startupFetchDone = true;
        if (startPrefetch)
        {
            _prefetchArmed = true;
            StartPrefetch();
        }
    }

    /// <summary>設定が入っていて、起動したときの取得が済み、この起動で走らせてよければ始める。走っていれば何もしない。</summary>
    private void StartPrefetch()
    {
        if (!services.Settings.PrefetchCandidateSearch || !_startupFetchDone || !_prefetchArmed)
        {
            return;
        }

        CancellationToken token;
        lock (_prefetchSync)
        {
            if (_prefetchRun is { IsCompleted: false })
            {
                return;
            }

            _prefetchStop = new CancellationTokenSource();
            token = _prefetchStop.Token;
        }

        var progress = new SyncProgress<(int Done, int Total)>(
            report => Progress?.Invoke("未確定の候補を検索中", report.Done, report.Total));

        var run = Task.Run(async () =>
        {
            try
            {
                var result = await Prefetch.RunAsync(progress, token);
                Core.Services.UiTrace.Write("先回り", $"未確定の自動検索の先回り：{result.End}・控えた {result.Searched} 件");
            }
            catch (OperationCanceledException)
            {
                // 設定を切った・閉じた
            }
            catch (Exception exception)
            {
                Core.Diagnostics.AppLog.Error("未確定の自動検索の先回り", exception);
            }
            finally
            {
                StageEnded?.Invoke();
                WorkEnded?.Invoke();
            }
        }, token);

        lock (_prefetchSync)
        {
            _prefetchRun = run;
        }

        run.Forget();
    }

    private void StopPrefetch()
    {
        lock (_prefetchSync)
        {
            _prefetchStop?.Cancel();
        }
    }

    /// <summary>未確定の記録から、画面と同じ行を組み、対象ごとに1件にする。行を組むのはディスクを見るので裏で。</summary>
    private async Task<IReadOnlyList<PrefetchTarget>> PlanPrefetchAsync(CancellationToken token)
    {
        var unresolved = await services.Store.Unresolved.LoadAsync(token);
        if (unresolved.Count == 0)
        {
            _plan = PrefetchPlan.Empty;
            return [];
        }

        var items = (await services.Store.Items.LoadAllAsync(cancellationToken: token)).Items;
        var owned = items
            .SelectMany(item => item.Local.OwnedFiles)
            .SelectMany(file => file.Paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importFolders = services.Settings.ImportFolders.ToList();
        var rows = await Task.Run(() => ResolveViewModel.BuildRows(unresolved, owned, importFolders).Rows, token);

        var (targets, folders) = PrefetchTargets(rows);
        _plan = new PrefetchPlan(ResolveViewModel.ListedPathsIn(rows), folders, items);
        return targets;
    }

    /// <summary>
    /// 行を対象ごとにまとめる。対象と持ち主は画面と同じ決め方（<see cref="ResolveViewModel.SearchTargetFor"/>・
    /// <see cref="ResolveViewModel.SearchOwnersIn"/>）。持ち主は束（展開した中身）の鍵ごとに先にまとめる——行ごとに一覧を舐めると、
    /// 未確定が数万件あるときに件数の2乗になる。
    /// </summary>
    internal static (List<PrefetchTarget> Targets, HashSet<string> Folders) PrefetchTargets(IReadOnlyList<UnresolvedRow> rows)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(row => row.IsExpandedContent || row.IsArchiveContent))
        {
            if (!groups.TryGetValue(row.GroupKey, out var hashes))
            {
                hashes = [];
                groups[row.GroupKey] = hashes;
            }

            hashes.Add(row.File.Hash);
        }

        var byTarget = new Dictionary<string, (HashSet<string> Files, DateTimeOffset Newest)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (ResolveViewModel.SearchTargetFor(row) is not { } target)
            {
                continue;
            }

            if (ResolveViewModel.SearchTargetIsFolderFor(row))
            {
                folders.Add(target);
            }

            IEnumerable<string> owners = row.IsExpandedContent || row.IsArchiveContent ? groups[row.GroupKey] : [row.File.Hash];
            if (!byTarget.TryGetValue(target, out var entry))
            {
                entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), row.File.FirstSeenAt);
                order.Add(target);
            }

            entry.Files.UnionWith(owners);
            byTarget[target] = (entry.Files, row.File.FirstSeenAt > entry.Newest ? row.File.FirstSeenAt : entry.Newest);
        }

        var targets = order
            .Select(target => new PrefetchTarget(target, byTarget[target].Files.ToList(), byTarget[target].Newest))
            .ToList();
        return (targets, folders);
    }

    /// <summary>人が「自動検索」を押したときと同じ手掛かりで引く（兄弟の zip・フォルダの置き方）。</summary>
    private async Task<ResolutionProposal> ProposeForPrefetchAsync(PrefetchTarget target, CancellationToken token)
    {
        var plan = _plan;
        var layout = await ResolveViewModel.LayoutHintForAsync(
            target.Target,
            plan.Folders.Contains(target.Target),
            services.Settings.FolderLayouts,
            plan.Items,
            () => Task.FromResult(plan.Items));
        return await services.Resolver.ProposeAsync(target.Target, token, progress: null, plan.Listed, layout);
    }
}
