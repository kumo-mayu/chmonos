using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定の入力欄ごとの右クリック「既定に戻す（既定：…）」（open.md 170・ユーザ判断 2026-11-19）。
/// 「すべての設定を既定に戻す」しか無く、1項目だけ戻すには既定の値を覚えて打ち直すしかなかった。
///
/// - **書くのはその1項目だけ**：画面の欄を既定にするとき、<see cref="Save"/> を「その項目だけを既定へ当てる」形に切り替える
///   （普段の Save は画面の持つ項目を全部当てる。画面を開いた後にほかの所が書いた値を、ここで巻き戻さないため）。
///   欄の値は欄の setter で変えるので、範囲の知らせを消す・一覧を読み直すといった欄ごとの後始末は普段と同じに走る
/// - 既定の値は <c>new AppSettings()</c>（「すべて既定に戻す」と同じ元）。外から使う API の欄は <see cref="ApiSettings"/>・<see cref="ApiClientList"/> の既定
/// - 今が既定と同じなら押せない（押しても何も変わらない）
/// - 行ごとの一覧（フォルダの置き方・API のソフト・カードに出す属性・許可するオリジン）には付けない（ユーザ判断 2026-11-19。行に既定の値が無い）
/// </summary>
public sealed partial class SettingsViewModel
{
    private static readonly AppSettings Defaults = new();

    /// <summary>1つの欄。Key は欄が束ねる画面の値の名前（XAML の <c>controls:SettingDefault.Key</c>）。</summary>
    /// <param name="Current">今の画面の値（既定と比べる）。</param>
    /// <param name="Default">既定の値。</param>
    /// <param name="Describe">メニューの括弧の中に出す既定の値の言い方。</param>
    /// <param name="Write">設定のファイルへその1項目だけを既定にして当てる。null の欄は、欄の setter が自分の1項目だけを書く（API・色・大きさ）。</param>
    /// <param name="Show">画面の欄を既定にする（setter を通す）。</param>
    internal sealed record DefaultEntry(
        Func<SettingsViewModel, object> Current,
        object Default,
        Func<SettingsViewModel, string> Describe,
        Func<AppSettings, AppSettings>? Write,
        Action<SettingsViewModel> Show);

    private static DefaultEntry Flag(
        Func<SettingsViewModel, bool> current, bool @default, Func<AppSettings, AppSettings> write, Action<SettingsViewModel, bool> show)
        => new(vm => current(vm), @default, _ => @default ? "オン" : "オフ", write, vm => show(vm, @default));

    private static DefaultEntry Number(
        Func<SettingsViewModel, int> current, int @default, string unit, Func<AppSettings, AppSettings> write, Action<SettingsViewModel, int> show)
        => new(vm => current(vm), @default, _ => unit.Length == 0 ? $"{@default}" : $"{@default} {unit}", write, vm => show(vm, @default));

    /// <summary>欄ごとの既定。足した欄は XAML に <c>controls:SettingDefault.Key</c> を付ける（試験 <c>SettingDefaultTests</c> が XAML と照らす）。</summary>
    internal static IReadOnlyDictionary<string, DefaultEntry> DefaultEntries { get; } = new Dictionary<string, DefaultEntry>(StringComparer.Ordinal)
    {
        // ---- 表示 ----
        ["Theme"] = new(_ => AppTheme.Current.Mode, Defaults.ColorTheme, _ => ColorTheme.Label(Defaults.ColorTheme), null,
            _ => AppTheme.Current.Mode = Defaults.ColorTheme),
        ["Zoom"] = new(_ => AppZoom.Current.Percent, Defaults.DisplayZoomPercent, _ => DisplayZoom.Label(Defaults.DisplayZoomPercent), null,
            _ => AppZoom.Current.Percent = Defaults.DisplayZoomPercent),
        [nameof(ShowAdult)] = Flag(vm => vm.ShowAdult, Defaults.ShowAdult,
            s => s with { ShowAdult = Defaults.ShowAdult }, (vm, v) => vm.ShowAdult = v),
        [nameof(ShowRemovedBoothImages)] = Flag(vm => vm.ShowRemovedBoothImages, Defaults.ShowRemovedBoothImages,
            s => s with { ShowRemovedBoothImages = Defaults.ShowRemovedBoothImages }, (vm, v) => vm.ShowRemovedBoothImages = v),
        [nameof(ShowAvatarsViaBase)] = Flag(vm => vm.ShowAvatarsViaBase, Defaults.ShowAvatarsViaBase,
            s => s with { ShowAvatarsViaBase = Defaults.ShowAvatarsViaBase }, (vm, v) => vm.ShowAvatarsViaBase = v),
        [nameof(GroupAvatarsByBase)] = Flag(vm => vm.GroupAvatarsByBase, Defaults.GroupAvatarsByBase,
            s => s with { GroupAvatarsByBase = Defaults.GroupAvatarsByBase }, (vm, v) => vm.GroupAvatarsByBase = v),
        [nameof(ShowAvatarsViaConversion)] = Flag(vm => vm.ShowAvatarsViaConversion, Defaults.ShowAvatarsViaConversion,
            s => s with { ShowAvatarsViaConversion = Defaults.ShowAvatarsViaConversion }, (vm, v) => vm.ShowAvatarsViaConversion = v),
        [nameof(IncludeUnconfirmedConversion)] = Flag(vm => vm.IncludeUnconfirmedConversion, Defaults.IncludeUnconfirmedConversion,
            s => s with { IncludeUnconfirmedConversion = Defaults.IncludeUnconfirmedConversion }, (vm, v) => vm.IncludeUnconfirmedConversion = v),
        [nameof(UseDetectedVariationTargets)] = Flag(vm => vm.UseDetectedVariationTargets, Defaults.UseDetectedVariationTargets,
            s => s with { UseDetectedVariationTargets = Defaults.UseDetectedVariationTargets }, (vm, v) => vm.UseDetectedVariationTargets = v),
        [nameof(ShowVariationTargetsForUnpurchased)] = Flag(vm => vm.ShowVariationTargetsForUnpurchased, Defaults.ShowVariationTargetsForUnpurchased,
            s => s with { ShowVariationTargetsForUnpurchased = Defaults.ShowVariationTargetsForUnpurchased }, (vm, v) => vm.ShowVariationTargetsForUnpurchased = v),
        [nameof(SelectedThumbnailRole)] = new(vm => vm.ThumbnailRole, Defaults.ThumbnailRole, _ => ImageRoles.Label(Defaults.ThumbnailRole),
            s => s with { ThumbnailRole = Defaults.ThumbnailRole }, vm => vm.ThumbnailRole = Defaults.ThumbnailRole),
        [nameof(ShowSubTagsInList)] = Flag(vm => vm.ShowSubTagsInList, Defaults.ShowSubTagsInList,
            s => s with { ShowSubTagsInList = Defaults.ShowSubTagsInList }, (vm, v) => vm.ShowSubTagsInList = v),
        [nameof(ShowCardAttributes)] = Flag(vm => vm.ShowCardAttributes, Defaults.ShowCardAttributes,
            s => s with { ShowCardAttributes = Defaults.ShowCardAttributes }, (vm, v) => vm.ShowCardAttributes = v),
        [nameof(ShowHiddenCountInSearch)] = Flag(vm => vm.ShowHiddenCountInSearch, Defaults.ShowHiddenCountInSearch,
            s => s with { ShowHiddenCountInSearch = Defaults.ShowHiddenCountInSearch }, (vm, v) => vm.ShowHiddenCountInSearch = v),
        [nameof(ShowSortDividers)] = Flag(vm => vm.ShowSortDividers, Defaults.ShowSortDividers,
            s => s with { ShowSortDividers = Defaults.ShowSortDividers }, (vm, v) => vm.ShowSortDividers = v),
        [nameof(ShowAcquiredSortDividers)] = Flag(vm => vm.ShowAcquiredSortDividers, Defaults.ShowAcquiredSortDividers,
            s => s with { ShowAcquiredSortDividers = Defaults.ShowAcquiredSortDividers }, (vm, v) => vm.ShowAcquiredSortDividers = v),
        [nameof(ShortYenInKanji)] = Flag(vm => vm.ShortYenInKanji, Defaults.ShortYenInKanji,
            s => s with { ShortYenInKanji = Defaults.ShortYenInKanji }, (vm, v) => vm.ShortYenInKanji = v),
        [nameof(PlaceNewConditionNearSameKind)] = Flag(vm => vm.PlaceNewConditionNearSameKind, Defaults.PlaceNewConditionNearSameKind,
            s => s with { PlaceNewConditionNearSameKind = Defaults.PlaceNewConditionNearSameKind }, (vm, v) => vm.PlaceNewConditionNearSameKind = v),
        [nameof(SearchHistoryCount)] = Number(vm => vm.SearchHistoryCount, Defaults.SearchHistoryCount, "件",
            s => s with { SearchHistoryCount = Defaults.SearchHistoryCount }, (vm, v) => vm.SearchHistoryCount = v),

        // ---- 商品ページと編集 ----
        [nameof(SelectedFileOrder)] = new(vm => vm.FileOrder, Defaults.FileOrder,
            _ => FileOrders.First(option => option.Value == Defaults.FileOrder).Label,
            s => s with { FileOrder = Defaults.FileOrder }, vm => vm.FileOrder = Defaults.FileOrder),
        // 向きの言い方は並べる物で変わる（新しい順・降順・大きい順）ので、今の並べる物の言い方で言う
        [nameof(FileOrderDescending)] = new(vm => vm.FileOrderDescending, Defaults.FileOrderDescending,
            vm => Defaults.FileOrderDescending ? vm.FileOrderDescendingLabel : vm.FileOrderAscendingLabel,
            s => s with { FileOrderDescending = Defaults.FileOrderDescending }, vm => vm.FileOrderDescending = Defaults.FileOrderDescending),
        [nameof(GallerySwitchOnHover)] = Flag(vm => vm.GallerySwitchOnHover, Defaults.GallerySwitchOnHover,
            s => s with { GallerySwitchOnHover = Defaults.GallerySwitchOnHover }, (vm, v) => vm.GallerySwitchOnHover = v),
        [nameof(ShowEditQueueStrip)] = Flag(vm => vm.ShowEditQueueStrip, Defaults.ShowEditQueueStrip,
            s => s with { ShowEditQueueStrip = Defaults.ShowEditQueueStrip }, (vm, v) => vm.ShowEditQueueStrip = v),

        // ---- 操作 ----
        [nameof(ExposeScreenToAutomation)] = Flag(vm => vm.ExposeScreenToAutomation, Defaults.ExposeScreenToAutomation,
            s => s with { ExposeScreenToAutomation = Defaults.ExposeScreenToAutomation }, (vm, v) => vm.ExposeScreenToAutomation = v),
        [nameof(ReturnToSearchWhenEditDone)] = Flag(vm => vm.ReturnToSearchWhenEditDone, Defaults.ReturnToSearchWhenEditDone,
            s => s with { ReturnToSearchWhenEditDone = Defaults.ReturnToSearchWhenEditDone }, (vm, v) => vm.ReturnToSearchWhenEditDone = v),
        // Unity の3つは、前の版の設定（欄が無い）の読み替えに落ちないよう、既定の値をはっきり書く（null を書くと古い欄の値に戻る）
        [nameof(ConfirmUnitySend)] = Flag(vm => vm.ConfirmUnitySend, Defaults.ConfirmsUnitySend,
            s => s with { ConfirmUnitySend = Defaults.ConfirmsUnitySend }, (vm, v) => vm.ConfirmUnitySend = v),
        [nameof(NotifyUnitySendResult)] = Flag(vm => vm.NotifyUnitySendResult, Defaults.NotifiesUnitySendResult,
            s => s with { NotifyUnitySendResult = Defaults.NotifiesUnitySendResult }, (vm, v) => vm.NotifyUnitySendResult = v),
        [nameof(UnitySendResultToast)] = new(vm => vm.UnitySendResultToast, Defaults.UnitySendResultByToast,
            _ => Defaults.UnitySendResultByToast ? "Windowsの通知" : "ウィンドウ",
            s => s with { UnitySendResultToast = Defaults.UnitySendResultByToast }, vm => vm.UnitySendResultToast = Defaults.UnitySendResultByToast),
        [nameof(ProjectManagerShown)] = new(vm => vm.ProjectManager, Defaults.ProjectManager,
            vm => vm.ProjectManagers.First(option => option.Value == Defaults.ProjectManager).Label,
            s => s with { ProjectManager = Defaults.ProjectManager }, vm => vm.ProjectManager = Defaults.ProjectManager),
        [nameof(OscPort)] = Number(vm => vm.OscPort, Defaults.OscPort, string.Empty,
            s => s with { OscPort = Defaults.OscPort }, (vm, v) => vm.OscPort = v),

        // ---- 取り込みと取得 ----
        [nameof(StartImportOnLaunch)] = Flag(vm => vm.StartImportOnLaunch, Defaults.StartImportOnLaunch,
            s => s with { StartImportOnLaunch = Defaults.StartImportOnLaunch }, (vm, v) => vm.StartImportOnLaunch = v),
        [nameof(WatchFoldersWhileOpen)] = Flag(vm => vm.WatchFoldersWhileOpen, Defaults.WatchFoldersWhileOpen,
            s => s with { WatchFoldersWhileOpen = Defaults.WatchFoldersWhileOpen }, (vm, v) => vm.WatchFoldersWhileOpen = v),
        [nameof(AutoImportWatchedFiles)] = Flag(vm => vm.AutoImportWatchedFiles, Defaults.AutoImportWatchedFiles,
            s => s with { AutoImportWatchedFiles = Defaults.AutoImportWatchedFiles }, (vm, v) => vm.AutoImportWatchedFiles = v),
        [nameof(StartImportOnDrop)] = Flag(vm => vm.StartImportOnDrop, Defaults.StartImportOnDrop,
            s => s with { StartImportOnDrop = Defaults.StartImportOnDrop }, (vm, v) => vm.StartImportOnDrop = v),
        [nameof(ResumeFetchInBackground)] = Flag(vm => vm.ResumeFetchInBackground, Defaults.ResumeFetchInBackground,
            s => s with { ResumeFetchInBackground = Defaults.ResumeFetchInBackground }, (vm, v) => vm.ResumeFetchInBackground = v),
        [nameof(PrefetchCandidateSearch)] = Flag(vm => vm.PrefetchCandidateSearch, Defaults.PrefetchCandidateSearch,
            s => s with { PrefetchCandidateSearch = Defaults.PrefetchCandidateSearch }, (vm, v) => vm.PrefetchCandidateSearch = v),
        [nameof(CheckForUpdates)] = Flag(vm => vm.CheckForUpdates, Defaults.CheckForUpdates,
            s => s with { CheckForUpdates = Defaults.CheckForUpdates }, (vm, v) => vm.CheckForUpdates = v),
        [nameof(RefreshIntervalDays)] = Number(vm => vm.RefreshIntervalDays, Defaults.RefreshIntervalDays, "日",
            s => s with { RefreshIntervalDays = Defaults.RefreshIntervalDays }, (vm, v) => vm.RefreshIntervalDays = v),
        [nameof(ShopBannerRecheckDays)] = Number(vm => vm.ShopBannerRecheckDays, Defaults.ShopBannerRecheckDays, "日",
            s => s with { ShopBannerRecheckDays = Defaults.ShopBannerRecheckDays }, (vm, v) => vm.ShopBannerRecheckDays = v),
        [nameof(AvatarDetectRecheckDays)] = Number(vm => vm.AvatarDetectRecheckDays, Defaults.AvatarDetectRecheckDays, "日",
            s => s with { AvatarDetectRecheckDays = Defaults.AvatarDetectRecheckDays }, (vm, v) => vm.AvatarDetectRecheckDays = v),
        [nameof(NotifyOnUpdateByDefault)] = Flag(vm => vm.NotifyOnUpdateByDefault, Defaults.NotifyOnUpdateByDefault,
            s => s with { NotifyOnUpdateByDefault = Defaults.NotifyOnUpdateByDefault }, (vm, v) => vm.NotifyOnUpdateByDefault = v),
        [nameof(NotificationRetentionCount)] = Number(vm => vm.NotificationRetentionCount, Defaults.NotificationRetentionCount, "件",
            s => s with { NotificationRetentionCount = Defaults.NotificationRetentionCount }, (vm, v) => vm.NotificationRetentionCount = v),
        [nameof(NotificationArchiveMegabytes)] = Number(vm => vm.NotificationArchiveMegabytes, Defaults.NotificationArchiveMegabytes, "MB",
            s => s with { NotificationArchiveMegabytes = Defaults.NotificationArchiveMegabytes }, (vm, v) => vm.NotificationArchiveMegabytes = v),
        [nameof(SaveImages)] = Flag(vm => vm.SaveImages, Defaults.SaveImages,
            s => s with { SaveImages = Defaults.SaveImages }, (vm, v) => vm.SaveImages = v),
        [nameof(FetchIntervalMs)] = Number(vm => vm.FetchIntervalMs, Defaults.FetchIntervalMs, "ミリ秒",
            s => s with { FetchIntervalMs = Defaults.FetchIntervalMs }, (vm, v) => vm.FetchIntervalMs = v),
        [nameof(ImageMaxEdgePixels)] = Number(vm => vm.ImageMaxEdgePixels, Defaults.ImageMaxEdgePixels, "px",
            s => s with { ImageMaxEdgePixels = Defaults.ImageMaxEdgePixels }, (vm, v) => vm.ImageMaxEdgePixels = v),
        [nameof(ImageQuality)] = Number(vm => vm.ImageQuality, Defaults.ImageQuality, string.Empty,
            s => s with { ImageQuality = Defaults.ImageQuality }, (vm, v) => vm.ImageQuality = v),
        [nameof(ModificationImageMaxEdgePixels)] = Number(vm => vm.ModificationImageMaxEdgePixels, Defaults.ModificationImageMaxEdgePixels, "px",
            s => s with { ModificationImageMaxEdgePixels = Defaults.ModificationImageMaxEdgePixels }, (vm, v) => vm.ModificationImageMaxEdgePixels = v),
        [nameof(SaveModificationImagesAtOriginalSize)] = Flag(vm => vm.SaveModificationImagesAtOriginalSize, Defaults.SaveModificationImagesAtOriginalSize,
            s => s with { SaveModificationImagesAtOriginalSize = Defaults.SaveModificationImagesAtOriginalSize }, (vm, v) => vm.SaveModificationImagesAtOriginalSize = v),

        // ---- API（api.json・api-clients.json。欄の setter がその1項目だけを書く） ----
        [nameof(ApiEnabled)] = new(vm => vm.ApiEnabled, new ApiSettings().Enabled, _ => new ApiSettings().Enabled ? "オン" : "オフ", null,
            vm => vm.ApiEnabled = new ApiSettings().Enabled),
        [nameof(ApiAllowWrites)] = new(vm => vm.ApiAllowWrites, new ApiSettings().AllowWrites, _ => new ApiSettings().AllowWrites ? "オン" : "オフ", null,
            vm => vm.ApiAllowWrites = new ApiSettings().AllowWrites),
        [nameof(ApiPort)] = new(vm => vm.ApiPort, ApiSettings.DefaultPort, _ => $"{ApiSettings.DefaultPort}", null,
            vm => vm.ApiPort = ApiSettings.DefaultPort),
        [nameof(ApiImportOnHeadlessLaunch)] = new(vm => vm.ApiImportOnHeadlessLaunch, new ApiSettings().ImportOnHeadlessLaunch,
            _ => new ApiSettings().ImportOnHeadlessLaunch ? "オン" : "オフ", null,
            vm => vm.ApiImportOnHeadlessLaunch = new ApiSettings().ImportOnHeadlessLaunch),
        [nameof(ApiRefusalLimit)] = new(vm => vm.ApiRefusalLimit, ApiClientList.DefaultRefusalLimit, _ => $"{ApiClientList.DefaultRefusalLimit} 回", null,
            vm => vm.ApiRefusalLimit = ApiClientList.DefaultRefusalLimit),
    };

    /// <summary>右クリックの項目の名前。既定の値を括弧に添える（何に戻るか分からないと押せない）。</summary>
    internal string DefaultMenuHeader(string key)
        => DefaultEntries.TryGetValue(key, out var entry) ? $"既定に戻す（既定：{entry.Describe(this)}）" : "既定に戻す";

    /// <summary>今が既定と同じか（同じなら押せない）。</summary>
    internal bool IsAtDefault(string key)
        => !DefaultEntries.TryGetValue(key, out var entry) || Equals(entry.Current(this), entry.Default);

    /// <summary>
    /// <see cref="Save"/> がこの1項目だけを書く間の当て方。null の間は普段どおり画面の持つ項目を全部当てる。
    /// 画面のスレッドだけで触る（setter の中の Save が、同じ呼び出しの中で読む）
    /// </summary>
    private Func<AppSettings, AppSettings>? _saveOnly;

    /// <summary>欄を既定に戻す。書くのはその1項目だけ（ほかの欄の画面の値で、ディスクの今の値を上書きしない）。</summary>
    internal void ResetToDefault(string key)
    {
        if (!DefaultEntries.TryGetValue(key, out var entry) || IsAtDefault(key))
        {
            return;
        }

        _saveOnly = entry.Write;
        try
        {
            entry.Show(this);
        }
        finally
        {
            _saveOnly = null;
        }
    }
}
