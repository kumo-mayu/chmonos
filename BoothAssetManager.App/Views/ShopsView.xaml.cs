using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ShopsView : UserControl
{
    public ShopsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>一覧の幅が変わったら列数を決め直す（行を仮想化の単位にしているため）。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShopsViewModel shops)
        {
            shops.SetViewportWidth(e.NewSize.Width);
        }
    }

    // ---- 戻ったときの一覧の位置（ユーザ判断 2026-09-28） ----
    // 同じ型の画面が続くと View は使い回されるので、Loaded ではなく DataContext の付け替えで結び直す

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShopsViewModel old)
        {
            old.ListReady -= OnListReady;
            old.AnchorReader = null;
        }

        if (e.NewValue is ShopsViewModel shops)
        {
            shops.AnchorReader = () => ListScrollAnchor.Capture(ShopList, KeyOf);
            shops.ListReady += OnListReady;

            // 裏の読み込みが View より先に済むことがある（店の数が少ないと一瞬）
            if (shops.IsListReady)
            {
                ScheduleRestore();
            }
        }
    }

    private void OnListReady(object? sender, EventArgs e) => ScheduleRestore();

    /// <summary>
    /// 位置は一覧を組み終えてから当てる。組んだ直後は、幅から列数を決めて行を切り直す分がまだ済んでいない
    /// （開いた直後は1列で組まれる）。Loaded の優先度で待つと、配置の処理が先に全部済む。
    /// </summary>
    private void ScheduleRestore()
    {
        if (!IsLoaded)
        {
            Loaded += RestoreWhenLoaded;
            return;
        }

        Dispatcher.InvokeAsync(Restore, DispatcherPriority.Loaded);
    }

    private void RestoreWhenLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= RestoreWhenLoaded;
        ScheduleRestore();
    }

    private void Restore()
    {
        if (DataContext is ShopsViewModel { IsListReady: true } shops && shops.TakePendingAnchor() is { } anchor)
        {
            ListScrollAnchor.Restore(ShopList, anchor, KeysOf);
        }
    }

    private static string? KeyOf(object row) => row is ShopCardRow { Cards.Count: > 0 } cards ? cards.Cards[0].Shop.Subdomain : null;

    private static IEnumerable<string> KeysOf(object row)
        => row is ShopCardRow cards ? cards.Cards.Select(card => card.Shop.Subdomain) : [];
}
