using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// ⑦ 期限の来た商品を取り直す。梯子のいちばん下。
///
/// **設定は前からあるのに、走らせる部分だけが無かった。**
/// `RefreshIntervalDays = 7` / `RefreshJitterDays = ±3` は既にあり、
/// <see cref="LocalBlock.NextFetchDueAt"/> もその値で書かれていたが、
/// それを読んで動くものがどこにも無かった。設定が嘘をついている状態だった。
///
/// 急ぐ理由が無い唯一の段なので、**いつ中断しても損がない**。
/// 取り込みが始まれば優先順位で自然に譲るので、ここで「取り込み中か」を見る必要はない。
/// </summary>
public sealed class DueRefresh
{
    /// <summary>
    /// 1回の起動で取り直す件数の上限（ユーザ判断 2026-09-21・G15）。
    ///
    /// 200件＝1件2リクエスト×1.5秒で**約10分**。⑦は梯子のいちばん下で急ぐ理由が無く、
    /// これ以上ゲートを占めると、その回に人が押した操作がずっと後ろで待つことになる。
    /// 残りは次の起動へ回る（期限の古い順なので、古い物から順に片付く）。
    /// </summary>
    private const int MaxPerRun = 200;

    private readonly DataStore _store;
    private readonly IItemService _items;

    public DueRefresh(DataStore store, IItemService items)
    {
        _store = store;
        _items = items;
    }

    /// <summary>
    /// 期限の来た商品を、期限の古い順に。
    ///
    /// 予定日を持たない商品（取り込んだきり一度も取り直していないもの）は
    /// 対象にしない。予定は保存時に必ず入るので、無いのは古い形のデータだけ。
    /// </summary>
    public async Task<IReadOnlyList<string>> FindDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            // 仮IDはBOOTHに存在しないので対象にしない。予定日も入れていないが、
            // 手でJSONを書いた場合に備えて、ここでも弾いておく
            .Where(item => !item.IsLocalOnly)
            .Where(item => item.Local.NextFetchDueAt is { } due && due <= now)
            .OrderBy(item => item.Local.NextFetchDueAt!.Value)
            .Select(item => item.Id)
            .ToList();
    }

    /// <summary>
    /// 期限の来たものを順に取り直す。1件2リクエスト（JSON＋HTML）。
    ///
    /// **画像は落とさない。**増えた画像は <see cref="ImageBacklog"/> が拾う。
    /// ここで落とすと「①②が画像より先」の外側に画像の取得が生まれる。
    /// </summary>
    /// <returns>取り直せた件数。</returns>
    public async Task<int> RunAsync(
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var due = await FindDueAsync(DateTimeOffset.Now, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        // **1回の起動で叩く件数に頭打ちを作る**（ユーザ判断 2026-09-21・G15）。
        // 一時失敗では次の予定日を動かさない決まりなので、圏外や相手の不調が続くと
        // 次の起動でも同じ全件が期限切れのまま残り、起動のたびに全件を叩き直していた
        // （1件2リクエスト＋再試行なので、件数が多いと数十分ゲートを占める）。
        // 期限の古い順に並んでいるので、頭から切れば古い物から順に片付く。
        // **予定日は動かさない**——動かすと、こちら側の都合（圏外）で本当の更新の取り直しが遅れる
        if (due.Count > MaxPerRun)
        {
            due = [.. due.Take(MaxPerRun)];
        }

        using var priority = BoothClient.Prioritize(BoothPriority.Background);

        var refreshed = 0;
        var done = 0;

        // 1件目が終わるまで何も出ないと、何をしているのか分からない
        progress?.Report((0, due.Count));

        foreach (var itemId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // **1件ずつ受け止める。**前は1件が投げると残りが全部止まり、しかも期限の古い順に並ぶので
            // 次の起動でも同じ商品が先頭に来て、毎回そこで止まっていた。
            // 受け止めた分は一時失敗と同じ扱い（予定日を動かさない・G15）で、ログに残して次へ進む
            try
            {
                var outcome = await _items.RefreshAsync(itemId, cancellationToken);
                if (outcome is RefreshOutcome.Updated)
                {
                    refreshed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Diagnostics.AppLog.Error("期限の来た商品の取り直し（1件）", exception);
            }

            progress?.Report((++done, due.Count));
        }

        return refreshed;
    }
}
