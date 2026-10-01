namespace Chmonos.Core.Booth;

/// <summary>打ち切った理由。画面の文を言い分ける（ネットにつながっていない／BOOTH が不調）。</summary>
public enum BoothOutageKind
{
    None,

    /// <summary>応答が1つも来ない（接続できない・タイムアウト）が続いた。</summary>
    Offline,

    /// <summary>BOOTH が 5xx を返し続けた。</summary>
    ServerDown,
}

/// <summary>
/// 1回の作業（取り込みの段・対応アバターの検出・画像の段）の中で、問い合わせの結果を続けて数え、
/// 相手に届かない失敗が <see cref="Limit"/> 件続いたら打ち切りを告げる（ユーザ判断 2026-09-29）。
///
/// ネットにつながっていないと1件ごとに再試行で長く待ち、全件を回るので止まって見えた。
/// BOOTH が落ちている間に全件を再試行で回ると、復旧に時間の掛かる相手へ問い合わせを重ね続ける。
/// **打ち切りは問い合わせを減らす向き**なので、1本ずつ・1.5秒の決め事には触れない。
///
/// 数えるのは「応答が無い」と「5xx」。BOOTH が何か意味のある物を返したら（取れた・404・429・読めない応答）数え直す。
/// 429 はこちらの出し過ぎなので、間隔を広げて待つ今の扱いのまま、打ち切りには数えない。
/// 1〜2件は一瞬の不調でも起きるので、3件で見切る。1つの商品だけが 5xx を返し続けても、間の成功で数え直すので止まらない
/// </summary>
public sealed class BoothOutageWatch
{
    public const int Limit = 3;

    private int _streak;

    /// <summary>打ち切ったか。立ったら、この回の残りは問い合わせない。</summary>
    public bool IsStopped => Stopped != BoothOutageKind.None;

    /// <summary>打ち切った理由（最後に数えた失敗の種類）。</summary>
    public BoothOutageKind Stopped { get; private set; }

    /// <summary>1件の問い合わせの結果（再試行を終えた後の物）を数える。</summary>
    public void Note<T>(BoothFetchResult<T> result)
        => Note(result.IsUnreachable ? BoothOutageKind.Offline
            : result.IsServerError ? BoothOutageKind.ServerDown
            : BoothOutageKind.None);

    /// <summary>
    /// 1件の結果を種類で数える。<see cref="BoothOutageKind.None"/> は BOOTH が意味のある物を返した（数え直す）。
    /// 問い合わせの結果を直に持たない所（⑦は取り直しの結果 <c>RefreshOutcome</c> しか受け取らない）が使う。
    /// </summary>
    public void Note(BoothOutageKind kind)
    {
        if (kind == BoothOutageKind.None)
        {
            _streak = 0;
            return;
        }

        if (++_streak >= Limit)
        {
            Stopped = kind;
        }
    }

    /// <summary>
    /// 打ち切ったことをログに残す（打ち切っていなければ何もしない）。
    /// 裏の作業は画面に窓を出さない決まりなので、残りを取りに行かなかった理由はログで追えるようにする
    /// （止まって見える・画像が埋まらない、と言われたときに、手元の不具合と区別できるように）
    /// </summary>
    public void LogIfStopped(string where)
    {
        switch (Stopped)
        {
            case BoothOutageKind.Offline:
                Diagnostics.AppLog.Warn(where, $"応答の無い失敗が {Limit} 件続いたので、この回の残りは問い合わせませんでした（ネットにつながっていないようです）");
                break;
            case BoothOutageKind.ServerDown:
                Diagnostics.AppLog.Warn(where, $"BOOTHがサーバの不調（5xx）を {Limit} 件続けて返したので、この回の残りは問い合わせませんでした");
                break;
        }
    }
}
