namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面：登録している間の進み具合（ユーザ指示 2026-09-29）。
///
/// 「このIDで確定する」は、手元に無い商品ならBOOTHから取ってくる（1本ずつ1.5秒の順番待ちで、取り込み中は更に後ろに並ぶ）。
/// 押してから一覧が動くまで何も出ていなかったので、どうなっているのか分からなかった。
/// 押した場所の近くに「登録しています…」と帯を出す。確定の欄で押す物は確定の欄に、「その他」で押す物は「その他」に出す
/// （画面の上に1つだけ置くと、押した所から離れていて見えない）。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>進み具合を出す場所。</summary>
    private enum RegisteringArea
    {
        None,

        /// <summary>「商品IDを決める」の欄（このIDで確定・まとめて確定・見つからないIDのまま登録）。</summary>
        Decision,

        /// <summary>「その他」の「BOOTHに無い商品として登録する」。</summary>
        Local,

        /// <summary>
        /// フォルダのまま登録。ボタンは元のzipが残っていれば「分かっていること」、zipが無ければ「その他」にあり、
        /// どちらか一方しか出ない（元のzipが残っているかで分かれる）ので、両方に同じ帯を置く。
        /// </summary>
        Folder,
    }

    private RegisteringArea _registeringArea;
    private int _registeringDone;
    private int _registeringTotal;

    /// <summary>
    /// 「商品IDを決める」の欄の帯。その場で済ませる登録（持っている商品へ足す・見つからないIDのまま）と、
    /// 列で走っている「このIDで登録」（選んだ行の分だけ。メモ60）の両方をここに出す。
    /// </summary>
    public bool IsRegisteringInDecision => _registeringArea == RegisteringArea.Decision || IsQueueRunningShown;

    /// <summary>その場の登録が無く、選んだ行の登録が列で走っているか（帯の中身を列から取る）。</summary>
    private bool IsQueueRunningShown => _registeringArea == RegisteringArea.None && IsTargetRunning;

    public bool IsRegisteringLocal => _registeringArea == RegisteringArea.Local;

    public bool IsRegisteringFolder => _registeringArea == RegisteringArea.Folder;

    /// <summary>2件以上なら件数まで言う。束を登録すると1件ずつ順に掛けるので、止まっていないことが分かるように。</summary>
    public string RegisteringText => IsQueueRunningShown
        ? QueueRunningText
        : RegisteringLine(_registeringDone, _registeringTotal, _registeringRequestsLeft, _services.Settings.FetchIntervalMs);

    /// <summary>帯の1行。その場の登録と、列で走っている登録で同じ文にする。</summary>
    internal static string RegisteringLine(int done, int total, int? requestsLeft, int intervalMs)
    {
        var text = total > 1
            ? $"登録しています… {done} / {total} 件"
            : "登録しています…";
        return RequestsLeftText(requestsLeft, intervalMs) is { Length: > 0 } left
            ? $"{text}　{left}"
            : text;
    }

    /// <summary>
    /// 手元に無い商品をBOOTHから取るときの、問い合わせの残りの数。まだ分からない・問い合わせが要らないときは null。
    /// 商品JSON・商品ページ・画像・ショップのアイコンの数で、1つずつ間隔を空けて問い合わせる（メモ34）。
    /// 同じIDで束ねた2件目からは手元の商品に足すだけなので、1件ごとに null へ戻す。
    /// </summary>
    private int? _registeringRequestsLeft;

    /// <summary>
    /// 「BOOTHへ あと 14 件・約 1 分」。目安は、残りの数 × 問い合わせの間隔。
    /// 応答に掛かる時間は含めないので、実際は少し長い（取り込みの見込みも、測れるまでは間隔だけで出す）。
    /// 0件のとき・分からないときは空。
    /// </summary>
    internal static string RequestsLeftText(int? left, int intervalMs)
        => left is > 0
            ? $"BOOTHへあと {left} 件・{ImportViewModel.Duration(left.Value * intervalMs / 1000.0)}"
            : string.Empty;

    /// <summary>問い合わせの残りを受ける。裏から届くので、画面のスレッドへ戻して反映する。</summary>
    private IProgress<int> RequestsLeftProgress
        => _requestsLeftProgress ??= new InlineProgress(left => RunOnUiThread(() => SetRequestsLeft(left)));

    private IProgress<int>? _requestsLeftProgress;

    internal void SetRequestsLeft(int? left)
    {
        // 終わった後に遅れて届いた分で、消した目安が戻らないようにする
        if (_registeringArea == RegisteringArea.None)
        {
            return;
        }

        _registeringRequestsLeft = left;
        OnPropertyChanged(nameof(RegisteringText));
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    /// <summary>件数が分かるときだけ実際の進み具合を出し、1件なら動いていることだけ示す（自動検索の帯と同じ）。</summary>
    public bool HasRegisteringTotal => IsQueueRunningShown ? HasQueueTotal : _registeringTotal > 1;

    public int RegisteringTotal => IsQueueRunningShown ? QueueTotal : _registeringTotal;

    public int RegisteringDone => IsQueueRunningShown ? QueueDone : _registeringDone;

    private void StartRegistering(RegisteringArea area, int total)
    {
        _registeringArea = area;
        _registeringTotal = total;
        _registeringDone = 0;
        _registeringRequestsLeft = null;
        NotifyRegistering();
    }

    private void StepRegistering(int done)
    {
        _registeringDone = done;
        _registeringRequestsLeft = null;
        NotifyRegistering();
    }

    private void EndRegistering() => StartRegistering(RegisteringArea.None, 0);

    private void NotifyRegistering()
    {
        OnPropertyChanged(nameof(IsRegisteringInDecision));
        OnPropertyChanged(nameof(IsRegisteringLocal));
        OnPropertyChanged(nameof(IsRegisteringFolder));
        OnPropertyChanged(nameof(RegisteringText));
        OnPropertyChanged(nameof(HasRegisteringTotal));
        OnPropertyChanged(nameof(RegisteringTotal));
        OnPropertyChanged(nameof(RegisteringDone));
    }
}
