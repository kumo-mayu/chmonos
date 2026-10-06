using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 自動検索の手掛かり。画面が手元の記録から読んで渡す（読むだけなので命令を通さない）。
/// </summary>
/// <param name="PreviousName">前の商品名。自分で付けた名前か、BOOTHの名前（どちらも無ければ null。IDだけの見出しで引かない）。</param>
/// <param name="ShopSubdomain">今の商品のショップ。同じショップの札に使う。</param>
/// <param name="Paths">手元のファイルとフォルダの場所。</param>
public sealed record ReplacementClues(string? PreviousName, string? ShopSubdomain, IReadOnlyList<string> Paths)
{
    public static ReplacementClues From(ItemRecord item) => new(
        item.Local.DisplayName is { Length: > 0 } own ? own : item.Booth.Name is { Length: > 0 } booth ? booth : null,
        item.ShopSubdomain,
        item.Local.AttachedFiles.Select(file => file.Paths.FirstOrDefault() ?? string.Empty)
            .Concat(item.Local.LocalFolders.Select(folder => folder.Path))
            .Where(path => path.Length > 0)
            .ToList());

    public bool IsEmpty => PreviousName is null && Paths.Count == 0;
}

/// <summary>自動検索の候補1件（窓の行）。</summary>
public sealed class ReplacementRow
{
    public required ReplacementCandidate Candidate { get; init; }

    public string ItemId => Candidate.ItemId;

    public string Title => Candidate.Name is { Length: > 0 } name ? name : $"商品ID {Candidate.ItemId}";

    public string ShopText => Candidate.ShopName ?? Candidate.ShopSubdomain ?? string.Empty;

    public bool HasShop => ShopText.Length > 0;

    public bool IsSameShop => Candidate.IsSameShop;

    public string FoundByText => $"{Candidate.FoundBy}で見つかりました";

    // 開けなくても選ぶことはできる（失敗は Shell が握る）
    public RelayCommand OpenBoothCommand => _openBooth ??= new RelayCommand(
        () => Services.Shell.OpenUrl(Core.Booth.BoothLinks.ItemPage(ItemId)));

    private RelayCommand? _openBooth;

    private System.Windows.Media.ImageSource? _image;
    private bool _triedImage;

    /// <summary>
    /// 枠（56DIP）の2倍で読む。原寸で読むと大きな画像1枚で数十MBを持つ（docs/dev/wpf.md「絵のメモリ」）。
    /// 読めなければ null で、枠は空のまま
    /// </summary>
    public System.Windows.Media.ImageSource? Image
    {
        get
        {
            if (!_triedImage)
            {
                _triedImage = true;
                if (Candidate.Image is { Length: > 0 } bytes)
                {
                    try
                    {
                        var image = new System.Windows.Media.Imaging.BitmapImage();
                        image.BeginInit();
                        image.StreamSource = new MemoryStream(bytes);
                        image.DecodePixelWidth = 112;
                        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                        image.EndInit();
                        image.Freeze();
                        _image = image;
                    }
                    catch (Exception exception) when (exception is IOException or NotSupportedException
                        or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
                    {
                        _image = null;
                    }
                }
            }

            return _image;
        }
    }
}

/// <summary>
/// 「IDを変える」の自動検索（ユーザ判断 2026-10-06）。作者が消して出し直した・ショップごと移った・名前を変えた商品の、新しいIDを探す。
/// **押したときだけ BOOTH へ問い合わせる**（命令を通すので、人が押した通信の優先度になる）。
/// **候補を選んでも移さない**——IDの欄に入れて下見を出すだけで、移すのは「この内容で移す」。
/// </summary>
public sealed partial class ChangeItemIdDialogViewModel
{
    private ReplacementClues? _clues;
    private CancellationTokenSource? _searchStop;
    private bool _hasSearched;
    private bool _boothUnreachable;
    private string _searchPhase = string.Empty;
    private int _searchCurrent;
    private int _searchTotal;

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<ChangeItemIdDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓を出す。「この内容で移す」なら true。</summary>
    public bool Ask()
        => Intercept is { } intercept
            ? intercept(this)
            : new Views.ChangeItemIdDialog(this).ShowDialog() == true;

    private RelayCommand? _searchCommand;
    private RelayCommand? _stopSearchCommand;
    private RelayCommand? _useCandidateCommand;

    public RelayCommand SearchCommand => _searchCommand ??= new RelayCommand(
        () => SearchAsync().Forget(),
        () => CanSearch && !IsSearching);

    public RelayCommand StopSearchCommand => _stopSearchCommand ??= new RelayCommand(
        () => _searchStop?.Cancel(),
        () => IsSearching);

    /// <summary>候補を選ぶ。IDの欄に入れて下見を取る（「調べる」と同じ。**移さない**）。</summary>
    public RelayCommand UseCandidateCommand => _useCandidateCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is ReplacementRow row)
            {
                IdInput = row.ItemId;
                CheckCommand.Execute(null);            }
        },
        parameter => parameter is ReplacementRow);

    /// <summary>手掛かり（名前かファイル）が1つでもあるか。無ければ自動検索は押せない。</summary>
    public bool CanSearch => _clues is { IsEmpty: false };

    public string SearchHint => CanSearch
        ? "前の商品名と手元のファイル名でBOOTHを検索します。"
        : "商品名も手元のファイルも無いので、自動検索はできません。";

    public ObservableCollection<ReplacementRow> Candidates { get; } = [];

    public bool IsSearching => _searchStop is not null;

    /// <summary>探し終えたか。探す前から「見つかりません」と言わない。</summary>
    public bool HasSearched
    {
        get => _hasSearched;
        private set
        {
            if (SetField(ref _hasSearched, value))
            {
                OnPropertyChanged(nameof(ShowsNoCandidates));
            }
        }
    }

    public bool ShowsNoCandidates => HasSearched && !IsSearching && Candidates.Count == 0;

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>0件のときの文。次にやることを言う。届かなかったときは一手が違う（待ってもう一度押す）。</summary>
    public string NoCandidatesText => _boothUnreachable
        ? "BOOTHに問い合わせできませんでした。通信を確かめて、少し待ってからもう一度「自動検索」を押してください。"
        : "候補が見つかりませんでした。BOOTHで探して、見つけた商品のIDかURLを上の欄に入れてください。";

    public string SearchProgressText => _searchTotal > 0
        ? $"{_searchPhase}　{_searchCurrent + 1} / {_searchTotal}"
        : _searchPhase;

    public int SearchCurrent => _searchCurrent;

    public int SearchTotal => _searchTotal;

    public bool HasSearchTotal => _searchTotal > 0;

    /// <summary>
    /// 探す。**何も書かない。**問い合わせは門を1本ずつ通るので数十秒かかることがあり、止められる。
    /// 止めたら何も残さない（もう一度押せばやり直せる）。
    /// </summary>
    public async Task SearchAsync()
    {
        if (_clues is not { IsEmpty: false } clues || IsSearching)
        {
            return;
        }

        using var stop = new CancellationTokenSource();
        _searchStop = stop;
        Candidates.Clear();
        HasSearched = false;
        _boothUnreachable = false;
        ReportSearch("準備しています", 0, 0);
        RaiseSearchState();

        var progress = new Progress<ResolveProgress>(report => ReportSearch(report.Phase, report.Current, report.Total));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.FindReplacementItem(FromId, clues.PreviousName, clues.ShopSubdomain, clues.Paths, progress),
                cancellationToken: stop.Token);

            if (result is CommandResult.ReplacementsFound found)
            {
                ShowSearchResult(found.Proposal);
            }
            else if (result is CommandResult.Failed failed)
            {
                Status = failed.Message;
            }
        }
        catch (OperationCanceledException)
        {
            Status = "自動検索を中止しました。もう一度「自動検索」を押すとやり直せます。";
        }
        finally
        {
            _searchStop = null;
            RaiseSearchState();
        }
    }

    /// <summary>探した結果を並べる。撮る台（ViewShot）も、BOOTH へ行かずにここへ作り物の結果を渡す。</summary>
    internal void ShowSearchResult(ReplacementProposal proposal)
    {
        Candidates.Clear();
        _boothUnreachable = proposal.BoothUnreachable;
        foreach (var candidate in proposal.Candidates)
        {
            Candidates.Add(new ReplacementRow { Candidate = candidate });
        }

        Status = string.Empty;
        HasSearched = true;
        RaiseSearchState();
    }

    private void ReportSearch(string phase, int current, int total)
    {
        _searchPhase = phase;
        _searchCurrent = current;
        _searchTotal = total;
        OnPropertyChanged(nameof(SearchProgressText));
        OnPropertyChanged(nameof(SearchCurrent));
        OnPropertyChanged(nameof(SearchTotal));
        OnPropertyChanged(nameof(HasSearchTotal));
    }

    private void RaiseSearchState()
    {
        foreach (var name in new[]
        {
            nameof(IsSearching), nameof(HasCandidates), nameof(ShowsNoCandidates), nameof(NoCandidatesText),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }
}
