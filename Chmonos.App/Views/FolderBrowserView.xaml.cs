using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>フォルダビューの右側（ユーザ指示 2026-09-14）。カードの上の操作は <see cref="ItemCardResources"/> が持つ。</summary>
public partial class FolderBrowserView : UserControl
{
    public FolderBrowserView()
    {
        InitializeComponent();

        // 別のフォルダへ移っても、この見た目は作り直されず中身（DataContext）だけ差し替わる。
        // 大きさは変わらないので SizeChanged が来ず、新しい中身が列数を知らないまま1列に並んでいた
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FolderViewDetail detail && ListArea.ActualWidth > 0)
            {
                detail.SetViewportWidth(ListArea.ActualWidth);
            }
        };

        // カードの大きさ（一覧の右下のスライダー）が変わったら列を割り直す。一覧の幅は変わらないので SizeChanged は来ない。
        // 知らせは静的なので、出ている間だけ聞く（離れた画面を掴んだままにしない）
        Loaded += (_, _) =>
        {
            // Loaded は出し直すたびに来る。2重に聞かないよう、外してから付ける
            Services.CardMetrics.Changed -= OnCardSizeChanged;
            Services.CardMetrics.Changed += OnCardSizeChanged;
        };
        Unloaded += (_, _) => Services.CardMetrics.Changed -= OnCardSizeChanged;
    }

    private void OnCardSizeChanged() => (DataContext as FolderViewDetail)?.RelayoutForCardSize();

    /// <summary>表示幅が変わったら列数を決め直す（検索画面と同じ。仮想化のために行に切っている）。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is FolderViewDetail detail)
        {
            detail.SetViewportWidth(e.NewSize.Width);
        }
    }
}
