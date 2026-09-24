namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 進み具合の知らせを間引く。**前に渡してから間が短ければ最新だけを持っておき、次の機会か <see cref="Flush"/> で渡す。**
///
/// 走査と ID の特定は1ファイルごとに知らせていた。画面の Progress は1回ごとに画面のスレッドへ仕事を積み、
/// 受けた側は段の名前・件数・残り時間・常設の1行・一覧の反映を毎回組み直すので、控えから引けるファイルが続くと
/// 数千回が一気に積まれていた（作り物の 300 本の取り込み直しで 600 回）。人の目に要るのは1秒に数回で、
/// 見た目の意味（今どの段で、何件目で、どのファイルか）は最新の1件で足りる。
///
/// 段が変わった知らせはすぐ渡す（段の切り替わりを遅らせない）。
/// </summary>
internal sealed class LatestProgress<T> : IProgress<T>
    where T : class
{
    /// <summary>渡す間隔。1秒に10回（人が数字の動きを追える速さで、それ以上は見分けられない）。</summary>
    public const long IntervalMs = 100;

    private readonly IProgress<T>? _inner;
    private readonly Func<T, object?> _stageOf;
    private readonly Func<long> _nowMs;
    private readonly object _gate = new();
    private T? _held;
    private object? _lastStage;
    private long _lastSentMs;
    private bool _sentAny;

    /// <param name="inner">本当の受け手（null なら何もしない）。</param>
    /// <param name="stageOf">段。これが変わった知らせは間引かない。</param>
    /// <param name="nowMs">今の時刻（ミリ秒）。試験で差し替える。</param>
    public LatestProgress(IProgress<T>? inner, Func<T, object?> stageOf, Func<long>? nowMs = null)
    {
        _inner = inner;
        _stageOf = stageOf;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    public void Report(T value)
    {
        if (_inner is null)
        {
            return;
        }

        lock (_gate)
        {
            var now = _nowMs();
            var stage = _stageOf(value);
            if (_sentAny && Equals(stage, _lastStage) && now - _lastSentMs < IntervalMs)
            {
                _held = value;
                return;
            }

            _held = null;
            _lastStage = stage;
            _lastSentMs = now;
            _sentAny = true;
        }

        _inner.Report(value);
    }

    /// <summary>持っている最新があれば渡す。段の終わりに呼ぶ（最後の1件を落とさない）。</summary>
    public void Flush()
    {
        T? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
            if (held is not null)
            {
                _lastSentMs = _nowMs();
            }
        }

        if (held is not null)
        {
            _inner?.Report(held);
        }
    }
}
