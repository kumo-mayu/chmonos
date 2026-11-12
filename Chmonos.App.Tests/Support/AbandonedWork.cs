namespace Chmonos.App.Tests.Support;

/// <summary>
/// 落ちた試験が済ませられずに残した「投げっぱなしの仕事」の数の控え（2026-11-12）。
///
/// **なぜ要るか：**<c>FireAndForget.Pending</c> はプロセス全体で1つの数なので、ある試験が裏の仕事を止めたまま落ちると数が0に戻らず、
/// 後の試験が全部、起動と後片付けの <see cref="UiThread.Settle"/> で10秒ずつ待って落ちた（SZ の一式 870件が2時間止まった。11-11・11-12 に計4回）。
/// 原因の試験は自分で落ちているので、後の試験はその残りを待たない。
///
/// **数しか分からない：**どの仕事が見捨てた物かは <c>Pending</c> から区別できない。試験の途中で見捨てた仕事が終わると、
/// その試験の中だけ、終わった件数ぶん自分の仕事を待たずに通り得る。次の試験の始めに <see cref="Reconcile"/> で詰め直すので、甘さはその1件に留まる。
///
/// 数の扱いを試験で確かめられるよう、静的な控え（<see cref="UiThread"/> が持つ）とは別に new できる型にしてある。
/// </summary>
internal sealed class AbandonedWork
{
    /// <summary>待たないことにした仕事の数。</summary>
    public int Count { get; private set; }

    /// <summary>誰が・いつ・何件残したか。後の試験が落ちたときの文に添える。残りが無ければ null。</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// 落ちた試験の後片付けで、待っても済まなかった仕事を見捨てる。
    /// **足さずに今の数を入れる**：今の <paramref name="pending"/> は、前に見捨てた分のうちまだ走っている物をもう含んでいる
    /// </summary>
    public void Abandon(int pending, string test, DateTime at)
    {
        if (pending <= Count)
        {
            return;
        }

        var added = pending - Count;
        Count = pending;
        Note = $"前の試験（{test}）が投げっぱなしの仕事を {added} 件残しました（{at:HH:mm:ss}）。"
            + $"後の試験は、見捨てた {Count} 件までは待ちません。";
    }

    /// <summary>
    /// 試験の始め（自分の仕事がまだ0の時）に呼ぶ。この時の <paramref name="pending"/> は前の残りそのものなので、
    /// 見捨てた仕事が終わった分だけ控えを減らす。減らさずにおくと、後の試験が投げた本物の仕事を待たずに通す
    /// </summary>
    public void Reconcile(int pending)
    {
        if (pending < Count)
        {
            Count = Math.Max(pending, 0);
        }

        if (Count == 0)
        {
            Note = null;
        }
    }

    /// <summary>見捨てた分を除いて、投げっぱなしの仕事が済んだか。</summary>
    public bool Settled(int pending) => pending <= Count;
}
