using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// メモ42（2026-10-05）：並べ替えのドラッグで端に来たときの自動の流れが「カクつく」。
/// 本物のドラッグ（OLE の DoDragDrop）はマウスを掴むので台では起こせない。DragOver の代わりに
/// 時計で <see cref="DragEdgeScroll.Update"/> を呼び、一覧の位置がどう動いたかを数える（タグの管理の左の一覧・大分類200件）。
/// 画像はおまけで、見るのはコンソールに出す数字。
/// </summary>
internal static partial class Scenes
{
    /// <summary>画面の周期（60Hz）。台の描画は画面に揃わないので、位置の記録をこの間隔で拾い直して「画面に出たら」を見る</summary>
    private const double DisplayFrameSeconds = 1.0 / 60;

    private static IEnumerable<Scene> DragEdgeScrollScenes =>
    [
        new Scene("drag-edge-scroll-measure", "タグの管理の左の一覧（大分類200件）：ドラッグで端に置いたときの流れの滑らかさを測る（数字はコンソール）", async context =>
        {
            await context.Seed.UserTags.SaveAsync(new UserTagMaster
            {
                Tops = Enumerable.Range(1, 200).Select(index => new UserTagTop { Name = $"作り物の大分類{index:000}" }).ToList(),
            });
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            main.ShowTagManage();
            var screen = context.Screen<TagManageViewModel>();
            await SceneContext.UntilAsync(() => screen.Tops.Count == 200, "大分類の一覧が並ぶ");
            await context.SettleAsync();

            var list = Look.All<ListBox>(root).First(box => ReferenceEquals(box.ItemsSource, screen.Tops));
            var scroller = Look.All<ScrollViewer>(list).First();
            Console.WriteLine($"  一覧：CanContentScroll={scroller.CanContentScroll} ScrollUnit={VirtualizingPanel.GetScrollUnit(list)} 見える高さ={scroller.ViewportHeight:0.#} 流せる高さ={scroller.ScrollableHeight:0.#}");

            // 1. ピクセル単位か（行単位なら 12.5 は行の番号に丸められる）
            scroller.ScrollToVerticalOffset(12.5);
            scroller.UpdateLayout();
            Console.WriteLine($"  12.5 へ流した後の位置 = {scroller.VerticalOffset}");

            // 2. 配置を挟まずに続けて流すと、前の分が消えるか（VerticalOffset は次の配置まで古い値を返す）
            scroller.ScrollToVerticalOffset(1000);
            scroller.UpdateLayout();
            for (var i = 0; i < 4; i++)
            {
                DragEdgeScroll.Apply(scroller, 10);
            }

            scroller.UpdateLayout();
            Console.WriteLine($"  配置を挟まずに 10px を4回流した結果 = {scroller.VerticalOffset - 1000:0.#}px（40px のはず）");

            // 3. 1歩の配置の重さ（流した直後の並べ直し）
            var steps = new List<double>();
            for (var i = 0; i < 150; i++)
            {
                var watch = Stopwatch.StartNew();
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 12);
                scroller.UpdateLayout();
                steps.Add(watch.Elapsed.TotalMilliseconds);
            }

            steps.Sort();
            Console.WriteLine($"  12px 流して並べ直す1回：中央 {steps[steps.Count / 2]:0.00}ms・95% {steps[(int)(steps.Count * 0.95)]:0.00}ms・最長 {steps[^1]:0.00}ms");

            // 4. DragOver の来る間隔ごとに、画面の1コマ（60Hz）で流れた量
            foreach (var interval in new[] { 50.0, 8.0 })
            {
                foreach (var crossing in new[] { false, true })
                {
                    var label = $"DragOver を {interval}ms ごと{(crossing ? "・行が替わると DragLeave" : "")}";
                    scroller.ScrollToVerticalOffset(scroller.ScrollableHeight / 2);
                    scroller.UpdateLayout();
                    var edge = new DragEdgeScroll();
                    var frames = await MeasureFramesAsync(list, scroller, TimeSpan.FromMilliseconds(interval), position: scroller.ViewportHeight - 2, crossing,
                        edge.Update, () => { }, edge.Stop);
                    Console.WriteLine($"  {label}：{frames}");
                }
            }

            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>
    /// 端に置いたまま（下の端から2px）DragOver が来る間隔を真似て1.5秒流し、一覧の位置の移り変わり（ScrollChanged）を記録する。
    /// 記録を 60Hz の格子で拾い直し、画面の1コマごとの移動を数える（台の描画は画面の周期に揃わないため）。
    /// crossing：WPF は点の下の部品が替わった回は DragOver の代わりに DragLeave（前の部品）と DragEnter（次の部品）を出し、
    /// DragLeave は一覧まで上がってくる（dotnet/wpf の DragDrop.cs OleDragOver）。流れて行が替わるたびにそれを真似る
    /// </summary>
    /// <param name="update">DragOver のときに呼ぶ物</param>
    /// <param name="leave">一覧の中で行が替わって DragLeave が来たときに呼ぶ物（RowReorder.OnDragLeave がすること。今の版は何もしない。<see cref="RowReorder.StillInside"/>）</param>
    /// <param name="stop">終わり（離した）</param>
    private static async Task<string> MeasureFramesAsync(ListBox list, ScrollViewer scroller, TimeSpan interval, double position, bool crossing,
        Action<ScrollViewer, double> update, Action leave, Action stop)
    {
        var clock = Stopwatch.StartNew();
        var changes = new List<(double At, double Offset)> { (0, scroller.VerticalOffset) };
        void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0)
            {
                changes.Add((clock.Elapsed.TotalSeconds, scroller.VerticalOffset));
            }
        }

        object? lastRow = null;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = interval };
        timer.Tick += (_, _) =>
        {
            var point = scroller.TranslatePoint(new Point(40, position), list);
            var row = (VisualTreeHelper.HitTest(list, point)?.VisualHit as FrameworkElement)?.DataContext;
            var changed = lastRow is not null && !ReferenceEquals(row, lastRow);
            lastRow = row;
            if (crossing && changed)
            {
                leave();
                return;
            }

            update(scroller, position);
        };
        scroller.ScrollChanged += OnScrollChanged;
        timer.Start();
        update(scroller, position);
        await Task.Delay(1500);
        timer.Stop();
        stop();
        scroller.ScrollChanged -= OnScrollChanged;
        var end = clock.Elapsed.TotalSeconds;

        // 流れ始めの上げ（0.3秒）を外すため、0.4秒より後だけを見る
        double OffsetAt(double time) => changes.Last(change => change.At <= time).Offset;
        var moves = new List<double>();
        for (var time = 0.4; time + DisplayFrameSeconds <= end; time += DisplayFrameSeconds)
        {
            moves.Add(OffsetAt(time + DisplayFrameSeconds) - OffsetAt(time));
        }

        var velocity = DragEdgeScroll.Velocity(position, scroller.ViewportHeight);
        var expected = velocity * DisplayFrameSeconds;
        var stalled = moves.Count(move => Math.Abs(move) < 0.01);
        var error = moves.Average(move => Math.Abs(move - expected));
        var speed = moves.Sum() / (moves.Count * DisplayFrameSeconds);
        return $"60Hz の {moves.Count} コマ中 動かなかったコマ {stalled}・1コマの移動と狙い（{expected:0.0}px）との差 平均 {error:0.0}px・最大の1コマ {moves.Max():0}px・"
            + $"位置が変わった回数 {changes.Count - 1}・流れた速さ {speed:0}px/秒（狙い {velocity:0}）";
    }
}
