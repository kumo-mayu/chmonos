using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

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

        // **1回の起動で叩く件数に頭打ちを作らない**（ユーザ判断 2026-11-06。前は 200 件まで・G15）。
        // 頭打ちは「ゲートを長く占めると人が押した操作が後ろで待つ」ためだったが、今の門（PriorityGate）は
        // 待っている中でいちばん急ぐ物を通すので、人が押した操作は⑦が何件残っていても次に通る。
        // 頭打ちがあると、大きいライブラリ（2万件なら1日に期限が来るのは約670件）や長く開かなかった後は、
        // 1日1回の起動では残りが増え続けた（2人目の友人の写しで期限 775 件が 200 件で止まった）。
        // 圏外が続く間に全件を叩き直す心配は、届かない失敗が3件続いたら打ち切る見張り（下の outage）が受ける。
        // **予定日は動かさない**——動かすと、こちら側の都合（圏外）で本当の更新の取り直しが遅れる
        using var priority = BoothClient.Prioritize(BoothPriority.Background);

        var refreshed = 0;
        var done = 0;

        // 1件目が終わるまで何も出ないと、何をしているのか分からない
        progress?.Report((0, due.Count));

        // 届かない失敗（応答が無い・5xx）が3件続いたら、この回の残りは取り直さない（ユーザ判断 2026-09-29）。
        // 前の200件の上限だけでは、圏外の間も1件ごとに再試行で長く待ちながら200件を回っていた（今は上限が無いので、なおさら要る）。
        // 予定日は動かさない（一時失敗と同じ）ので、残りは次の起動で期限の古い順にまた来る
        var outage = new BoothOutageWatch();

        foreach (var itemId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outage.IsStopped)
            {
                break;
            }

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

                NoteOutage(outage, outcome);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Diagnostics.AppLog.Error("期限の来た商品の取り直し（1件）", exception);
            }

            progress?.Report((++done, due.Count));
        }

        outage.LogIfStopped("起動時の裏の作業：期限の来た商品の取り直し");
        return refreshed;
    }

    /// <summary>
    /// 取り直しの結果を打ち切りの数えに入れる。問い合わせていない結果（BOOTHに無い商品・手元に無い）は数えも数え直しもしない。
    /// 429 は <see cref="RefreshOutcome.TemporaryFailure"/> で来て、こちらの出し過ぎなので数え直す側に入る
    /// </summary>
    private static void NoteOutage(BoothOutageWatch outage, RefreshOutcome outcome)
    {
        switch (outcome)
        {
            case RefreshOutcome.NotOnBooth or RefreshOutcome.Missing:
                return;
            case RefreshOutcome.Unreachable:
                outage.Note(BoothOutageKind.Offline);
                return;
            case RefreshOutcome.ServerError:
                outage.Note(BoothOutageKind.ServerDown);
                return;
            default:
                outage.Note(BoothOutageKind.None);
                return;
        }
    }
}
