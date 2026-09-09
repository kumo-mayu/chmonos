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
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        // 404だった画像は「決着済み」として数に入れる。数に入れないと、
        // 二度と取れないものを毎回の起動で対象に挙げ続けることになる
        return loaded.Items
            .Where(item => item.Booth.Images.Count > CountOnDisk(item.Id) + _images.CountMissingMarkers(item.Id))
            .Select(item => item.Id)
            .ToList();
    }

    /// <summary>
    /// 残っている画像を取りに行く。1件ずつ保存されるので、どこで止めても続きから進む。
    /// </summary>
    /// <returns>この呼び出しで落とせた枚数。</returns>
    public async Task<int> ResumeAsync(
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await FindPendingAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        using var priority = BoothClient.Prioritize(BoothPriority.Gallery);

        var downloaded = 0;
        var done = 0;

        foreach (var itemId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = await _store.Items.LoadAsync(itemId, cancellationToken);
            if (item is null)
            {
                continue;
            }

            var result = await _images.SyncAsync(itemId, item.Booth.Images, cancellationToken);
            downloaded += result.Downloaded;

            progress?.Report((++done, pending.Count));
        }

        return downloaded;
    }

    private int CountOnDisk(string itemId)
    {
        var directory = _store.Paths.ItemImagesDir(itemId);

        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.webp").Count()
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めないなら「揃っている」ことにする。取りに行っても同じ場所に置けない
            return int.MaxValue;
        }
    }
}
