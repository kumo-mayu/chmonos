using System.Diagnostics;
using System.Runtime;
using System.Windows;
using System.Windows.Threading;

namespace Chmonos.App.Services;

/// <summary>
/// 使い終わったメモリをOSへ返させる（#71）。
/// 頼むのは4か所：全件を読み込んだ後・サムネイルのキャッシュから捨てた後・画面を移ったとき・
/// ナビの未確定の数を行から数え直した後（取り込みの終わりの後に出るゴミ。2026-09-30）。
/// どれも「ひとまとまりの作業が終わって手が止まる所」で、止めても気付かれにくい。
///
/// 2000件・画像1.6万枚の保存先で起動直後のダンプを取ると、生きている管理オブジェクトは64MBなのに
/// GCのヒープは210MBを握っていて、そのうち75MBは第2世代の空き（断片化37%）だった。
/// 全件の読み込み・検索対象の文字列作り・カードの生成が一度に出すゴミを、
/// 裏で動くGC（詰め直しをしない）が掃くだけなので、穴が空いたまま返されない。
///
/// 生きているのが数十MBなら詰め直しは数十ミリ秒で終わり、止まったとは感じられない。
/// </summary>
public static class MemoryTrim
{
    /// <summary>立て続けに頼まれても、詰め直すのはこの間隔に1回。</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 前回返させた後から、これだけ増えていなければ何もしない。
    /// 間隔だけで絞ると、起動直後の画面の切り替え（まだ返すものが無い）で1回使ってしまい、
    /// 本当に返したい「全件を読み込んだ後」が後ろへずれた。増えた量で決めればその取り違えが起きない。
    /// 取り込み中の1件ごとの読み直しも、溜まるまでは素通りになる。
    /// </summary>
    private const long GrowthThresholdBytes = 32L * 1024 * 1024;

    /// <summary>画面が描き終わるのを待つ分。カードの実体化で出るゴミも一緒に掃ける。</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private static DateTime _lastRunAt = DateTime.MinValue;
    private static long _privateBytesAfterLastRun;
    private static DispatcherTimer? _pending;

    /// <summary>UIスレッドから呼ぶ。既に予約があれば何もしない。</summary>
    public static void Request()
    {
        if (_pending is not null || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        var wait = _lastRunAt + MinimumInterval - DateTime.UtcNow;
        _pending = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher)
        {
            Interval = (wait > TimeSpan.Zero ? wait : TimeSpan.Zero) + SettleDelay,
        };
        _pending.Tick += (_, _) =>
        {
            _pending?.Stop();
            _pending = null;
            Run();
        };
        _pending.Start();
    }

    private static void Run()
    {
        using var self = Process.GetCurrentProcess();
        if (self.PrivateMemorySize64 - _privateBytesAfterLastRun < GrowthThresholdBytes)
        {
            return;
        }

        _lastRunAt = DateTime.UtcNow;

        // 大きいオブジェクト用のヒープは既定では詰め直さない。
        // 読み込みで一時的に作る大きな配列の穴がそこに残るので、この1回だけ詰めさせる
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

        // Aggressive は詰め直した後の空きをOSへ返す（Optimized や Forced は持ったままにする）
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        self.Refresh();
        _privateBytesAfterLastRun = self.PrivateMemorySize64;
    }
}
