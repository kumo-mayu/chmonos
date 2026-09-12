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

        using var priority = BoothClient.Prioritize(BoothPriority.Background);

        var refreshed = 0;
        var done = 0;

        // 1件目が終わるまで何も出ないと、何をしているのか分からない
        progress?.Report((0, due.Count));

        foreach (var itemId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await _items.RefreshAsync(itemId, cancellationToken);
            if (outcome is RefreshOutcome.Updated)
            {
                refreshed++;
            }

            progress?.Report((++done, due.Count));
        }

        return refreshed;
    }
}
