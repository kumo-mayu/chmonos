using System.Diagnostics;

namespace ViewShot;

/// <summary>
/// 1つの場面にかかった時間の内訳（起動・アプリの組み立て・場面を組む・落ち着くまで・描く・書き出す）。
///
/// 網羅の撮影（<see cref="Catalog"/>）が 8 分かかり、どこに使っているかが分からなかった（2026-10-05）。
/// 子のプロセスが1行（<c>#timing</c>）で書き、親が summary.txt と索引に載せる。場面の中の待ちは場面ごとに違うので、
/// 待ち（<see cref="Stage.SettleAsync"/>）と描画（<see cref="Stage.Render"/>）はどこから呼ばれても足し込む
/// </summary>
internal static class Timing
{
    public const string Tag = "#timing";

    private static readonly Stopwatch Settle = new();
    private static readonly Stopwatch Render = new();
    private static readonly Stopwatch Save = new();
    private static int _settles;
    private static int _renders;

    private static readonly Dictionary<string, long> Named = [];

    /// <summary>場面を組む中の区切り（作り物を書く・一式を組む・窓の中身を作る）。どれも1場面に1回なので足し込む</summary>
    public static void Add(string name, long ms) => Named[name] = (Named.TryGetValue(name, out var before) ? before : 0) + ms;

    private static long _brokenQuiet;

    public static void NoteBrokenQuiet(long ms) => _brokenQuiet = Math.Max(_brokenQuiet, ms);

    public static long SettleMs => Settle.ElapsedMilliseconds;

    public static int Settles => _settles;

    public static IDisposable Settling()
    {
        _settles++;
        return new Lap(Settle);
    }

    public static IDisposable Rendering()
    {
        _renders++;
        return new Lap(Render);
    }

    public static IDisposable Saving() => new Lap(Save);

    /// <summary>今までの合計を0に戻す（1つのプロセスで続けて描くとき、場面ごとに数え直す）。</summary>
    public static void Reset()
    {
        Settle.Reset();
        Render.Reset();
        Save.Reset();
        _settles = 0;
        _renders = 0;
        _brokenQuiet = 0;
        Named.Clear();
    }

    /// <summary>親が読む1行。値は ms。</summary>
    public static string Line(long startup, long app, long build, long buildSettle, long total)
        => FormattableString.Invariant(
            $"{Tag}\tstartup={startup}\tapp={app}\tbuild={build}\tbuildSettle={buildSettle}\tsettle={Settle.ElapsedMilliseconds}\tsettles={_settles}\trender={Render.ElapsedMilliseconds}\trenders={_renders}\tsave={Save.ElapsedMilliseconds}\ttotal={total}\tbrokenQuiet={_brokenQuiet}")
            + string.Concat(Named.Select(pair => FormattableString.Invariant($"\t{pair.Key}={pair.Value}")));

    private sealed class Lap : IDisposable
    {
        private readonly Stopwatch _watch;

        public Lap(Stopwatch watch)
        {
            _watch = watch;
            _watch.Start();
        }

        // 待ちが入れ子になることは無い（場面は1本の流れで await する）ので、止めるのは外側だけを数える必要がない
        public void Dispose() => _watch.Stop();
    }
}
