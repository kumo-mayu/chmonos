using System.IO;
using System.Net.Http;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 新しい版の知らせ（ユーザ判断 2026-10-08・`docs/spec/data-format.md`「新しい版の確認」）。
/// 起動の後に裏で1日1回まで GitHub に聞き、自分より新しい版があれば下の帯で知らせる。
/// </summary>
public sealed partial class MainViewModel
{
    private string? _updateVersion;

    /// <summary>帯に出す新しい版（無ければ null）。</summary>
    public bool HasUpdateNotice => _updateVersion is not null;

    public string UpdateNoticeText => _updateVersion is null ? string.Empty : $"新しい版（v{_updateVersion}）があります。";

    private RelayCommand? _openUpdatePageCommand;

    public RelayCommand OpenUpdatePageCommand => _openUpdatePageCommand ??= new RelayCommand(() => _services.OpenUrl(UpdateCheck.DownloadPage));

    private RelayCommand? _dismissUpdateCommand;

    /// <summary>×：この版の間は出さない（次の版が出たらまた出す）。</summary>
    public RelayCommand DismissUpdateCommand => _dismissUpdateCommand ??= new RelayCommand(() => DismissUpdateAsync().Forget());

    /// <summary>
    /// 新しい版を確かめる。設定で切っていれば何もしない。前に確かめてから1日たっていなければ、前に聞けた版で帯を決める
    /// （起動のたびに問い合わせない）。聞けなくても日時は書く——つながらない間に、起動のたび聞きに行かない。
    /// 失敗はログだけ（画面には出さない。知らせが出ないだけで、使う人の作業は何も止まらない）
    /// </summary>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!_services.Settings.CheckForUpdates)
        {
            return;
        }

        try
        {
            var record = await _services.Store.UpdateCheck.LoadAsync(cancellationToken);
            var now = DateTimeOffset.Now;
            if (UpdateCheck.IsDue(record, now))
            {
                string? latest = null;
                try
                {
                    latest = await _services.FetchLatestVersion(cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    Core.Diagnostics.AppLog.Warn("新しい版の確認", exception.Message);
                }

                record = await _services.Store.UpdateCheck.UpdateAsync(
                    current => current with { CheckedAt = now, LatestVersion = latest ?? current.LatestVersion },
                    cancellationToken);
            }

            var show = UpdateCheck.VersionToShow(record, Services.AppVersion.Text);
            RunOnUiThread(() => SetUpdateVersion(show));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("新しい版の確認", exception);
        }
    }

    private async Task DismissUpdateAsync()
    {
        var dismissed = _updateVersion;
        SetUpdateVersion(null);
        if (dismissed is null)
        {
            return;
        }

        try
        {
            // 人が押した書き込みなので UiCommand を通す（裏で確かめた日時を書くのは人の操作ではないので直に書く）
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUpdateCheck(current => current with { DismissedVersion = dismissed }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 書けなければ次の起動でまた出るだけ
            Core.Diagnostics.AppLog.Error("新しい版の知らせを閉じる", exception);
        }
    }

    private void SetUpdateVersion(string? version)
    {
        _updateVersion = version;
        OnPropertyChanged(nameof(HasUpdateNotice));
        OnPropertyChanged(nameof(UpdateNoticeText));
    }
}
