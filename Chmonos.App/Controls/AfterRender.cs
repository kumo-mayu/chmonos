using System.Windows.Media;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 次のコマを描き終えた後に、仕事を1つ走らせる（後から作る物の始まりに使う：<see cref="DeferredSection"/>・<see cref="ProgressiveItems"/>）。
///
/// **Background の段に積むだけでは、絵の後にならない。**WPF は前のコマを画面に出し終えるまで次の描画を待つので、
/// その待ちの間に Background の仕事が先に走り、最初の絵が遅れる（対応40体の商品で、開く6回のうち2回が約0.5秒になった。2026-11-08）。
/// 描く直前の知らせ（<see cref="CompositionTarget.Rendering"/>）を待ってから積むと、そのコマを描き終えた後に走る
/// </summary>
internal static class AfterRender
{
    public static void Run(Action action)
    {
        EventHandler? onRendering = null;
        onRendering = (_, _) =>
        {
            CompositionTarget.Rendering -= onRendering;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, action);
        };
        CompositionTarget.Rendering += onRendering;
    }
}
