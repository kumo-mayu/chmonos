using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定：「すべての設定を既定に戻す」（メモ29・ユーザ判断 2026-10-04）。
/// 戻さないのは、使う人の環境に特有の物（取り込み元・監視するフォルダ。非表示・除外・外した記録と保存先は設定の外にある）。
/// 何を戻し何を残すかは <see cref="AppSettings.ResetToDefaults"/> が決める
/// </summary>
public sealed partial class SettingsViewModel
{
    private string _resetNote = string.Empty;

    /// <summary>戻した結果の1行（戻したボタンのすぐ下）。次に別の操作をしても消えない、その行だけの知らせ。</summary>
    public string ResetNote
    {
        get => _resetNote;
        private set => SetField(ref _resetNote, value);
    }

    private RelayCommand? _resetAllSettingsCommand;

    public RelayCommand ResetAllSettingsCommand => _resetAllSettingsCommand ??= new RelayCommand(ResetAllSettings);

    /// <summary>
    /// 取り返しがつかないので聞く。何が戻り何が戻らないかを先に言う——「全部消える」と読まれると押せず、
    /// 「取り込み元も消える」と読まれたまま押すと困る
    /// </summary>
    private void ResetAllSettings()
    {
        var answer = Services.Notice.Show(
            "表示・操作・ショートカット・取り込みの動き・画像・通信の間隔の設定を、すべて既定に戻します。\n\n"
            + "取り込み元・監視するフォルダ・非表示にしたもの・保存先は、そのままです。\n"
            + "元には戻せません。",
            "設定を既定に戻す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        ResetAllSettingsAsync().Forget();
    }

    /// <summary>
    /// 書いてから画面の値を合わせる。書く前に合わせると、<c>Save</c> が前の値の一部を書き戻す道ができる。
    /// 表示の大きさ・色・カードの幅は、それぞれ自分の値を持って自分で書く部品なので、その値も既定へ戻す
    /// （書く値は今と同じ既定なので、重ねて書いても食い違わない）
    /// </summary>
    internal async Task ResetAllSettingsAsync()
    {
        Status = string.Empty;
        await SaveAsync(settings => settings.ResetToDefaults());
        if (Status.Length > 0)
        {
            // 書けなかった（理由は Status に出ている）。画面の値は変えない
            return;
        }

        var defaults = _services.Settings;
        _suppressSave = true;
        try
        {
            ApplyFields(defaults);
            foreach (var row in ShortcutRows)
            {
                row.Note = string.Empty;
                row.SetGesture(Services.Shortcuts.GestureOf(defaults.Shortcuts ?? new ShortcutSettings(), row.Action));
            }

            foreach (var field in Notes.Fields)
            {
                Notes[field] = string.Empty;
            }

            LoadCardAttributes(defaults);

            AppZoom.Current.Percent = defaults.DisplayZoomPercent;
            AppTheme.Current.Mode = defaults.ColorTheme;
            ItemViewSize.Current.CardWidth = defaults.CardWidth;
            ItemViewSize.Current.ListRowHeight = defaults.ListRowHeight;
            _services.PaneWidths.ResetAll();
            PaneWidthNote = string.Empty;
        }
        finally
        {
            _suppressSave = false;
        }

        // 表示の設定（R-18・区切り・サブタグなど）が一覧の中身に効くので、読み直す
        OnPropertyChanged(string.Empty);
        ResetNote = "設定を既定に戻しました。";
        await _main.ReloadLibraryAsync();
    }

    /// <summary>設定の値をこの画面の持つ欄へ写す。起動時と、既定に戻した後で同じ物を使う（写し忘れの食い違いを作らない）。</summary>
    private void ApplyFields(AppSettings settings)
    {
        _showSubTagsInList = settings.ShowSubTagsInList;
        _showAdult = settings.ShowAdult;
        _showHiddenCountInSearch = settings.ShowHiddenCountInSearch;
        _placeNewConditionNearSameKind = settings.PlaceNewConditionNearSameKind;
        _showSortDividers = settings.ShowSortDividers;
        _showAcquiredSortDividers = settings.ShowAcquiredSortDividers;
        _thumbnailRole = settings.ThumbnailRole;
        _gallerySwitchOnHover = settings.GallerySwitchOnHover;
        _returnToSearchWhenEditDone = settings.ReturnToSearchWhenEditDone;
        _showEditQueueStrip = settings.ShowEditQueueStrip;
        _notifyOnUpdateByDefault = settings.NotifyOnUpdateByDefault;
        _resumeFetchInBackground = settings.ResumeFetchInBackground;
        _saveImages = settings.SaveImages;
        _refreshIntervalDays = settings.RefreshIntervalDays;
        _notificationRetentionCount = settings.NotificationRetentionCount;
        _searchHistoryCount = settings.SearchHistoryCount;
        _fetchIntervalMs = settings.FetchIntervalMs;
        _imageMaxEdgePixels = settings.ImageMaxEdgePixels;
        _imageQuality = settings.ImageQuality;
        _modificationImageMaxEdgePixels = settings.ModificationImageMaxEdgePixels;
        _saveModificationImagesAtOriginalSize = settings.SaveModificationImagesAtOriginalSize;
        _shopBannerRecheckDays = settings.ShopBannerRecheckDays;
        _avatarDetectRecheckDays = settings.AvatarDetectRecheckDays;
        _startImportOnDrop = settings.StartImportOnDrop;
        _startImportOnLaunch = settings.StartImportOnLaunch;
        _projectManager = settings.ProjectManager;
    }
}
