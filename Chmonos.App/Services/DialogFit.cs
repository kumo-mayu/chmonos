using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Services;

/// <summary>
/// 小窓（ShowDialog で出す窓）を、小さい画面でもはみ出さないようにする。
///
/// 小窓は幅を決めて高さを中身に合わせる（SizeToContent="Height"）。中身が長い窓（送る物を選ぶ窓の一覧は 420 まで伸びる）は、
/// 1366×768・125% の作業領域（高さ約576 DIP）ではボタンの行が画面の下に出て押せなくなる。
/// 窓の大きさを作業領域で頭打ちにし、収まらない分は中身を縦に送る。
/// 収まっている間は送る入れ物は何もしない（高さは中身のまま・スクロールバーも出ない）。
///
/// 小窓を作ったら、出す前にここを呼ぶ（XAML の窓は InitializeComponent の後、コードで組む窓は中身を入れた後）。
/// </summary>
internal static class DialogFit
{
    /// <param name="scrollsItself">
    /// 送る入れ物を窓が自分で持つ（知らせの窓は本文だけを送り、ボタンの帯は常に見せる）。
    /// 包まずに、表示の大きさだけを中身に当てる。窓の高さの頭打ちは同じにする（それで中の入れ物が送り始める）
    /// </param>
    public static void Prepare(Window dialog, bool scrollsItself = false)
    {
        if (scrollsItself)
        {
            if (dialog.Content is FrameworkElement root)
            {
                root.SetResourceReference(FrameworkElement.LayoutTransformProperty, ViewModels.AppZoom.TransformResourceKey);
            }
        }

        // 中身を送る入れ物で包む。作った直後（まだ出していない）なので、包み直しても読み込みの知らせは1回だけ来る
        else if (dialog.Content is UIElement content and not ScrollViewer)
        {
            dialog.Content = null;
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,

                // 送る入れ物自体には Tab で止まらせない（キーボードで進める順が1つ増えるだけになる）
                Focusable = false,
                IsTabStop = false,
                Content = content,
            };

            // 表示の大きさ（AppZoom）。小窓は主の窓の縮尺の外なので、中身に同じ倍率を当てる
            scroll.SetResourceReference(FrameworkElement.LayoutTransformProperty, ViewModels.AppZoom.TransformResourceKey);
            dialog.Content = scroll;
        }

        // 幅は決め打ちなので倍率を掛ける（掛けないと、中身だけが大きくなって同じ幅に押し込まれ、折り返しが増える）。
        // 小窓は主の窓を止めて出すので、出ている間に倍率は変わらない
        if (!double.IsNaN(dialog.Width))
        {
            dialog.Width *= DisplayScale.Zoom;
        }

        dialog.SourceInitialized += (_, _) => FitToWorkArea(dialog);

        // 題の帯を表示の色に合わせる（暗い表のとき暗くする）。帯は Windows が描くので色の表が届かない
        ViewModels.AppTheme.Watch(dialog);

        // 地を書いていない小窓は Windows の既定の白で出る。暗い表では文字だけ明るくなって読めなくなるので、面の色を指す
        // （明るい表の面は白なので、明るいときの見た目は変わらない）
        if (dialog.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue)
        {
            dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        }
    }

    /// <summary>
    /// 窓ができた所（出す前）で、出す先のモニターの作業領域に頭打ちにする。持ち主の窓があればそのモニター
    /// （持ち主の中央に出すので）。
    /// </summary>
    private static void FitToWorkArea(Window dialog)
    {
        if (WindowNative.WorkAreaDip(dialog.Owner ?? dialog) is not { } work)
        {
            return;
        }

        dialog.MaxWidth = work.Width;
        dialog.MaxHeight = work.Height;
        if (!double.IsNaN(dialog.Width) && dialog.Width > work.Width)
        {
            dialog.Width = work.Width;
        }
    }
}
