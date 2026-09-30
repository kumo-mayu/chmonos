namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主画面：「一時的に展開して開く」の間の帯（大容量の確かめ #3・ユーザ判断 2026-09-30）。
///
/// 前は押してからエクスプローラが開くまで画面が何も変わらず、遅いディスクでは数十秒「押したのに何も起きない」と見えた。
/// 展開は商品ページからもカードの右クリックからも始まり、待つ間に別の画面へ移れるので、
/// Unity へ送るときの帯と同じく、どの画面でも見える下の帯に進み具合と「中止」を出す。
/// 終われば帯を畳んでエクスプローラを開く（済んだ知らせは出さない。開いたフォルダが返事）。
/// </summary>
public sealed partial class MainViewModel
{
    private UnpackingStatus? _unpacking;
    private RelayCommand? _stopUnpack;

    public bool IsUnpacking => _unpacking is not null;

    /// <summary>進み具合の1行（「展開しています… 1.2 GB / 2.3 GB」）。</summary>
    public string UnpackText => _unpacking is { } status
        ? Core.Models.DisplayText.UnpackingLine(status.Count, status.DoneBytes, status.TotalBytes, status.IsStopping)
        : string.Empty;

    /// <summary>何を展開しているか（zip の名前）。2つ以上を並べて展開している間は出さない（数は <see cref="UnpackText"/> に入る）。</summary>
    public string UnpackTargetText => _unpacking is { IsStopping: false } status ? status.Name : string.Empty;

    /// <summary>合計が分かっているか。分かるまで（zip を開く前）と片付けの間は、長さの無い待ちとして出す。</summary>
    public bool HasUnpackProgress => _unpacking is { IsStopping: false, TotalBytes: > 0 };

    public double UnpackProgress => _unpacking is { TotalBytes: > 0 } status
        ? Math.Clamp((double)status.DoneBytes / status.TotalBytes, 0, 1)
        : 0;

    /// <summary>
    /// 「中止」を出すか。押した後は片付けが済むまで引っ込める（もう押す物が無い）。
    /// 押せるかどうかではなく見えるかどうかで切り替える（docs/dev/wpf.md「コマンド」）
    /// </summary>
    public bool CanStopUnpack => _unpacking is { IsStopping: false };

    public RelayCommand StopUnpackCommand => _stopUnpack ??= new RelayCommand(ItemFileActions.StopUnpacking);

    /// <summary>展開の様子が変わった（始まった・進んだ・中止を押した・終わった）。画面のスレッドで届く。</summary>
    private void RefreshUnpacking()
    {
        _unpacking = ItemFileActions.CurrentUnpacking;
        OnPropertyChanged(nameof(IsUnpacking));
        OnPropertyChanged(nameof(UnpackText));
        OnPropertyChanged(nameof(UnpackTargetText));
        OnPropertyChanged(nameof(HasUnpackProgress));
        OnPropertyChanged(nameof(UnpackProgress));
        OnPropertyChanged(nameof(CanStopUnpack));
    }
}
