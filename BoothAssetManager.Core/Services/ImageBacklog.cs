using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 前の取り込みが途中で終わって残った画像を、次の起動で取り直す（梯子の⑤の再開）。
///
/// **これを自動で始めてよいのは、対象が手元のJSONだけで決まるから。**
/// <c>Booth.Images</c> の件数と <c>images/{商品ID}/</c> のファイル数の差がそのまま対象になる。
/// ①②の対象を決めるにはフォルダの走査が要り、それは「ユーザが指示していない読み取り」になる。
/// 起動時に黙ってドライブを舐めに行くのは、背景で画像を1枚ずつ取るのとは質が違う。
///
/// 優先度は <see cref="BoothPriority.Gallery"/>。取り込みが始まれば①②が自然に割り込むので、
/// ここで「取り込み中かどうか」を見る必要はない。
/// </summary>
public sealed class ImageBacklog
{
    private readonly DataStore _store;
    private readonly ImagePipeline _images;

    public ImageBacklog(DataStore store, ImagePipeline images)
    {
        _store = store;
        _images = images;
    }

    /// <summary>
    /// まだ全部揃っていない商品のID。件数はディスクを数え直して出す。
    ///
    /// 「取得済み」のフラグを持たないのは、実態とフラグがずれたときに
    /// どちらが正しいか分からなくなるため。数え直せば必ず実態と一致する。
    /// </summary>
    public async Task<IReadOnlyList<string>> FindPendingAsync(CancellationToken cancellationToken = default)
        => (await FindPendingItemsAsync(cancellationToken)).Select(item => item.Id).ToList();

    /// <summary>
    /// 対象の商品そのもの。**読んだ物をそのまま返す**（ユーザ判断 2026-09-21・G18）。
    /// IDだけ返して呼ぶ側がもう一度読み直していたので、起動直後にディスクが二重に回っていた。
    /// </summary>
    private async Task<IReadOnlyList<Models.ItemRecord>> FindPendingItemsAsync(CancellationToken cancellationToken)
    {
        // 画像を取らない設定なら、差はあっても対象ではない。
        // 見ないと全商品が「未取得」に見えて、全件を無駄に回すことになる
        if (!_images.SavesImages)
        {
            return [];
        }

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return loaded.Items.Where(HasMissingImages).ToList();
    }

    /// <summary>
    /// まだ手元に無い画像があるか。**1枚ずつ見る**（ユーザ判断 2026-09-21・G7）。
    ///
    /// 前は「BOOTHの枚数 > 手元の .webp の数 + 取れなかった印」で決めていた。
    /// **自分で足した絵も、BOOTHの一覧から消えたので残している絵も、同じ場所に同じ拡張子である**ため
    /// 数に混ざり、本当は欠けていても「揃っている」と判定されていた（自分で絵を足した商品は永久に対象外）。
    /// 今の一覧のURLごとに「手元にあるか・取れなかった印があるか」を見れば、混ざりようがない。
    /// 見るファイルの数は前とほぼ同じ。
    /// </summary>
    private bool HasMissingImages(Models.ItemRecord item)
        => item.Booth.Images.Any(image => !_images.IsSettled(item.Id, image.OriginalUrl));

    /// <summary>
    /// 残っている画像を取りに行く。1件ずつ保存されるので、どこで止めても続きから進む。
    /// </summary>
    /// <returns>この呼び出しで落とせた枚数。</returns>
    public async Task<int> ResumeAsync(
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 対象を決めるときに読んだ商品をそのまま使う（読み直さない・G18）
        var items = await FindPendingItemsAsync(cancellationToken);
        if (items.Count == 0)
        {
            return 0;
        }

        var downloaded = 0;
        var done = 0;

        // **1周目は全件の1枚目だけ**（ユーザ判断 2026-09-21・Q3）。
        // 1商品ずつ最後まで取っていたので、先頭の商品が20枚取り終えるまで
        // 後ろの商品は1枚も出てこなかった。梯子でも④（1枚目）は⑤（残り）より先。
        //
        // 進み具合の単位は「1商品につき2つ（1枚目・残り）」。途中で数が戻らないようにするため
        var total = items.Count * 2;
        progress?.Report((0, total));

        using (BoothClient.Prioritize(BoothPriority.Thumbnail))
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.Booth.Images.Count > 0)
                {
                    // 既に手元にある物は「落とした」に数えない（SyncOneAsync は手元にあるだけでも true）
                    var had = File.Exists(_images.FilePathFor(item.Id, item.Booth.Images[0].OriginalUrl));
                    if (await _images.SyncOneAsync(item.Id, item.Booth.Images[0], cancellationToken) && !had)
                    {
                        downloaded++;
                    }
                }

                progress?.Report((++done, total));
            }
        }

        using (BoothClient.Prioritize(BoothPriority.Gallery))
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1枚目は既に手元にあるので、ここでは飛ばされる（枚数には数えない）
                var result = await _images.SyncAsync(item.Id, item.Booth.Images, cancellationToken);
                downloaded += result.Downloaded;

                progress?.Report((++done, total));
            }
        }

        return downloaded;
    }
}
