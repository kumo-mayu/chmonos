using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定の「隠したもの」の、除外したファイルと商品から外したファイル（open.md 171・ユーザ判断 2026-11-19）。
/// どちらも除外の一覧の共通部品（<see cref="ExclusionListViewModel"/>）で、三角で畳み、既定は閉じ、見出しに件数を出す。
/// 開くまで行を作らない（除外は上限なく溜まる。5,000 件で設定を開くたびに 7 秒余り止まった。large-files-2026-09-30.md）。
/// </summary>
public sealed partial class SettingsViewModel
{
    private ExclusionListViewModel? _excludedList;
    private ExclusionListViewModel? _detachedList;

    /// <summary>管理対象から除外したファイル。解除すると、その場で未確定に戻す（ナビの件数も合わせる）。</summary>
    public ExclusionListViewModel ExcludedList => _excludedList ??= MakeExcludedList();

    /// <summary>
    /// 商品ページで「この商品から外す」を押したファイル。
    /// 外した記録が見えないと、なぜその商品へ紐付かないのかを探す場所が無くなる。
    /// </summary>
    public ExclusionListViewModel DetachedList => _detachedList ??= ExclusionLifting.ForDetached(_services, LoadAsync);

    private ExclusionListViewModel MakeExcludedList()
    {
        var list = ExclusionLifting.ForExcluded(_services, async () =>
        {
            await LoadAsync();

            // 未確定に戻したので、ナビの件数も合わせる
            _main.RefreshBadges();
        });
        list.IsActive = s_excludedExpanded;
        return list;
    }

    /// <summary>
    /// 欄を開いているか。既定は閉じる（開くまで行を作らない）。
    /// ほかの畳む欄と同じく、アプリを閉じるまで覚える（`docs/spec/ui-rules.md`。設定画面は開くたびに作り直される）。
    /// </summary>
    public bool IsExcludedExpanded
    {
        get => s_excludedExpanded;
        set
        {
            if (s_excludedExpanded != value)
            {
                s_excludedExpanded = value;
                ExcludedList.IsActive = value;
                OnPropertyChanged();
            }
        }
    }

    private static bool s_excludedExpanded;

    public bool IsDetachedExpanded
    {
        get => s_detachedExpanded;
        set
        {
            if (s_detachedExpanded != value)
            {
                s_detachedExpanded = value;
                DetachedList.IsActive = value;
                OnPropertyChanged();
            }
        }
    }

    private static bool s_detachedExpanded;

    private static IReadOnlyList<ExclusionEntry> ExcludedEntries(IReadOnlyList<ExcludedFile> files) => ExclusionLifting.Entries(files);

    private IReadOnlyList<ExclusionEntry> DetachedEntries(IReadOnlyList<DetachedRecord> records)
    {
        DetachedList.IsActive = s_detachedExpanded;
        return ExclusionLifting.Entries(records);
    }
}
