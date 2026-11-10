using Chmonos.Core.Commands;

namespace Chmonos.App.ViewModels;

/// <summary>未確定画面：自動検索の結果を、探した対象ごとに覚えておく</summary>
public sealed partial class ResolveViewModel
{
    /// <summary>
    /// 自動検索の結果を、アプリを閉じるまで覚えておく（ユーザ判断 2026-09-28）。
    /// 前は選び直すたびに候補を空にしていたので、別のファイルを見てから戻ると、分単位かかった検索をやり直すしかなかった。
    ///
    /// **この画面は開くたびに作り直す**（フォルダビューの右側にも別に作られる）ので、画面の中ではなく
    /// 静的に持つ。アプリを閉じるまで、という決めにそのまま合う。触るのは画面のスレッドだけ（検索の続きも await で戻ってくる）。
    /// </summary>
    private static readonly SearchMemory RememberedSearches = new();

    /// <summary>
    /// 覚えた検索の結果。**鍵は検索に使った対象**（<see cref="SearchTargetPath"/>：元zip・展開物の根のフォルダ・ファイル）。
    ///
    /// 選ぶ単位（ファイル・束・「このファイルだけを扱う」）で鍵を分けないのは、検索が引くのは対象の名前だけで、
    /// 束のどの行から押しても、「このファイルだけを扱う」を入れても、同じ元zipの名前で引くから。
    /// 単位で分けると、同じ問い合わせの結果が行によって出たり出なかったりする。
    ///
    /// 検索の結果には、その単位のファイル（持ち主）を添えておき、確定・除外・登録で持ち主が全部一覧から消えたら忘れる。
    /// </summary>
    private sealed class SearchMemory
    {
        /// <summary>
        /// 覚える数の上限。持ち主が消えたら忘れるので普段は届かないが、別の画面で片付いた物は
        /// 絞った一覧（フォルダビュー）からは見えず忘れられない。そのぶんが開いている間に積もり続けないための保険。
        /// 1回の検索は数十秒から分単位かかるので、1回の起動で100件を超えて探すことはまず無い
        /// </summary>
        private const int Capacity = 100;

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private long _sequence;

        private sealed record Entry(CommandResult.CandidatesProposed Result, HashSet<string> Owners, long Sequence);

        public CommandResult.CandidatesProposed? Find(string target)
            => _entries.TryGetValue(target, out var entry) ? entry.Result : null;

        /// <summary>覚える。持ち主が1件も無い（検索の間に片付いた）なら、もう選ばれることはないので覚えない。</summary>
        public void Remember(string target, CommandResult.CandidatesProposed result, IEnumerable<string> owners)
        {
            var ownerSet = owners.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ownerSet.Count == 0)
            {
                _entries.Remove(target);
                return;
            }

            _entries[target] = new Entry(result, ownerSet, ++_sequence);

            if (_entries.Count > Capacity)
            {
                // いちばん前に探した物から忘れる。戻って見る見込みがいちばん薄い
                var oldest = _entries.MinBy(pair => pair.Value.Sequence).Key;
                _entries.Remove(oldest);
            }
        }

        /// <summary>一覧から消えたファイルを持ち主から外し、持ち主の居なくなった結果を忘れる。</summary>
        public void Forget(IEnumerable<string> fileHashes)
        {
            var gone = fileHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (gone.Count == 0 || _entries.Count == 0)
            {
                return;
            }

            foreach (var (target, entry) in _entries.ToList())
            {
                entry.Owners.ExceptWith(gone);
                if (entry.Owners.Count == 0)
                {
                    _entries.Remove(target);
                }
            }
        }

        /// <summary>未確定の全件を読み直したとき、もう未確定に無いファイルを持ち主から外す（画面を閉じている間に片付いた分）。</summary>
        public void KeepOnly(IReadOnlySet<string> remaining)
        {
            foreach (var (target, entry) in _entries.ToList())
            {
                entry.Owners.IntersectWith(remaining);
                if (entry.Owners.Count == 0)
                {
                    _entries.Remove(target);
                }
            }
        }
    }

    /// <summary>
    /// 片付いたファイルの検索の結果を忘れる。開いている間の覚えと、保存先の控え（ユーザ判断 2026-11-11：決まったら控えも消す）の両方から。
    /// 控えは書き込みなので命令を通す。待たない（一覧の差し替えを止めない）
    /// </summary>
    private void ForgetSearches(IEnumerable<string> fileHashes)
    {
        var hashes = fileHashes.ToList();
        RememberedSearches.Forget(hashes);
        if (hashes.Count > 0)
        {
            _services.Commands.ExecuteAsync(new UiCommand.ForgetCandidateSearches(hashes)).Forget();
        }
    }

    /// <summary>一覧を全件読み直した。未確定に残っていないファイルを、覚えと控えの持ち主から外す。</summary>
    private void KeepSearchesOf(IReadOnlySet<string> remaining)
    {
        RememberedSearches.KeepOnly(remaining);
        _services.Commands.ExecuteAsync(new UiCommand.ForgetCandidateSearches([], remaining)).Forget();
    }

    /// <summary>今の候補の欄に並べている、自動検索の分の行。検索し直したとき・結果が届いたときに、この分だけ入れ替える。</summary>
    private readonly List<CandidateRow> _searchRows = [];

    /// <summary>
    /// 検索した対象を持つファイル（持ち主）。展開した中身と展開物のフォルダは、束の全件が同じ対象で引くので束の全件。
    /// それ以外は1件ごとに対象が違う（同じフォルダの別のファイルはファイル名で引く）ので、その1件だけ。
    /// 今の一覧に残っている物だけを数える——検索の間に片付いた物を持ち主にしない
    /// </summary>
    private List<string> SearchOwners(UnresolvedRow row) => SearchOwnersIn(row, Files);

    /// <summary><see cref="SearchOwners"/> の決め方。先回り（<see cref="Services.StartupWork"/>）も同じ持ち主で控える。</summary>
    internal static List<string> SearchOwnersIn(UnresolvedRow row, IEnumerable<UnresolvedRow> rows)
        => row.IsExpandedContent || row.IsArchiveContent
            ? rows.Where(other => (other.IsExpandedContent || other.IsArchiveContent)
                    && string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.File.Hash)
                .ToList()
            : rows.Where(other => string.Equals(other.File.Hash, row.File.Hash, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.File.Hash)
                .ToList();

    /// <summary>
    /// 選んだ物に覚えた検索の結果があれば出す。無ければ控え（先回り・前の起動の再検索。<see cref="Core.Resolution.CandidateSearchCache"/>）を、
    /// それも無ければ「まだ探していない」に戻す。問い合わせはしない。
    /// 開いている間に覚えた結果を先に見る：控えより後に押して引いた物なので新しい
    /// </summary>
    private void ShowRememberedSearch()
    {
        _searchRows.Clear();
        OnPropertyChanged(nameof(HasCachedSearch));
        OnPropertyChanged(nameof(ProposeButtonText));

        if (SearchTargetPath is { } target)
        {
            if (RememberedSearches.Find(target) is { } remembered)
            {
                ShowSearchResult(remembered);
                return;
            }

            if (_services.CandidateSearches.Find(target) is { } cached)
            {
                ShowCachedSearch(cached);
                return;
            }
        }

        HasSearched = false;
        BoothUnreachable = false;
    }

    /// <summary>控えを候補の欄に出す。**待たずに出す**（ユーザ判断 2026-11-11）。古くなっても引き直さず、押したときだけ引き直す。</summary>
    private void ShowCachedSearch(Core.Resolution.CandidateSearchEntry cached)
    {
        ShowSearchResult(new CommandResult.CandidatesProposed(cached.Candidates));
        StatusText = cached.Candidates.Count == 0
            ? string.Empty
            : $"{cached.SearchedAt.ToLocalTime():M月d日}に検索した候補を {cached.Candidates.Count} 件表示しています。";
        OnPropertyChanged(nameof(HasCachedSearch));
        OnPropertyChanged(nameof(ProposeButtonText));
        OnPropertyChanged(nameof(CandidatesEmptyText));
        OnPropertyChanged(nameof(ProposeHint));
    }

    /// <summary>
    /// 選んだ物に控えがあるか。あれば「自動検索」を「再検索」にする（ユーザ判断 2026-11-11）。
    /// 開いている間に押して引いた結果だけのときは「自動検索」のまま（控えに書かないので、次に開いたときは無い）
    /// </summary>
    public bool HasCachedSearch => SearchTargetPath is { } target && _services.CandidateSearches.Find(target) is not null;

    /// <summary>候補を探すボタンの名前。控えがあれば押すと引き直して置き換えるので「再検索」。</summary>
    public string ProposeButtonText => HasCachedSearch ? "再検索" : "自動検索";

    /// <summary>
    /// 先回りが控えを書いた（裏のスレッドから来る）。選んでいる対象の分で、まだ何も出していなければ、待たずに出す。
    /// 開いている間に押して引いた結果を出しているときは、そちらの方が新しいので差し替えない
    /// </summary>
    private void OnCandidateSearchRemembered(string target) => RunOnUiThread(() =>
    {
        if (!string.Equals(SearchTargetPath, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsSearching && RememberedSearches.Find(target) is null && _services.CandidateSearches.Find(target) is { } cached)
        {
            ShowCachedSearch(cached);
            return;
        }

        OnPropertyChanged(nameof(HasCachedSearch));
        OnPropertyChanged(nameof(ProposeButtonText));
    });

    /// <summary>検索の結果を候補の欄と状態の1行に出す。前に出していた検索の分は入れ替える（押し直すと重なっていた）。</summary>
    private void ShowSearchResult(CommandResult.CandidatesProposed proposed)
    {
        foreach (var row in _searchRows)
        {
            Candidates.Remove(row);
        }

        _searchRows.Clear();

        foreach (var candidate in proposed.Candidates)
        {
            var row = ToRow(candidate);
            Candidates.Add(row);
            _searchRows.Add(row);
        }

        HasSearched = true;
        BoothUnreachable = proposed.BoothUnreachable;
        // 0件のときは候補の欄の「候補がありません…」が同じことを言うので、状態の1行には出さない（ユーザ指示 2026-09-17）
        StatusText = proposed.Candidates.Count == 0
            ? string.Empty
            : $"候補を {proposed.Candidates.Count} 件見つけました。";

        // 再検索で控えが置き換わった・控えの無い対象を引いた。ボタンの名前と空の文を今の控えに合わせる
        OnPropertyChanged(nameof(HasCachedSearch));
        OnPropertyChanged(nameof(ProposeButtonText));
        OnPropertyChanged(nameof(CandidatesEmptyText));
        OnPropertyChanged(nameof(ProposeHint));
    }
}
