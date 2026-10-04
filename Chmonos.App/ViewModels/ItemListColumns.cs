using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>カードとリストのどちらで出すか（画面ごとに ui-state.json に覚える・ユーザ指示 2026-09-14）。</summary>
internal static class ItemListMode
{
    public static bool IsList(AppServiceContainer services, string screen) => services.UiState.ItemListScreens.Contains(screen);

    public static void Save(AppServiceContainer services, string screen, bool list)
        => services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(state => state with
        {
            ItemListScreens = list
                ? [.. state.ItemListScreens.Where(name => name != screen), screen]
                : [.. state.ItemListScreens.Where(name => name != screen)],
        })).Forget();
}

/// <summary>
/// 商品をリストで出すときの列の幅（ユーザ指示 2026-09-14：列の幅もドラッグで変え、その画面の一覧で共通にする）。
///
/// 見出しの境目をドラッグすると幅が変わり、全部の行が同じ幅になる（GridView の列）。
/// 幅は画面ごと（<c>"search"</c>・<c>"folder"</c>）に <see cref="PaneWidths"/> へ覚え、範囲に収める
/// （画面の幅と同じ所。設定画面の「全部を元の幅に戻す」でこれも戻る）。
/// </summary>
public sealed class ItemListColumns : ViewModelBase
{
    private readonly PaneWidths _widths;
    private readonly string _screen;

    /// <param name="screen">画面の名前（幅を覚える鍵の頭）。</param>
    /// <param name="hasSelect">
    /// 選ぶチェックの列を出すか（まとめて操作できる画面だけ）。**出すのは検索とフォルダビュー**で、
    /// ショップ画面と改変の使ったものには無い（2026-09-20 に注釈を実装へ合わせた・V6：前は逆に書いてあった）。
    /// </param>
    /// <param name="shopHeader">ショップの列の見出し（フォルダビューはフォルダの場所も出すので名前を変える）。</param>
    public ItemListColumns(PaneWidths widths, string screen, bool hasSelect, string shopHeader)
    {
        _widths = widths;
        _screen = screen;
        HasSelect = hasSelect;
        ShopHeader = shopHeader;
        System.ComponentModel.PropertyChangedEventManager.AddHandler(ItemViewSize.Current, OnSizeChanged, string.Empty);
    }

    public bool HasSelect { get; }

    public string ShopHeader { get; }

    /// <summary>選ぶチェック。出さない画面では幅0（列ごと見えなくする）。</summary>
    public double SelectWidth
    {
        get => HasSelect ? Get("select") : 0;
        set
        {
            if (HasSelect)
            {
                Set("select", value);
            }
        }
    }

    /// <summary>
    /// 絵の列の幅。画面ごとには覚えず、一覧の右下のスライダー（リストの行の高さ）から決まる（ユーザ判断 2026-09-29）。
    /// 境目をドラッグしたときは行の高さを変える——前の「絵の欄を大きくしたら縦の幅も変える」（2026-09-14）を残す
    /// </summary>
    public double IconWidth
    {
        get => ItemViewSize.Current.ListIconColumnWidth;
        set => ItemViewSize.Current.ListIconColumnWidth = value;
    }

    /// <summary>絵の大きさ。行の高さに合わせた正方形（既定の行52で40px）。</summary>
    public double IconSize => ItemViewSize.Current.ListIconSize;

    /// <summary>
    /// 共有の大きさが変わった（別の画面のスライダー・この一覧の境目）。弱い参照で聞くので、
    /// 使い捨ての画面（ショップ・フォルダ）の列がアプリの終わりまで残ることはない
    /// </summary>
    private void OnSizeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemViewSize.ListRowHeight) || e.PropertyName == nameof(ItemViewSize.ListIconColumnWidth))
        {
            OnPropertyChanged(nameof(IconWidth));
            OnPropertyChanged(nameof(IconSize));
        }
    }

    public double FavWidth
    {
        get => Get("fav");
        set => Set("fav", value);
    }

    public double NameWidth
    {
        get => Get("name");
        set => Set("name", value);
    }

    public double ShopWidth
    {
        get => Get("shop");
        set => Set("shop", value);
    }

    public double ChipsWidth
    {
        get => Get("chips");
        set => Set("chips", value);
    }

    // ---- 検索だけの列（ユーザ判断 2026-10-04・案C：ユーザータグ・属性・払った額・対応）。ほかの画面は列ごと持たない（SearchItemListGridView） ----

    public double TagsWidth
    {
        get => Get("tags");
        set => Set("tags", value);
    }

    public double AttributesWidth
    {
        get => Get("attrs");
        set => Set("attrs", value);
    }

    public double PaidWidth
    {
        get => Get("paid");
        set => Set("paid", value);
    }

    public double AvatarsWidth
    {
        get => Get("avatars");
        set => Set("avatars", value);
    }

    private double Get(string column) => _widths.Get($"{_screen}.col.{column}");

    private void Set(string column, double value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return;
        }

        _widths.Set($"{_screen}.col.{column}", value);

        // 範囲の外まで引かれたら、範囲の端へ戻して見せる
        OnPropertyChanged(property);
    }
}
