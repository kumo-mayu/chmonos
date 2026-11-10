namespace Chmonos.App.ViewModels;

/// <summary>
/// メモの自動保存（R2・2026-11-10）。押さずに残し、打ち止めてから 0.8 秒で1回書く（ユーザ指示 2026-09-17〜20）。
/// 前は属性・タグ・アバター・改変・ショップ・商品ページの6か所に、待ちの印・入れ替え中の印・800ms の待ちが写されていて、
/// どの値を信じるか（書く時に選んでいる物か、打った時の物か）が画面ごとに違っていた。
///
/// 決め事は3つ：
/// <list type="bullet">
/// <item>**書く先と文は打った時点で決める**（<see cref="Edited"/>）。書く時に選んでいる物を見ると、待ちの間に選び直したとき
/// 移った先へ書いてしまう</item>
/// <item>**選び直しで欄を入れ替える前に、待っている物を元の持ち主へ書き切る**（<see cref="Replace"/>）。
/// 書かずに入れ替えると、移った先で打ち始めたときに待ちが上書きされて、前の物に打った文が消える</item>
/// <item>**入れ替えは打ったことに数えない**。数えると、開いただけの物へ読んだ文をそのまま書き戻す</item>
/// </list>
/// 画面を離れる・閉じる・読み直す前は <see cref="FlushAsync"/>（打ってすぐ閉じると、待っている 0.8 秒のうちに捨てられていた・I10）。
/// </summary>
/// <typeparam name="TOwner">メモの持ち主（行・商品 ID など）。</typeparam>
public sealed class MemoAutoSave<TOwner> where TOwner : class
{
    /// <summary>打ち止めてから書くまでの待ち。1文字ごとに書くとファイルの置き換えが続き、短すぎると打つ途中で書く。どの画面も同じ値。</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(800);

    private readonly Func<TOwner, string, Task> _write;
    private readonly Debounced _timer;
    private (TOwner Owner, string Text)? _pending;
    private bool _replacing;

    /// <param name="write">書く。渡された持ち主と文をそのまま使う（今選んでいる物を見直さない）。</param>
    public MemoAutoSave(Func<TOwner, string, Task> write)
    {
        _write = write;
        _timer = new Debounced(Wait, FlushAsync);
    }

    /// <summary>書くのを待っている物があるか。</summary>
    public bool IsPending => _pending is not null;

    /// <summary>人が打った。持ち主が無い（何も選んでいない）ときと、欄の入れ替えの最中は数えない。</summary>
    public void Edited(TOwner? owner, string text)
    {
        if (_replacing || owner is null)
        {
            return;
        }

        _pending = (owner, text);
        _timer.Request();
    }

    /// <summary>
    /// 選び直しで欄の文を入れ替える。待っている物を先に元の持ち主へ書き、入れ替えの間に欄から来る知らせは打ったことにしない。
    /// 書くのは最初の await より前に書く先と文を控えるので、待たずに入れ替えても控えた方が書かれる
    /// </summary>
    public void Replace(Action setText)
    {
        Flush();
        _replacing = true;
        try
        {
            setText();
        }
        finally
        {
            _replacing = false;
        }
    }

    /// <summary>待っている物を今書く（待たない）。選び直す前に呼ぶ。</summary>
    public void Flush() => FlushAsync().Forget();

    /// <summary>待っている物を今書き、書き終わりまで待てる（画面を離れる前・閉じる前・読み直す前）。待っている物が無ければ済んだ状態で返る。</summary>
    public Task FlushAsync()
    {
        _timer.Cancel();
        if (_pending is not { } pending)
        {
            return Task.CompletedTask;
        }

        _pending = null;
        return _write(pending.Owner, pending.Text);
    }
}
