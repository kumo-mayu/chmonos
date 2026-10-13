namespace Chmonos.App.Services;

/// <summary>
/// 画面を出さずに動いている間、作業が静かになったら1回だけメモリを片付ける（docs/spec/headless.md）。
///
/// 画面の無い起動では、起動したときの作業（全件を読む見回り・確かめ）で出たごみが、その後なにも動かないので片付けられずに残る。
/// 実物に近い2千件の写しで、専用メモリが API オフ 226 → 92MB・オン 243 → 131MB まで下がった（2026-10-13 に測った）。
/// 常駐させる使い方なので、この差は使う人の PC にずっと乗る。
///
/// 画面ありの起動では使わない：描画などで動き続けるので片付けは自然に進み、強い片付け（圧縮）は画面のスレッドも一瞬止める。
/// 合図（作業の終わり）が続けて来たら待ちを延ばし、静かになってから1回だけ走らせる。
/// </summary>
internal sealed class IdleCompactor : IDisposable
{
    /// <summary>最後の合図から片付けるまでの待ち。続けて終わる段（③→確かめ→⑤→⑦）の間に挟まらない長さ。</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

    private readonly Timer _timer;

    public IdleCompactor()
    {
        _timer = new Timer(_ => Compact(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 窓を開いている間は片付けない（強い片付けは画面のスレッドも止めるので、操作の最中に固まる。点検36）。
    /// 開いたら予約を取り消し、隠したらまた予約する
    /// </summary>
    private volatile bool _paused;

    /// <summary>何かが終わった。<see cref="Quiet"/> の後に片付ける（前の予約は延ばす）。窓を開いている間は予約しない。</summary>
    public void Note()
    {
        if (!_paused)
        {
            _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>指定の時間の後に片付ける（何も合図が来ないときの保険）。</summary>
    public void NoteAfter(TimeSpan delay)
    {
        if (!_paused)
        {
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>窓を開いた（片付けを止める）・隠した（また予約する）。</summary>
    public void SetWindowShown(bool shown)
    {
        _paused = shown;
        if (shown)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else
        {
            Note();
        }
    }

    private void Compact()
    {
        // 予約が走る直前に窓が開いたら片付けない
        if (_paused)
        {
            return;
        }

        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    public void Dispose() => _timer.Dispose();
}
