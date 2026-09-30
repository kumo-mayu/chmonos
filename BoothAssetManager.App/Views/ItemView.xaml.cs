using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 商品ページ。画像・対応アバター・手元のファイルの欄は、編集画面と共有する部品
/// （<see cref="ItemGalleryPanel"/>・<see cref="ItemAvatarsPanel"/>・<see cref="ItemFilesPanel"/>）。
/// </summary>
public partial class ItemView : UserControl
{
    public ItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// フォルダビューと改変の画面に組み込んだ商品ページで、別の商品を選び直したら、流した位置を先頭へ戻す（ユーザ判断 2026-09-30）。
    ///
    /// 同じ型の ViewModel に替わるとき、WPF は画面を作り直さず DataContext だけを替える。流した位置は画面が持つので、
    /// 前の商品で下まで流していると、次の商品が途中から出ていた（説明の途中や、右の列の下の方）。
    ///
    /// 主の窓の商品ページは戻さない：商品から商品へ移って戻ったとき、流した位置が残っていることで元の所が出る（今の動き）。
    /// 同じ商品を開き直したとき（取り直した後・ファイルを外した後）も戻さない——見ていた所が先頭へ飛ぶ
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ShouldReturnToTop(e.OldValue as ItemViewModel, e.NewValue as ItemViewModel))
        {
            return;
        }

        // 先頭へ戻す命令は次の配置で効く。説明の見出しを後から足す側（ProgressiveItems）は中身が替わった時点で位置を読むので、
        // 「先頭へ戻る」と印で伝える（伝えないと、前の位置が残ると見て、見出しを全部その場で作る）。配置が済んだら外す
        Body.ScrollToHome();
        ProgressiveItems.SetReturnsToTop(Body, true);
        Body.LayoutUpdated += ClearReturnsToTop;
    }

    private void ClearReturnsToTop(object? sender, EventArgs e)
    {
        Body.LayoutUpdated -= ClearReturnsToTop;
        Body.ClearValue(ProgressiveItems.ReturnsToTopProperty);
    }

    /// <summary>中身が替わったとき、流した位置を先頭へ戻すか。組み込んだ商品ページで、別の商品へ替わったときだけ。</summary>
    internal static bool ShouldReturnToTop(ItemViewModel? previous, ItemViewModel? next)
        => previous is not null
           && next is { IsEmbedded: true }
           && !string.Equals(previous.Item.Id, next.Item.Id, StringComparison.Ordinal);
}
