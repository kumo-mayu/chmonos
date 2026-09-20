namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// タグの管理・属性の管理・アバターの管理で、商品をカードで出すかリストで出すか（ユーザ指示 2026-09-20・M4）。
///
/// **検索・ショップ・フォルダ・改変と同じ切り替え**（`ItemCardResources` の `ItemViewModeSwitch`）を使う。
/// 前はこの3画面だけ、詰まった行に固定されていた。
/// 覚え方も同じ（`ui-state.json` の `itemListScreens`）で、画面ごとに別の鍵を持つ。
/// </summary>
internal sealed class ManageItemView
{
    private readonly AppServiceContainer _services;
    private readonly string _screen;
    private readonly Action _changed;

    public ManageItemView(AppServiceContainer services, string screen, Action changed)
    {
        _services = services;
        _screen = screen;
        _changed = changed;
        IsListMode = ItemListMode.IsList(services, screen);
    }

    /// <summary>リストで出すか。**既定はリスト**——この3画面は「中身を確かめる」場なので、名前が読める方を先に出す。</summary>
    public bool IsListMode { get; private set; } = true;

    public bool IsCardMode => !IsListMode;

    public void Set(bool list)
    {
        if (IsListMode == list)
        {
            return;
        }

        IsListMode = list;
        ItemListMode.Save(_services, _screen, list);
        _changed();
    }
}
