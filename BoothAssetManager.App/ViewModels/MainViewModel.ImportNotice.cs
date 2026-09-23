using System.ComponentModel;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主画面：落とした物・フォルダビューから始めた取り込みの知らせ（点検 2026-09-23・動線の点検 A2・A3）。
///
/// 前はファイルやフォルダを落とすと、どの画面からでも取り込み画面へ移っていた（設定で「そのまま始める」を切っていても）。
/// 持っていない商品の URL を登録したときに画面を移していたのをやめた（Q4・ユーザ指示 2026-09-15）のと同じ種類で、
/// 見ていた画面が勝手に替わる。**画面は移さず**、登録の知らせと同じ帯に進み具合と「取り込み画面を開く」を出す。
/// 取り込み画面にいるときに落とした分は今までどおりその画面で見えるので、帯は出さない。
///
/// 文は取り込みの状態から毎回作る（走っている間は件数、終わったら終わったこと、始めていなければ始め方）。
/// 押すか閉じるまで残す——終わった後も、結果（未確定の数など）は取り込み画面で読むので入口を残す。
/// </summary>
public sealed partial class MainViewModel
{
    private bool _hasImportNotice;
    private bool _importNoticeStarted;
    private bool _importNoticeSawRunning;
    private bool _importNoticeHooked;
    private string? _importNoticeNotStarted;
    private RelayCommand? _openImportFromNotice;
    private RelayCommand? _dismissImportNotice;

    public bool HasImportNotice => _hasImportNotice && CurrentViewModel != Import;

    public string ImportNoticeText
    {
        get
        {
            if (!_hasImportNotice)
            {
                return string.Empty;
            }

            if (Import.IsRunning)
            {
                return $"取り込んでいます：{Import.PhaseText}（{Import.ProgressText}）。この画面のまま使えます。";
            }

            if (_importNoticeNotStarted is { } notStarted)
            {
                return notStarted;
            }

            // 始めたが、ファイルの有無を裏で見ている間はまだ走っていない。ここで「終わりました」と言わない
            if (_importNoticeStarted && !_importNoticeSawRunning)
            {
                return "取り込みを始めています…";
            }

            return _importNoticeStarted
                ? "取り込みが終わりました。見つかった物と、商品が決まらなかったファイルは取り込み画面で見られます。"
                : "取り込みの対象に積みました。取り込み画面の「取り込みを開始」で始まります（設定で、落としたらすぐ始めるようにもできます）。";
        }
    }

    public RelayCommand OpenImportFromNoticeCommand => _openImportFromNotice ??= new RelayCommand(() =>
    {
        SetImportNotice(false, false);
        ShowImport();
    });

    public RelayCommand DismissImportNoticeCommand => _dismissImportNotice ??= new RelayCommand(() => SetImportNotice(false, false));

    /// <summary>
    /// 取り込みの対象に積んだ物を、画面を移さずに知らせる。取り込み画面にいるときは何も出さない（その画面で見えている）。
    /// </summary>
    /// <param name="started">そのまま始めたか（設定「落としたらそのまま始める」・フォルダビューのボタン）。</param>
    internal void NoteImportQueued(bool started)
    {
        if (CurrentViewModel == Import)
        {
            return;
        }

        HookImportNotice();
        SetImportNotice(true, started || _importNoticeStarted);
    }

    /// <summary>走っている間の件数と、終わったことを帯に映すため、取り込みと画面の移り変わりを聞いておく（初めて出すときに1回だけ）。</summary>
    private void HookImportNotice()
    {
        if (_importNoticeHooked)
        {
            return;
        }

        _importNoticeHooked = true;
        Import.PropertyChanged += OnImportChangedForNotice;
        PropertyChanged += OnMainChangedForImportNotice;
    }

    private void OnImportChangedForNotice(object? sender, PropertyChangedEventArgs e)
    {
        if (_hasImportNotice && e.PropertyName is nameof(ImportViewModel.IsRunning) or nameof(ImportViewModel.ProgressText) or nameof(ImportViewModel.PhaseText))
        {
            if (Import.IsRunning)
            {
                // 「始めていなかった」分を人が取り込み画面で始めたときも、走り出したら件数に替える
                _importNoticeStarted = true;
                _importNoticeSawRunning = true;
            }

            OnPropertyChanged(nameof(ImportNoticeText));
        }
    }

    // 取り込み画面を開いたら帯は要らない（同じことがその画面に出ている）。離れたらまた出す
    private void OnMainChangedForImportNotice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrentViewModel))
        {
            OnPropertyChanged(nameof(HasImportNotice));
        }
    }

    /// <summary>
    /// 帯を出した後に、取り込みが始まらなかった（落とした物が見つからなかった）と分かった。
    /// 「始めています…」のまま止めずに、始まらなかったことと次にやることを出す。
    /// </summary>
    /// <remarks>
    /// 取り込み画面にいるときは帯を出していないので何もしない（その画面の一覧に何も増えないのが見えている）。
    /// 走っている取り込みがあれば、帯はそちらの件数を出し続ける（上の文の順番）。
    /// </remarks>
    internal void NoteImportNotStarted(string text)
    {
        if (!_hasImportNotice)
        {
            return;
        }

        _importNoticeNotStarted = text;
        OnPropertyChanged(nameof(ImportNoticeText));
    }

    private void SetImportNotice(bool show, bool started)
    {
        _importNoticeNotStarted = null;
        _hasImportNotice = show;
        _importNoticeStarted = show && started;
        _importNoticeSawRunning = show && Import.IsRunning;
        OnPropertyChanged(nameof(HasImportNotice));
        OnPropertyChanged(nameof(ImportNoticeText));
    }
}
