namespace Chmonos.Core.Services;

/// <summary>
/// Unity が取り込みの後の作業を終えて、外から打った検索を受け付ける状態になったかを決める（1件送った後に入ったフォルダを選ぶ前の待ち）。
///
/// **進捗の窓が消えただけでは足りない**（実機 2026-11-27・スクリプト入りの物を取り込んだ時）。
/// 「Compiling Scripts → Reloading Domain → Completing Domain → Hold on...」の窓が消えた直後から約1.7秒、
/// Unity の主窓は返事をしない（WM_NULL が 1.7 秒止まる。2回とも同じ）。返事が戻った直後に Editor.log へ約47行がまとめて出る。
/// その間に検索を打つと黙って捨てられた（窓が消えただけで選ぶ：3回とも失敗）。
///
/// **返事が止まっている間はログも出ないので、ログの静けさだけを数えない。**
/// 窓が無い・返事が速い・ログに新しい行が無い、の3つがそろった状態が <see cref="QuietMs"/> 続いた時だけ落ち着いたとする。
/// ログが0.5秒静かなだけで選ぶと5回中1回失敗し、3つの条件を 0.3 秒にすると5回とも選べた（窓が消えてから 1.75〜2.1 秒）。
/// </summary>
public sealed class UnitySettleWatch
{
    /// <summary>3つの条件がそろって続く時間。実機で5回とも選べた値（0.3秒）。スクリプトの無い物では3つがすぐそろうので、遅れはこの分だけで済む。</summary>
    public const int QuietMs = 300;

    /// <summary>返事が速いとみなす上限。落ち着いた Unity は 10〜50ms で返し、作業中は 500ms〜1.7秒止まった（実機 2026-11-27）。</summary>
    public const int ReplyLimitMs = 100;

    private long? _quietSince;

    /// <param name="nowMs">見た時刻（増えていく ms）。</param>
    /// <param name="busy">Unity の進捗の窓が出ているか。</param>
    /// <param name="replied">主窓が <see cref="ReplyLimitMs"/> 未満で返事をしたか。</param>
    /// <param name="newLogLines">前に見てから Editor.log に増えた行の数。</param>
    /// <returns>落ち着いたか。</returns>
    public bool Observe(long nowMs, bool busy, bool replied, int newLogLines)
    {
        if (busy || !replied || newLogLines > 0)
        {
            _quietSince = null;
            return false;
        }

        _quietSince ??= nowMs;
        return nowMs - _quietSince.Value >= QuietMs;
    }
}
