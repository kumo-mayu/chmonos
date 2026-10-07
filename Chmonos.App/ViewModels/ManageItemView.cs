namespace Chmonos.App.ViewModels;

/// <summary>
/// タグの管理・属性の管理・アバターの管理で、商品をカードで出すかリストで出すか（ユーザ指示 2026-09-20・M4）。
///
/// **検索・ショップ・フォルダ・改変と同じ切り替え**（`ItemCardResources` の `ItemViewModeSwitch`）を使う。
/// 前はこの3画面だけ、詰まった行に固定されていた。
/// 覚え方も同じ（`ui-state.json`）で、画面ごとに別の鍵を持つ。
/// </summary>
internal sealed class ManageItemView
{
    private readonly AppServiceContainer _services;
    private readonly string _screen;
    private readonly bool _defaultList;
    private readonly Action _changed;

    /// <param name="defaultList">
    /// 一度も切り替えていないときにリストで出すか。アバターの管理はリスト（ユーザ指示 2026-10-07：アバターは名前で探すので、
    /// 大きな絵のカードより名前の並ぶリストが先）。タグ・属性の管理はカードのまま
    /// </param>
    public ManageItemView(AppServiceContainer services, string screen, Action changed, bool defaultList = false)
    {
        _services = services;
        _screen = screen;
        _defaultList = defaultList;
        _changed = changed;
        IsListMode = ItemListMode.IsList(services, screen, defaultList);
    }

    public bool IsListMode { get; private set; }

    public bool IsCardMode => !IsListMode;

    public void Set(bool list)
    {
        if (IsListMode == list)
        {
            return;
        }

        IsListMode = list;
        ItemListMode.Save(_services, _screen, list, _defaultList);
        _changed();
    }
}
