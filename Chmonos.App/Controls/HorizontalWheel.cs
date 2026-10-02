using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Chmonos.App.Controls;

/// <summary>
/// Shift＋ホイールと横ホイール（チルト）を、マウスの下で横に送れる入れ物の横送りにする（ユーザ指摘 2026-10-02 メモ1・メモ4）。
///
/// WPF の ScrollViewer は Shift を見ずに縦に送り、横ホイールの知らせ（WM_MOUSEHWHEEL）は受け口が無い。
/// 横に送れる所は、組み込んだ商品ページ・改変の詳細・画面全体（ViewportFitHost）・リストの列と散っていて、
/// 所ごとに書くと付け忘れた所だけ黙って効かない。ScrollViewer の型に1回で掛け、横ホイールはスレッドのメッセージで1回受ける。
///
/// 送る先は、マウスの下からたどって**いちばん内側の、その向きへまだ送れる入れ物**。内側が端まで来ていたら外へ渡す
/// （組み込んだ商品ページを右端まで送ったら、画面全体が続けて送る。ブラウザと同じ）。
/// 横に送れる入れ物が1つも無ければ何もしない——Shift＋ホイールは今までどおり縦に流れる。
/// </summary>
internal static class HorizontalWheel
{
    private const int WmMouseHWheel = 0x020E;

    private static bool _registered;
    private static bool _listening;

    /// <summary>今押している修飾キー。試験は Shift を押した体にして知らせを送る（本物のキーは押せない）。</summary>
    internal static Func<ModifierKeys> Modifiers { get; set; } = () => Keyboard.Modifiers;

    /// <summary>Shift＋ホイールの受け口を ScrollViewer の型に掛ける。部品を作る前に1回呼ぶ。</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        // 下りの知らせ（Preview）で受ける。上りで受けると、内側の ScrollViewer が先に縦に送って「済んだ」にしてしまう
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    /// <summary>
    /// 横ホイールを受け始める（アプリ本体の起動のときだけ）。WPF が知らせに変えないので、スレッドのメッセージを直に見る。
    /// 横ホイールはフォーカスのある窓に届くので、窓ごとに受け口を付けるより、スレッドで1回見る方が漏れない（小窓も同じ）
    /// </summary>
    public static void ListenForTilt()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 下りの知らせは外側の ScrollViewer から順に来る。最初に受けた所で送り先を決めて済ませる
        if (e.Handled || Modifiers() != ModifierKeys.Shift)
        {
            return;
        }

        // ホイールを手前へ回す（Delta が負）と右へ。縦の「下へ」と同じ向き
        if (Scroll(e.OriginalSource as DependencyObject, -e.Delta))
        {
            e.Handled = true;
        }
    }

    private static void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WmMouseHWheel)
        {
            return;
        }

        // 上位の16ビットが回した量（右へ倒すと正）
        var delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
        if (Scroll(Mouse.DirectlyOver as DependencyObject, delta))
        {
            handled = true;
        }
    }

    /// <summary>
    /// <paramref name="source"/> から外へたどり、横に送れる入れ物を <paramref name="amount"/>（右が正・px）だけ送る。
    /// 送れる入れ物があれば、端まで来ていて動かなくても真（縦に流さない）。
    /// </summary>
    internal static bool Scroll(DependencyObject? source, double amount)
    {
        if (amount == 0)
        {
            return false;
        }

        ScrollViewer? fallback = null;
        for (var node = source; node is not null; node = Parent(node))
        {
            if (node is not ScrollViewer viewer || viewer.ScrollableWidth <= 0.5)
            {
                continue;
            }

            var canMove = amount > 0
                ? viewer.HorizontalOffset < viewer.ScrollableWidth - 0.5
                : viewer.HorizontalOffset > 0.5;
            if (canMove)
            {
                viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + amount);
                return true;
            }

            fallback ??= viewer;
        }

        return fallback is not null;
    }

    // 文の中の Run（RichTextBox の中）は見た目の木に居ないので、論理の木で上へ出る
    private static DependencyObject? Parent(DependencyObject node)
        => node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
