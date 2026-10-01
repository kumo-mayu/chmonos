using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 足跡を溜めて、まとめて <c>recent.json</c> に書く。取り込みの「追加した」に使う。
///
/// 取り込みは新しい商品1件ごとに足跡を打つ。1件ずつ書くと、そのたびに育ったファイルを丸ごと読んで丸ごと書くので、
/// 2,000件の取り込みで 2,000回の読み書き・合計はおよそ件数の2乗になっていた（書くたびに読む側の写しも捨てる）。
/// 溜めて <see cref="FlushEvery"/> 件ごとと段の区切りで書けば、回数は件数の 1/<see cref="FlushEvery"/> になる。
///
/// **書くときは錠の中で今のファイルに当てる**（<see cref="JsonFileStore{T}.UpdateAsync(Func{T, T}, CancellationToken)"/>）。
/// 溜めている間に人が商品を開いた「閲覧」の足跡は、溜めた分を重ねても消えない。
/// </summary>
public sealed class RecentStampBuffer
{
    /// <summary>
    /// この件数が溜まったら、段の区切りを待たずに書く。
    ///
    /// 区切りまで溜めるだけだと、落ちたとき（例外で抜けるのではなく、プロセスごと止まったとき）に
    /// その回の「追加した」が全部消える。①は1件ごとに BOOTH へ問い合わせ、間を1.5秒以上空けるので、
    /// 50件は少なくとも75秒ぶん——落ちて失うのはその間に足した商品の「追加した」だけで、商品そのものは無事
    /// （足跡が無い商品は「不明」として出るだけ）。書く回数は 2,000件で 2,000回 → 40回
    /// （作り物で測って 18秒 → 0.35秒。docs/research/memory-budget.md）。
    /// 40回でもう取り込み全体（①だけで50分以上）から見て無視できるので、これより粗くしても落ちたときに失う量が増えるだけ。
    /// 時間ではなく件数で区切るのは、試験が時計に左右されないようにするため。
    /// </summary>
    public const int FlushEvery = 50;

    private readonly JsonFileStore<RecentLog> _file;
    private readonly List<RecentStamp> _pending = [];

    public RecentStampBuffer(JsonFileStore<RecentLog> file)
    {
        _file = file;
    }

    /// <summary>まだ書いていない足跡の数。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>足跡を1つ溜める。<see cref="FlushEvery"/> 件に達したら書く。</summary>
    public Task AddAsync(string itemId, RecentKind kind, DateTimeOffset at)
    {
        _pending.Add(new RecentStamp(itemId, kind, at));
        return _pending.Count >= FlushEvery ? FlushAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// 溜めた足跡を1回で書く。溜まっていなければ何もしない。
    ///
    /// **取り消しの印は受けない。**中止で抜けるときにも呼ぶので、印で書くのを止めると溜めた分を捨ててしまう。
    /// 1回の書き込みは数ミリ秒で、待たせる長さではない。
    ///
    /// 書けなかったら（ファイルが開けないなど）溜めたまま残し、次に書くときに一緒に書く。
    /// 足跡が欠けても商品の記録は無事なので、取り込みは止めない。
    /// </summary>
    public async Task FlushAsync()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var stamps = _pending.ToList();
        try
        {
            await _file.UpdateAsync(
                log => new RecentLog { Entries = RecentActivity.TouchAll(log.Entries, stamps) },
                CancellationToken.None);
            _pending.RemoveRange(0, stamps.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
