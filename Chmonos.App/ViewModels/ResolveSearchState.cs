namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定の画面の自動検索が、今どのファイルについて・どこまで進んでいるか（ユーザ判断 2026-10-05 メモ60）。
///
/// **主画面が持つ**：未確定の画面は開くたびに作り直すので、画面が持つと離れて戻ったときに進み具合が消え、
/// 終わっても結果が出なかった。検索は1本ずつ（長い作業の帯は1本）なので、持つのも1組。
/// 進みは検索した対象を選んでいる行にだけ出し、ほかの行の「自動検索」は押せずに理由を出す（検索は他を妨げる。登録は妨げない）。
/// </summary>
public sealed class ResolveSearchState : ViewModelBase
{
    private string? _target;
    private IReadOnlyCollection<string> _ownerHashes = [];
    private string _phase = string.Empty;
    private int _current;
    private int _total;

    /// <summary>検索している対象（元zip・展開物の根のフォルダ・ファイル）。走っていなければ null。</summary>
    public string? Target => _target;

    public bool IsRunning => _target is not null;

    /// <summary>検索の結果を持つファイル（行の札「検索中」を付ける行）。</summary>
    public IReadOnlyCollection<string> OwnerHashes => _ownerHashes;

    public string Phase => _phase;

    public int Current => _current;

    public int Total => _total;

    /// <summary>終わった（結果が出た・やめた・失敗した）。開いている画面は、同じ対象を選んでいれば覚えた結果を出し直す。</summary>
    public event Action<string>? Ended;

    public void Begin(string target, IReadOnlyCollection<string> ownerHashes)
    {
        _target = target;
        _ownerHashes = ownerHashes;
        Report("準備しています", 0, 0);
    }

    public void Report(string phase, int current, int total)
    {
        _phase = phase;
        _current = current;
        _total = total;
        OnPropertyChanged(null);
    }

    public void End()
    {
        if (_target is not { } target)
        {
            return;
        }

        _target = null;
        _ownerHashes = [];
        _phase = string.Empty;
        _current = 0;
        _total = 0;
        OnPropertyChanged(null);
        Ended?.Invoke(target);
    }

    public bool IsSearching(string? target)
        => target is not null && string.Equals(_target, target, StringComparison.OrdinalIgnoreCase);
}
