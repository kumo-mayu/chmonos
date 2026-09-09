namespace BoothAssetManager.Core.Booth;

/// <summary>今BOOTHに対して何をしているか。</summary>
public enum BoothActivityKind
{
    /// <summary>何もしていない。</summary>
    Idle,

    /// <summary>前のリクエストから間隔が空くまで待っている。相手のためにわざと待っている。</summary>
    Waiting,

    /// <summary>実際に通信している。</summary>
    Sending,

    /// <summary>失敗したので、次の試行まで待っている。</summary>
    Retrying,
}

/// <summary>
/// 通信の様子。
///
/// 取得は全部 <see cref="BoothClient"/> 1つを通り、ゲートで直列化されている。
/// つまり**どの瞬間も起きていることは1つだけ**なので、状態も1つで足りる。
/// 画面ごとに持つのではなく、これを見て各画面が好きな形で出す。
///
/// 「ただ止まって見える」時間の大半は、実は長さの分かっている待ち
/// （間隔待ち・再試行の待ち・429の指示）なので、残り時間を出せる。
/// </summary>
public sealed record BoothActivity
{
    public static readonly BoothActivity Idle = new() { Kind = BoothActivityKind.Idle };

    public required BoothActivityKind Kind { get; init; }

    /// <summary>何を取りに行っているか。人に見せる短い名前（商品ID、画像、など）。</summary>
    public string? Target { get; init; }

    /// <summary>待ちの長さ。分かっているときだけ入る（通信そのものは分からない）。</summary>
    public TimeSpan? Total { get; init; }

    /// <summary>待ちの残り。<see cref="Total"/> があるときだけ意味がある。</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>再試行の何回目か。1から数える。</summary>
    public int Attempt { get; init; }

    public int MaxAttempts { get; init; }

    /// <summary>429を受けて間隔を広げている最中か。</summary>
    public bool IsThrottled { get; init; }

    /// <summary>待ちの進み具合（0〜1）。バーに使う。長さが分からなければ null。</summary>
    public double? Progress => Total is { TotalMilliseconds: > 0 } total && Remaining is { } remaining
        ? Math.Clamp(1 - (remaining.TotalMilliseconds / total.TotalMilliseconds), 0, 1)
        : null;

    /// <summary>1行で読める説明。常設の行にも画面の詳細にも、同じ文を使う。</summary>
    public string Text => Kind switch
    {
        BoothActivityKind.Waiting => IsThrottled
            ? $"混み合っているので間隔を広げています（あと {Seconds(Remaining)} 秒）"
            : $"間隔を空けています（あと {Seconds(Remaining)} 秒）",

        BoothActivityKind.Sending => Target is null
            ? "BOOTHから取得しています"
            : $"BOOTHから取得しています（{Target}）",

        BoothActivityKind.Retrying =>
            $"応答がないので待っています（{Seconds(Remaining)} 秒後に再試行 {Attempt}/{MaxAttempts}）",

        _ => string.Empty,
    };

    public bool IsActive => Kind != BoothActivityKind.Idle;

    private static string Seconds(TimeSpan? value)
        => value is { } span ? Math.Max(0, Math.Ceiling(span.TotalSeconds)).ToString("0") : "…";
}
