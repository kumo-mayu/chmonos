using System.Collections.ObjectModel;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「隠したもの」の非表示にした商品を、検索と同じカード・リストで出す（ユーザ判断 2026-11-18・案A）。
/// カードの右クリックの宛先（<see cref="IItemCardHost"/>）はこの画面。書く命令は検索画面と同じ物を借り、
/// 「非表示にする」の所だけ「非表示を解除」に替える。
/// </summary>
public sealed partial class SettingsViewModel : IItemCardHost
{
    /// <summary>非表示にした商品の行（カードとリストの両方の元。カードは見せるときに初めて作る）。</summary>
    public ObservableCollection<TagItemRow> HiddenItems { get; } = [];

    private ManageItemView? _hiddenItemView;

    /// <summary>
    /// カードかリストか。画面の名前で覚える（タグの管理・属性の管理と同じ作り）。
    /// 既定はカード：検索と同じ見え方で、何を隠したかが絵で分かる
    /// </summary>
    private ManageItemView HiddenItemView => _hiddenItemView ??= new ManageItemView(_services, "settings-hidden", () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
    });

    // 一覧の右下の大きさのスライダー（ItemSizeBar）と切り替え（ItemViewModeSwitch）がこの名前で引く
    public bool IsCardMode => HiddenItemView.IsCardMode;

    public bool IsListMode => HiddenItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => HiddenItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => HiddenItemView.Set(true));

    /// <summary>
    /// 検索の写しにある商品なら絵と店の名前を付けて行にする。写しに無ければ（検索をまだ読んでいない）名前だけの行。
    /// 画面のスレッドで呼ぶ（検索の写しを読むため）
    /// </summary>
    private TagItemRow MakeHiddenRow(string itemId, string name)
    {
        var item = _main.Search.FindItem(itemId);
        var thumbnail = item is null
            ? null
            : new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, Core.Models.ItemRecord>()).ItemThumbnailPath(item);
        return new TagItemRow
        {
            ItemId = itemId,
            Name = name,
            ShopName = item?.Booth.Shop?.Name ?? string.Empty,
            ThumbnailPath = thumbnail,
            Thumbnails = _main.Thumbnails,
            CardFactory = () => _main.Search.CardFor(itemId),
            OpenCommand = new RelayCommand(() =>
            {
                OpeningItem?.Invoke();
                OpenHiddenAsync(itemId).Forget();
            }),
        };
    }

    // ---- カードの操作（検索画面と同じ・IItemCardHost） ----

    /// <summary>
    /// 商品ページへ移る直前。戻ったときに流した位置を戻すため、画面へ今の位置を覚えさせる（open.md 50）。
    /// 設定の画面は開くたびに作り直されるので、位置はアプリを閉じるまで持つ静的な値に置く
    /// </summary>
    public event Action? OpeningItem;

    /// <summary>戻ったときに戻す流した位置。1回戻したら捨てる（ナビから開き直したときは先頭から）。</summary>
    internal static double? PendingScrollOffset { get; set; }

    public void OpenItem(ItemCardViewModel card)
    {
        OpeningItem?.Invoke();
        _main.ShowItem(card.Item);
    }

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    public CardMenuCommands MenuCommands => _menuCommands ??= CardMenuCommands.BorrowedFrom(_main.Search) with
    {
        HideItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is ItemCardViewModel card)
            {
                UnhideAsync(card.Item.Id).Forget();
            }
        }),
        HideItemHeader = "非表示を解除",
    };

    private CardMenuCommands? _menuCommands;
}
