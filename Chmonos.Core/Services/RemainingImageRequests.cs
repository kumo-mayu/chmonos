using Chmonos.Core.Booth;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 登録した商品の残りの画像を、梯子の⑤の段で裏に頼む。登録の列が動いている間は頼むのを待たせ、列が空いたらまとめて頼む。
///
/// ItemService から分けた（点検24・ユーザ判断 2026-10-08「クラス分けは進めてくれ」）。保留の数・待たせた一覧・錠を、
/// 同じ担当に置く（点検26：入れ子の保留と、最後に放したときだけ始めることが、錠1つで守られるように）
/// </summary>
internal sealed class RemainingImageRequests(ImagePipeline pipeline)
{
    /// <summary>
    /// 登録した商品の残りの画像（2枚目から）を、梯子の⑤（<see cref="BoothPriority.Gallery"/>）で裏に頼む（メモ60 案B・ユーザ判断 2026-10-06）。
    ///
    /// 登録の中で全部を取ると、1件が「2＋画像の枚数＋アイコン」になる。友人の写し206件で画像は平均7.4枚・90%で15枚・最大43枚あり、
    /// 1件の登録が中央 約13秒・90% 約30秒・最大 約1.1分かかって、列の後ろの登録を待たせていた。残りを⑤へ回すと1件 約6秒になる。
    /// **人が押した優先度は掛けない**——起動時の⑤と同じ段で走らせ、次に並んだ登録（人が押した操作）や取り込みの①②に先を譲る。
    /// 閉じて途中で止まっても印は置かないので、手元の JSON とディスクの差で次の起動の⑤（<see cref="ImageBacklog"/>）が拾う。
    /// </summary>
    public void Request(string itemId, IReadOnlyList<BoothImage> images)
    {
        if (!pipeline.SavesImages || images.Count <= 1)
        {
            return;
        }

        lock (_galleryHoldGate)
        {
            if (_galleryHolds > 0)
            {
                // 登録の列が動いている間は始めない（下の Hold）。始めると、門が空いた瞬間に待っている
                // 残りの画像が、次の登録の問い合わせの合間に1本ずつ入り、2件目からの登録が見込みの倍ほどかかっていた
                _heldGalleries.Add((itemId, images));
                return;
            }
        }

        StartRemainingImages(itemId, images);
    }

    private readonly object _galleryHoldGate = new();
    private int _galleryHolds;
    private readonly List<(string ItemId, IReadOnlyList<BoothImage> Images)> _heldGalleries = [];

    /// <summary>
    /// 登録の列が動いている間、登録した商品の残りの画像を頼むのを待たせる（ユーザ判断 2026-10-06・メモ60 案B の続き）。
    /// 返した物を Dispose すると（列が空になったら）、待たせた分をまとめて⑤の段で頼む。
    /// 門の決まり（空いた時点で待っている物から選ぶ）には触れず、列を短くする狙いがそのまま出る
    /// </summary>
    public IDisposable Hold()
    {
        lock (_galleryHoldGate)
        {
            _galleryHolds++;
        }

        return new GalleryHold(this);
    }

    private void ReleaseGalleryHold()
    {
        List<(string ItemId, IReadOnlyList<BoothImage> Images)> released;
        lock (_galleryHoldGate)
        {
            if (--_galleryHolds > 0)
            {
                return;
            }

            released = [.. _heldGalleries];
            _heldGalleries.Clear();
        }

        foreach (var (itemId, images) in released)
        {
            StartRemainingImages(itemId, images);
        }
    }

    private sealed class GalleryHold(RemainingImageRequests owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.ReleaseGalleryHold();
            }
        }
    }

    private void StartRemainingImages(string itemId, IReadOnlyList<BoothImage> images)
    {
        BackgroundWork.Run("登録した商品の残りの画像", async () =>
        {
            using var priority = BoothClient.Prioritize(BoothPriority.Gallery);

            // 起動時の⑤と同じく、届かない失敗が3件続いたら残りは問い合わせない（取らなかった絵は次の起動の⑤で取る）
            var outage = new BoothOutageWatch();
            await pipeline.SyncAsync(itemId, images, outage);
            outage.LogIfStopped("登録した商品の残りの画像");
        });
    }
}
