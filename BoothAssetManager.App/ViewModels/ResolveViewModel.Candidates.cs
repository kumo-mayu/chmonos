using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定画面：候補を探す・出す（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
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
        ? "BOOTHに問い合わせできませんでした。通信を確かめて、少し待ってからもう一度「候補を検索」を押してください。商品IDが分かっていれば、「商品IDを決める」に直接入れられます。"
        : "候補がありません。上のボタンで検索するか、「商品IDを決める」に商品IDを直接入れてください。";

    private async Task ProposeAsync()
    {
        if (SearchTargetPath is not { } searchTarget)
        {
            return;
        }

        IsBusy = true;
        StatusText = string.Empty;
        SearchCurrent = 0;
        SearchTotal = 0;
        SearchPhase = "準備しています";

        // 1件ずつ間隔を空けて取りに行くので十数秒かかることがある。
        // 何をどこまでやっているかを出さないと、止まったように見える。
        var progress = new Progress<ResolveProgress>(report => RunOnUiThread(() =>
        {
            SearchPhase = report.Phase;
            SearchCurrent = report.Current;
            SearchTotal = report.Total;
        }));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ProposeCandidates(searchTarget, progress));

            if (result is CommandResult.CandidatesProposed proposed)
            {
                foreach (var candidate in proposed.Candidates)
                {
                    Candidates.Add(ToRow(candidate));
                }

                SearchPhase = string.Empty;
                HasSearched = true;
                BoothUnreachable = proposed.BoothUnreachable;
                // 0件のときは候補の欄の「候補がありません…」が同じことを言うので、状態の1行には出さない（ユーザ指示 2026-09-17）
                StatusText = proposed.Candidates.Count == 0
                    ? string.Empty
                    : $"候補を {proposed.Candidates.Count} 件見つけました。";
            }
            else if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
            }
        }
        finally
        {
            IsBusy = false;
            IsSearching = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    // --- 自動検索の進み具合 ---

    private string _searchPhase = string.Empty;
    private int _searchCurrent;
    private int _searchTotal;
    private bool _isSearching;

    /// <summary>今どの段階かの文言。</summary>
    public string SearchPhase
    {
        get => _searchPhase;
        private set
        {
            if (SetField(ref _searchPhase, value))
            {
                IsSearching = value.Length > 0;
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    public int SearchCurrent
    {
        get => _searchCurrent;
        private set
        {
            if (SetField(ref _searchCurrent, value))
            {
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    /// <summary>0なら件数の分からない段階。バーは伸び縮みだけさせる。</summary>
    public int SearchTotal
    {
        get => _searchTotal;
        private set
        {
            if (SetField(ref _searchTotal, value))
            {
                OnPropertyChanged(nameof(HasSearchTotal));
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    public bool HasSearchTotal => SearchTotal > 0;

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetField(ref _isSearching, value);
    }

    public string SearchProgressText => SearchTotal > 0
        ? $"{SearchPhase}　{SearchCurrent + 1} / {SearchTotal}"
        : SearchPhase;

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
        row.OpenBoothCommand = new RelayCommand(() => OpenInBrowser(Core.Booth.BoothClient.ItemPageUrl(row.ItemId)));
        return row;
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくても作業は続けられる
        }
    }

    /// <summary>
    /// 「商品IDを決める」の欄を見せてほしいとき（候補の「これで確認」・商品ページを落としたとき）。
    /// 候補は決める欄より下にあるので、押した結果が画面の外に出て、何も起きなかったように見える（ユーザ判断 2026-09-17）。
    /// </summary>
    public event Action? DecisionFocusRequested;

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
