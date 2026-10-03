using System.Windows;

namespace Chmonos.App.Controls;

/// <summary>
/// フォーカスを受けた部品を見える所まで流すとき、上下左右に少し余分に流す（2026-10-03 の点検・ユーザ判断「Dで直しましょう」）。
///
/// WPF の既定は最小限しか流さないので、Tab で移った部品は流れの端にぴったり付く。
/// 札や三角の印（<c>ChipFocusVisual</c>・<c>SmallFocusVisual</c>）は部品の外側に枠を出すので、
/// 端に付くと ScrollViewer の縁で枠の外の辺が欠けた（検索の「除く」の札・商品ページの「バリエーション」の三角）。
/// 印を内側に重ねると小さな三角や×に枠が被り、流れの中身に余白を足すと全部の画面で並びの端がずれる。
/// 流す先を広げるだけなら、印の形も並びも変わらない。部品ごとに書くと付け忘れるので、型に1回で掛ける。
/// </summary>
internal static class FocusScrollMargin
{
    /// <summary>
    /// 広げる幅。外側に出す印は Margin -2 で枠の太さが 2 なので、部品の縁から外へ 2 はみ出す。
    /// ちょうどでは丸めで1画素欠けることがあるので、倍の 4 を取る
    /// </summary>
    internal const double Margin = 4;

    private static bool _registered;

    /// <summary>広げて頼み直している間。頼み直しもこの受け口を通るので、もう一度広げない。</summary>
    [ThreadStatic]
    private static bool _reissuing;

    /// <summary>流す頼みの受け口を部品の型に掛ける。部品を作る前に1回呼ぶ。</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
    }

    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 道の上の部品ごとに呼ばれる。頼みが出た所で1回だけ見る。
        // マウスで押したときに流さない一覧（NoScrollOnClick）が止めた頼みは、広げて出し直さない
        if (_reissuing || e.Handled || !ReferenceEquals(sender, e.TargetObject)
            || sender is not FrameworkElement target || !target.IsKeyboardFocused)
        {
            return;
        }

        // 空の範囲は「部品の全体」の意味
        var rect = e.TargetRect.IsEmpty ? new Rect(target.RenderSize) : e.TargetRect;
        rect.Inflate(Margin, Margin);

        _reissuing = true;
        try
        {
            target.BringIntoView(rect);
        }
        finally
        {
            _reissuing = false;
        }

        e.Handled = true;
    }
}
