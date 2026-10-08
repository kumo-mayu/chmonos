using Chmonos.Core.Booth;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品の画像の、人が決める所（自分で足す画像の追加・削除・並べ替え、画像の役割、サムネイルの指名）。
///
/// ItemService（約3,000行）から分けた（点検24・ユーザ判断 2026-10-08「クラス分けは進めてくれ」）。
/// どれも商品の記録の `Local` の、画像に関わる欄だけを持ち主として宣言して、錠の中で今の値に当てる（`ItemRepository.ChangeLocalAsync`）。
/// 公開の口は ItemService のまま（画面と命令の受け手は変わらない）
/// </summary>
internal sealed class UserImageEditor(DataStore store, ImagePipeline images)
{
    /// <summary>
    /// 自分で足す画像を1枚入れる。
    ///
    /// BOOTHの画像と同じ圧縮を通してライブラリへ保存する。
    /// **同じ絵を2回入れても1枚**にまとまる（保存名が中身のハッシュなので）。
    /// </summary>
    /// <returns>保存したファイル名。画像として読めなければ null。</returns>
    public async Task<string?> AddUserImageAsync(
        string itemId,
        byte[] bytes,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var fileName = await images.SaveUserImageAsync(itemId, bytes, cancellationToken);
        if (fileName is null)
        {
            return null;
        }

        // 既に同じ絵が入っていれば、記録は増やさずファイルだけ入れ替わる。
        // 一覧は書く直前の値に足す（絵を保存する間に別の操作が並べ替えていることがある）
        await store.Items.ChangeLocalAsync(
            itemId,
            current => current.UserImages.Any(image => string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                ? null
                : current with
                {
                    UserImages = [.. current.UserImages, new UserImage
                    {
                        FileName = fileName,
                        AddedAt = DateTimeOffset.Now,
                        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
                    }],
                },
            LocalOwners.UserImages,
            cancellationToken);

        return fileName;
    }

    /// <summary>
    /// 自分で足した画像を消す。**ファイルごと消える。**
    ///
    /// サムネイルに指名していたなら、指名も外す。
    /// BOOTHの画像が消えたときは指名を残す（取り直せば戻る）が、
    /// **自分で消したものは戻らない**ので、指名を残すと永久に空振りする。
    /// </summary>
    public async Task<bool> RemoveUserImageAsync(
        string itemId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await images.DeleteUserImageAsync(itemId, fileName, cancellationToken);

        await store.Items.ChangeLocalAsync(
            itemId,
            current => current with
            {
                UserImages = [.. current.UserImages
                    .Where(image => !string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase))],
                ThumbnailImage = string.Equals(current.ThumbnailImage, fileName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.ThumbnailImage,

                // 役割の行も落とす。残すと、同じ絵を入れ直したときに外したはずの役割が復活する
                ImageRoles = current.ImageRoles
                    .Where(pair => !string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            },
            LocalOwners.RemoveUserImage,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// 自分で足した画像の並びを1つ動かす。
    ///
    /// **動かせるのは自分の画像の中だけ。**BOOTHの並びは観測した事実なので触らない
    /// （ギャラリーは「観測 → 自分の分 → 消えたもの」の順で出る）。
    /// </summary>
    /// <param name="delta">-1 で前へ、+1 で後ろへ。</param>
    public async Task<bool> MoveUserImageAsync(
        string itemId,
        string fileName,
        int delta,
        CancellationToken cancellationToken = default)
    {
        // 並べ替えは書く直前の一覧に当てる（続けて押したとき、前の結果を古い写しで消さない）
        return await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var images = current.UserImages.ToList();
                var from = images.FindIndex(image =>
                    string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase));

                var to = from + delta;
                if (from < 0 || to < 0 || to >= images.Count)
                {
                    return null;
                }

                (images[from], images[to]) = (images[to], images[from]);
                return current with { UserImages = images };
            },
            LocalOwners.UserImages,
            cancellationToken);
    }

    /// <summary>
    /// 画像に役割を付ける。
    ///
    /// **出どころから決まる値と同じなら記録しない。**BOOTHの画像に「BOOTH」を、
    /// 自分で足した画像に「その他」を付けても、それは既定と同じなので書かない。
    /// 全画像分を書き出すと、観測しただけのものまで人が決めたように見える。
    /// </summary>
    public async Task<bool> SetImageRoleAsync(
        string itemId,
        string fileName,
        ImageRole role,
        bool isUserAdded,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileName(fileName);
        var natural = isUserAdded ? ImageRole.Other : ImageRole.Booth;

        return await store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var roles = current.ImageRoles
                    .Where(pair => !string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                if (role != natural)
                {
                    roles[name] = role;
                }

                return current with { ImageRoles = roles };
            },
            LocalOwners.ImageRoles,
            cancellationToken);
    }

    /// <summary>
    /// サムネイルに使う1枚を指名する。**BOOTHの画像も指名できる。**
    /// null を渡すと指名を外し、並びの1枚目に戻る。
    /// </summary>
    public async Task<bool> PinThumbnailAsync(
        string itemId,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var pinned = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);

        return await store.Items.ChangeLocalAsync(
            itemId,
            current => current with { ThumbnailImage = pinned },
            LocalOwners.ThumbnailImage,
            cancellationToken);
    }
}
