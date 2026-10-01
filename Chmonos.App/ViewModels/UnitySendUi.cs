using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// Unity へ送っている間の、画面への出し方（ユーザ判断 2026-09-20・E7・E10）。
///
/// **1件でも進み具合を出す。**1件につき3〜4段（送っています → 取り込み画面で押してください →
/// 既に入っています → Unity 側で確認の窓が出ています）を報告しているのに、
/// 1件の道だけ受け口を渡していなかった。押してから Unity の窓が出るまで無言で、
/// Unity が手前に出るぶん、アプリ側は完全に黙って見えていた。
///
/// 送っている間だけ「中止」を出すために、始まりと終わりも同じ物に持たせる。
/// </summary>
internal sealed class UnitySendUi
{
    private readonly Action<string> _setText;

    /// <param name="setSending">送っている間だけ true。中止のボタンと二重送信の止めに使う。</param>
    /// <param name="setText">進み具合の1行。空文字で消す。</param>
    public UnitySendUi(Action<bool> setSending, Action<string> setText)
    {
        SetSending = setSending;
        _setText = setText;

        // Progress は作ったスレッドの文脈に戻す。画面を持つ側で作られるので、ここで1回だけ作る
        Progress = new Progress<UnityQueueProgress>(report => _setText(report.Text));
    }

    public Action<bool> SetSending { get; }

    public IProgress<UnityQueueProgress> Progress { get; }

    /// <summary>送り始める（進み具合の1行を出し、中止を押せるようにする）。</summary>
    public void Begin(string text)
    {
        SetSending(true);
        _setText(text);
    }

    /// <summary>送り終える。1行は消す（結果は別に言う）。</summary>
    public void End()
    {
        _setText(string.Empty);
        SetSending(false);
    }
}
