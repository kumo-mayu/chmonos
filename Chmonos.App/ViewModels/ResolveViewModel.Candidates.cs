using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>未確定画面：候補を探す・出す（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
    /// <summary>
    /// 同じ場所に前にあったファイルを持っていた商品を、候補の先頭に出す（ユーザ判断 2026-09-30「2A」）。**通信は増えない。**
    ///
    /// 手で商品に結んだファイルを同じ名前で上書きすると（壊れた zip を落とし直した・更新版を上書きした）、新しい中身は
    /// 手掛かりが無いので未確定に出る。どの商品の物だったかは取り込みが記録に残している（<see cref="UnresolvedFile.SamePathItemIds"/>）。
    /// **自動では結ばない**——同じ名前の別の商品を置いただけかもしれないので、人が選んだときだけ登録する。
    ///
    /// 先頭に置くのは、ほかの候補（名前の一致・検索）より確かな手掛かりだから。理由は行に書く（なぜ候補なのかを隠さない）。
    /// </summary>
    private void AddSamePathCandidates()
    {
        if (Selected is not { } selected)
        {
            return;
        }

        var what = selected.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "ファイル";
        foreach (var id in selected.File.SamePathItemIds.Distinct(StringComparer.Ordinal))
        {
            var known = _itemNames.TryGetValue(id, out var name);

            // 手元から消した「BOOTHに無い商品」は出さない。仮のIDはBOOTHに無いので、選んでも登録する先が無い。
            // BOOTHの商品なら、消した後でも選べば取り直して登録できるので、IDで出す
            if (!known && LocalItemId.IsLocal(id))
            {
                continue;
            }

            Candidates.Add(WithBooth(new CandidateRow
            {
                ItemId = id,
                Title = known ? name! : $"商品ID {id}",
                Detail = $"前に同じ場所にあった{what}を、この商品に登録していました",
                Source = "手元の商品の記録",
            }));
        }
    }

    /// <summary>
    /// 手元のアバター登録簿から候補を足す。**通信は増えない。**
    ///
    /// 登録簿は未所持の商品の名前まで持っているので、BOOTHが404を返すファイルでも
    /// 名前から辿り着けることがある。別名（「くうた対応」など）は
    /// **まさにファイル名に現れる形**で溜まっている。
    ///
    /// **欄は分けない。**（Q7）候補が2箇所に出ると、ユーザは
    /// 「どちらを先に見るべきか」を判断させられる。押した先で真実を出せばよい——
    /// 「BOOTHでは見つかりません。手元の記録では『くうた』です」。
    /// </summary>
    private void AddRegistryCandidates()
    {
        if (Selected?.File.Paths.FirstOrDefault() is not { } path)
        {
            return;
        }

        var registry = _services.Store.Avatars.Load().Entries;
        var already = Candidates.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in Core.Resolution.RegistryCandidates.For(
            path, registry, _services.Bridge, _services.KanjiReadings))
        {
            // 名前は「BOOTHに無い商品として登録する」側の候補にも回す。
            // 当たった項目が非公開なら、その名前こそが手元に残っている唯一の名前
            if (!LocalNameSuggestions.Contains(candidate.Name))
            {
                LocalNameSuggestions.Add(candidate.Name);
            }

            if (!already.Add(candidate.ItemId))
            {
                continue;
            }

            var detail = $"「{candidate.MatchedOn}」で一致";
            if (candidate.NeverFetched)
            {
                detail += "　BOOTHからは情報を取れていません";
            }

            Candidates.Add(WithBooth(new CandidateRow
            {
                ItemId = candidate.ItemId,
                Title = candidate.Name,
                Detail = detail,
                Source = "手元のアバター登録簿",
            }));
        }
    }

    /// <summary>ファイル名からBOOTH内を検索して候補を出す。通信するので明示的に押させる。</summary>
    /// <summary>
    /// 検索の手掛かりにするパス。
    ///
    /// 展開物の中身は、ファイル名（cloth.psd など）で引いても商品には辿り着かない。
    /// 元zipが分かればその名前で、分からなければ展開元とみなしたフォルダの名前で引く。
    /// zip名は配布者が付けた名前そのもので、フォルダ名は展開した人が変えていることがある。
    /// zipが今もその場所にあれば、中の unitypackage も手掛かりとして読まれる。
    /// バナーからも候補カードからも同じ対象になるようにここへ集約する。
    /// </summary>
    public string? SearchTargetPath => Selected is null || Selected.File.Paths.Count == 0
        ? null
        : Selected.Origin is { } origin
            ? origin.ArchivePath
            : Selected.IsArchiveContent && RegisterTargetFolder is { } folder
                ? folder
                : Selected.File.Paths[0];

    public string SearchTargetText => SearchTargetPath is null
        ? string.Empty
        : SelectedOriginText is not null
            ? $"元のzipの名前「{Selected!.Origin!.ArchiveName}」で探します"
        : Selected?.IsArchiveContent == true
            ? $"フォルダ名「{Path.GetFileName(SearchTargetPath)}」で探します"
            : $"ファイル名「{Path.GetFileName(SearchTargetPath)}」で探します";

    private bool _hasSearched;

    /// <summary>選んだファイルで自動検索をしたか。するまでは「候補がありません」を出さない（ユーザ指示 2026-09-17：探す前から無いと言っていた）。</summary>
    public bool HasSearched
    {
        get => _hasSearched;
        private set => SetField(ref _hasSearched, value);
    }

    private bool _boothUnreachable;

    /// <summary>
    /// 直前の検索が BOOTH に届かなかったか（E3）。**0件と言い分ける**——
    /// 届かなかったのに「候補がありません。商品IDを直接入れてください」と出すと、
    /// 待ってもう一度押せば出るものを、手で入れさせることになる。
    /// </summary>
    public bool BoothUnreachable
    {
        get => _boothUnreachable;
        private set
        {
            if (SetField(ref _boothUnreachable, value))
            {
                OnPropertyChanged(nameof(CandidatesEmptyText));
            }
        }
    }

    /// <summary>候補が0件のときに出す文。届かなかったときは次の一手が違う。</summary>
    public string CandidatesEmptyText => BoothUnreachable
        ? "BOOTHに問い合わせできませんでした。通信を確かめて、少し待ってからもう一度「自動検索」を押してください。商品IDが分かっていれば、「商品IDを決める」に直接入れられます。"
        : "候補がありません。上の「自動検索」を押すか、「商品IDを決める」に商品IDを直接入れてください。";

    private async Task ProposeAsync()
    {
        if (SearchTargetPath is not { } searchTarget || Selected is not { } startedWith)
        {
            return;
        }

        // **どの画面からでも止められるようにする**（ユーザ判断 2026-09-21・C2）。
        // BOOTH内検索＋候補3件のJSON＋（当たらなければ）別語での引き直しで分単位かかるのに、
        // 押した後まったく止められなかった（配管は通っていて、入口だけ抜けていた）
        using var stop = new CancellationTokenSource();
        var job = _main.BeginLongJob("候補を検索しています", "この間、BOOTHへの他の問い合わせは順番待ちになります", stop,
            "候補の検索をやめます。もう一度押すとやり直せます。");
        if (job is null)
        {
            return;
        }

        CandidatesFocusRequested?.Invoke();

        // 検索は分単位かかり、その間も左の一覧は選び直せる。結果は始めた対象の分として覚え、
        // 今も同じ対象を選んでいるときだけ並べる（前は今のファイルに前のファイルの候補が並び、
        // それを止めた後は結果を捨てていたので、戻っても検索し直すしかなかった・ユーザ判断 2026-09-28）。
        // 行ではなく対象で比べる：一覧を読み直すと行は作り直されるが、同じファイルなら対象は同じ
        bool StillShowing() => string.Equals(SearchTargetPath, searchTarget, StringComparison.OrdinalIgnoreCase);

        // 検索は画面全体を止めない（登録・除外は押せる。メモ60）。進みは主画面に置き、検索している対象を選んだときだけ出す
        var search = _main.ResolveSearch;
        StatusText = string.Empty;
        _proposing = true;
        search.Begin(searchTarget, SearchOwners(startedWith));

        // 1件ずつ間隔を空けて取りに行くので十数秒かかることがある。
        // 何をどこまでやっているかを出さないと、止まったように見える。
        var progress = new Progress<ResolveProgress>(report => RunOnUiThread(() =>
        {
            if (!search.IsSearching(searchTarget))
            {
                return;
            }

            search.Report(report.Phase, report.Current, report.Total);
            _main.ReportLongJob($"候補を検索中　{report.Phase}　{report.Current} / {report.Total}", report.Current, report.Total);
        }));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ProposeCandidates(searchTarget, progress, ListedPaths()), cancellationToken: stop.Token);

            if (result is CommandResult.CandidatesProposed proposed)
            {
                RememberedSearches.Remember(searchTarget, proposed, SearchOwners(startedWith));

                if (StillShowing())
                {
                    ShowSearchResult(proposed);
                }
            }
            else if (result is CommandResult.Failed failed && StillShowing())
            {
                StatusText = failed.Message;
            }
        }
        catch (OperationCanceledException)
        {
            // 中止。もう一度押せばやり直せる（何も書いていない）。選び直した後なら、今のファイルの話ではないので出さない。
            // 前に覚えた結果があれば、それは残しておく（中止したのは探し直しの方）
            StatusText = StillShowing()
                ? "候補の検索を中止しました。もう一度「候補を検索」を押すとやり直せます。"
                : StatusText;
        }
        finally
        {
            job.Dispose();
            search.End();
            _proposing = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    /// <summary>
    /// 一覧にあるファイルの場所（検索で隠れている行も含む）。自動検索が同じフォルダの兄弟の zip と見比べて、
    /// アバターごとに分けた zip の間で変わる語（アバター名）を検索語から外すのに使う（<see cref="SiblingTokens"/>）。
    /// 展開した中身の元のzipも入れる（zip を消していても、名前は兄弟の手掛かりになる）。
    /// </summary>
    private List<string> ListedPaths()
        => Files
            .SelectMany(row => row.Origin is { } origin ? row.File.Paths.Append(origin.ArchivePath) : row.File.Paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // --- 自動検索の進み具合（主画面の ResolveSearchState を、選んだ行が検索の対象のときだけ映す。メモ60） ---

    /// <summary>選んだ行が、今走っている検索の対象か。違う行を選んでいる間は進みを出さない（行ごとの進み）。</summary>
    public bool IsSearching => _main.ResolveSearch.IsSearching(SearchTargetPath);

    /// <summary>今どの段階かの文言。</summary>
    public string SearchPhase => IsSearching ? _main.ResolveSearch.Phase : string.Empty;

    public int SearchCurrent => IsSearching ? _main.ResolveSearch.Current : 0;

    /// <summary>0なら件数の分からない段階。バーは伸び縮みだけさせる。</summary>
    public int SearchTotal => IsSearching ? _main.ResolveSearch.Total : 0;

    public bool HasSearchTotal => SearchTotal > 0;

    public string SearchProgressText => SearchTotal > 0
        ? $"{SearchPhase}　{SearchCurrent + 1} / {SearchTotal}"
        : SearchPhase;

    private static readonly string[] SearchProperties =
    [
        nameof(IsSearching), nameof(SearchPhase), nameof(SearchCurrent), nameof(SearchTotal), nameof(HasSearchTotal),
        nameof(SearchProgressText), nameof(ProposeHint),
    ];

    private void RaiseSearchProperties()
    {
        foreach (var name in SearchProperties)
        {
            OnPropertyChanged(name);
        }
    }

    private void OnSearchStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        RaiseSearchProperties();

        // 行の札「検索中」は、始まったとき・終わったときだけ変わる（進むたびに全部の行を回さない）
        if (_searchRowsShown != _main.ResolveSearch.IsRunning)
        {
            _searchRowsShown = _main.ResolveSearch.IsRunning;
            RefreshQueueState();
        }
    }

    private bool _searchRowsShown;

    /// <summary>この画面が始めた検索が走っている間。</summary>
    private bool _proposing;

    /// <summary>
    /// 検索が終わった。この画面が始めた検索でなくても（離れて戻った・フォルダビューの右から始めた）、
    /// 同じ対象を選んでいれば覚えた結果を出す（始めた画面はもう無いことがある）。
    /// </summary>
    private void OnSearchEnded(string target)
    {
        // 自分で始めた検索は、結果・中止・失敗の文を自分で出している（ここで出し直すと中止の文が消える）
        if (_proposing)
        {
            return;
        }

        if (string.Equals(SearchTargetPath, target, StringComparison.OrdinalIgnoreCase)
            && RememberedSearches.Find(target) is { } remembered)
        {
            ShowSearchResult(remembered);
        }
    }

    private static CandidateRow ToRow(ResolutionCandidate candidate) => WithBooth(new CandidateRow
    {
        ItemId = candidate.ItemId,
        Title = candidate.Name ?? $"商品ID {candidate.ItemId}",
        Detail = string.Join("　", new[] { candidate.ShopName, string.Join(" / ", candidate.Reasons) }
            .Where(part => !string.IsNullOrEmpty(part))),
        Source = candidate.IsStrong ? $"検索・確度が高い（{candidate.Score}）" : $"検索（{candidate.Score}）",
        IsStrong = candidate.IsStrong,
    });

    /// <summary>候補にBOOTHを開くコマンドを付ける。候補を出す以上、確かめる手段が要る。</summary>
    private static CandidateRow WithBooth(CandidateRow row)
    {
        if (!row.HasBoothPage)
        {
            return row;
        }

        // 開けなくても作業は続けられる（失敗は Shell が握る）
        row.OpenBoothCommand = new RelayCommand(() => Services.Shell.OpenUrl(Core.Booth.BoothLinks.ItemPage(row.ItemId)));
        return row;
    }

    /// <summary>
    /// 「商品IDを決める」の欄を見せてほしいとき（候補の「これで確認」・商品ページを落としたとき）。
    /// 候補は決める欄より下にあるので、押した結果が画面の外に出て、何も起きなかったように見える（ユーザ判断 2026-09-17）。
    /// </summary>
    public event Action? DecisionFocusRequested;

    /// <summary>
    /// 「候補」の欄を見せてほしいとき（自動検索を押したとき）。ボタンは上の「分かっていること」にあり、
    /// 進み具合と結果は決める欄より下の候補の欄に出るので、送らないと押しても何も起きないように見える（ユーザ指示 2026-09-29）。
    /// </summary>
    public event Action? CandidatesFocusRequested;

    private async Task UseCandidateAsync(object? parameter)
    {
        if (parameter is CandidateRow candidate)
        {
            ItemIdInput = candidate.ItemId;
            DecisionFocusRequested?.Invoke();
            await PreviewAsync(candidate.ItemId);
        }
    }

    /// <summary>
    /// 画面に落とされた BOOTH の商品ページを、選んでいるファイルの商品IDとして入れて確かめる。
    /// ファイルを選んでいないときは入れる先が無いので false（呼んだ側が普段の扱いに回す）。
    /// </summary>
    public bool AcceptDroppedItemId(string itemId)
    {
        if (!HasSelection)
        {
            return false;
        }

        ItemIdInput = itemId;
        DecisionFocusRequested?.Invoke();
        PreviewAsync(itemId).Forget();
        return true;
    }
}
