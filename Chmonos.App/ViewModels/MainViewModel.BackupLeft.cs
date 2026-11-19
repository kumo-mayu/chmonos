namespace Chmonos.App.ViewModels;

/// <summary>
/// 前のバックアップの書きかけが、ごみ箱の無いドライブに残ったことを知らせる下の帯（ユーザ判断 2026-11-20・案C）。
/// 起動の片付けは、ごみ箱へ送れない場所では何も消さず記録だけ外す（完全に消すと取り戻せず、Windows の確認の窓は何の窓か分からない）。
/// 前はそれをログにだけ書いたので、書きかけが大きいまま誰にも気付かれず残った。
/// 記録は外した後なので、知らせる機会は起動のこの1回。閉じるまで出し、開き直すと出ない
/// </summary>
public sealed partial class MainViewModel
{
    private string? _leftBackupWriting;
    private RelayCommand? _dismissLeftBackupWriting;
    private RelayCommand? _revealLeftBackupWriting;

    /// <summary>残った書きかけの場所。無ければ null。</summary>
    public string? LeftBackupWriting => _leftBackupWriting;

    public bool HasLeftBackupWriting => _leftBackupWriting is not null;

    public string LeftBackupWritingText => _leftBackupWriting is null
        ? string.Empty
        : $"バックアップの書きかけが残っています。ごみ箱の無いドライブなので消していません。場所：{_leftBackupWriting}";

    /// <summary>書きかけを選んだ状態でエクスプローラを開く。消すかは使う人が決める。</summary>
    public RelayCommand RevealLeftBackupWritingCommand => _revealLeftBackupWriting ??= new RelayCommand(
        () =>
        {
            if (_leftBackupWriting is { } path)
            {
                _services.RevealInFolder(path).Forget();
            }
        });

    public RelayCommand DismissLeftBackupWritingCommand => _dismissLeftBackupWriting ??= new RelayCommand(() => SetLeftBackupWriting(null));

    /// <summary>
    /// 起動の片付けの答えを受ける。片付けは窓を出した後の裏で走り、このクラスが作られた後のことも前のこともあるので、
    /// 今ある答えを読んでから、後から来る答えも受ける
    /// </summary>
    private void WatchLeftBackupWriting()
    {
        _services.BackupWritingLeftFound += path => RunOnUiThread(() => SetLeftBackupWriting(path));
        if (_services.LeftBackupWriting is { } already)
        {
            _leftBackupWriting = already;
        }
    }

    internal void SetLeftBackupWriting(string? path)
    {
        _leftBackupWriting = path;
        OnPropertyChanged(nameof(LeftBackupWriting));
        OnPropertyChanged(nameof(HasLeftBackupWriting));
        OnPropertyChanged(nameof(LeftBackupWritingText));
    }
}
