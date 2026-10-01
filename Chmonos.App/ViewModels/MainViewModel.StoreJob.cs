namespace Chmonos.App.ViewModels;

/// <summary>保存先を丸ごと触る作業の種類。同時に走るのは1つだけ（書き込みの門 `StoreWriteGate` が1つなので）。</summary>
public enum StoreJobKind
{
    None,
    Export,
    Restore,
    Move,
}

/// <summary>
/// 作業が終わった知らせ。<paramref name="Text"/> は結果の1行、<paramref name="ExportedZip"/> は書き出しに成功したときの zip の場所
/// （知らせに「エクスプローラで開く」を出すのはこれがあるときだけ。失敗・中止では zip が無いので出さない）。
/// </summary>
public sealed record StoreJobOutcome(string Text, string? ExportedZip = null)
{
    public static StoreJobOutcome None { get; } = new(string.Empty);
}

/// <summary>
/// 主画面：保存先を丸ごと触る作業（バックアップの書き出し・戻す・保存先の移動）の状態（公開前の点検 2026-10-01）。
///
/// **設定の画面は開くたびに作り直す**ので、走っている状態と進み具合を画面の側に持つと、
/// 書き出しの途中で別の画面へ移って戻ったときに進み具合が消え、ボタンがまた押せた（押すと2本目が門の前で待つ）。
/// 終わった知らせも、捨てた古い画面に書かれてどこにも出なかった。アプリと同じ寿命の主画面に持たせ、設定の画面は開くたびにここを映す。
///
/// 終わったときに設定の画面にいなければ、取り込みの知らせと同じく下の帯で知らせる（設定の画面にいる間は出さない。そこの1行で見える）。
/// 帯は閉じるか、設定の画面を開くまで残す（開いたら、設定の画面の1行へ移す）。
/// </summary>
public sealed partial class MainViewModel
{
    private StoreJobKind _storeJob;
    private string _storeJobText = string.Empty;
    private StoreJobOutcome _storeJobNotice = StoreJobOutcome.None;
    private RelayCommand? _dismissStoreJobNotice;
    private RelayCommand? _revealStoreJobZip;

    /// <summary>今走っている作業。無ければ <see cref="StoreJobKind.None"/>。</summary>
    public StoreJobKind StoreJob => _storeJob;

    /// <summary>走っている間の進み具合の1行（設定の画面の1行と同じ文）。走っていなければ空。</summary>
    public string StoreJobText => _storeJobText;

    /// <summary>終わった知らせを帯に出しているか。</summary>
    public bool HasStoreJobNotice => _storeJobNotice.Text.Length > 0;

    public string StoreJobNoticeText => _storeJobNotice.Text;

    /// <summary>知らせに「エクスプローラで開く」を出すか（書き出しに成功したときだけ）。</summary>
    public bool HasStoreJobNoticeZip => _storeJobNotice.ExportedZip is not null;

    public RelayCommand DismissStoreJobNoticeCommand =>
        _dismissStoreJobNotice ??= new RelayCommand(() => SetStoreJobNotice(StoreJobOutcome.None));

    /// <summary>
    /// 書き出した zip の場所をエクスプローラで開き、その zip を選んだ状態にする（ユーザ判断 2026-10-01）。
    /// 書き出した後にすることは、たいてい zip を別のドライブやクラウドへ移すこと。知らせはそのまま残す（開いた後も結果は読める）。
    /// </summary>
    public RelayCommand RevealStoreJobZipCommand => _revealStoreJobZip ??= new RelayCommand(
        () => RevealExportedZip(_storeJobNotice.ExportedZip),
        () => HasStoreJobNoticeZip);

    /// <summary>書き出した zip を選んだ状態で開く。設定の画面の1行の「エクスプローラで開く」も同じ口を通す。</summary>
    internal void RevealExportedZip(string? zip)
    {
        if (zip is not null)
        {
            _services.RevealInFolder(zip).Forget();
        }
    }

    /// <summary>作業を始める。前の知らせは役目を終えるので消す（新しい作業の結果と取り違えない）。</summary>
    internal void BeginStoreJob(StoreJobKind kind, string text)
    {
        _storeJob = kind;
        _storeJobText = text;
        SetStoreJobNotice(StoreJobOutcome.None);
        RaiseStoreJob();
    }

    internal void ReportStoreJob(string text)
    {
        _storeJobText = text;
        OnPropertyChanged(nameof(StoreJobText));
    }

    /// <summary>
    /// 作業が終わった。<paramref name="result"/> は結果の1行（空なら知らせない：再起動の窓など、別の形で返事をする物）。
    /// 設定の画面にいれば、その画面が <see cref="StoreJobEnded"/> で受けて1行に出す。いなければ帯に出す。
    /// </summary>
    internal void EndStoreJob(string result) => EndStoreJob(new StoreJobOutcome(result));

    internal void EndStoreJob(StoreJobOutcome result)
    {
        _storeJob = StoreJobKind.None;
        _storeJobText = string.Empty;
        RaiseStoreJob();

        if (CurrentViewModel is SettingsViewModel)
        {
            StoreJobEnded?.Invoke(result);
        }
        else
        {
            SetStoreJobNotice(result);
        }
    }

    /// <summary>設定の画面にいる間に終わった。結果の1行を渡す。</summary>
    internal event Action<StoreJobOutcome>? StoreJobEnded;

    /// <summary>設定の画面を開いたときに、帯に出していた知らせを受け取る（帯は消え、設定の画面の1行に移る）。</summary>
    internal StoreJobOutcome TakeStoreJobNotice()
    {
        var notice = _storeJobNotice;
        SetStoreJobNotice(StoreJobOutcome.None);
        return notice;
    }

    private void RaiseStoreJob()
    {
        OnPropertyChanged(nameof(StoreJob));
        OnPropertyChanged(nameof(StoreJobText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void SetStoreJobNotice(StoreJobOutcome notice)
    {
        if (_storeJobNotice == notice)
        {
            return;
        }

        _storeJobNotice = notice;
        OnPropertyChanged(nameof(HasStoreJobNotice));
        OnPropertyChanged(nameof(StoreJobNoticeText));
        OnPropertyChanged(nameof(HasStoreJobNoticeZip));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
