using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

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
    }

    /// <summary>表示幅が変わったら列数を決め直す（検索画面と同じ。仮想化のために行に切っている）。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is FolderViewDetail detail)
        {
            detail.SetViewportWidth(e.NewSize.Width);
        }
    }
}
