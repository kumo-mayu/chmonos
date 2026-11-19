using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 共通素体の絵の決め方（ユーザ判断 2026-11-20・152 の案A）。**素体の絵を出す所はすべてここを通す**（場所ごとに探し方を持たない）。
///
/// 順は 人が付けた画像（<see cref="AvatarBaseGroup.Image"/>）→ 素体の商品（<see cref="AvatarBaseGroup.ItemId"/>）の1枚目。
/// どちらも無ければ null（画面は頭の字を出す）。
/// </summary>
public static class AvatarBaseImage
{
    /// <summary>
    /// 人が付けた画像の場所。付けていない・ファイルが無い・名前の形が違うときは null。
    ///
    /// 登録簿は人が手で直せる JSON。<c>..\</c> や絶対パスを書かれても置き場の外を指さないように、
    /// ファイル名だけを取り、アプリが付ける形（<see cref="UserImageName"/>）の物だけを使う
    /// </summary>
    public static string? OwnFile(AppPaths paths, AvatarBaseGroup group)
    {
        if (!IsValidName(group.Image))
        {
            return null;
        }

        var path = Path.Combine(paths.BaseImagesDir, group.Image!);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 画面に出す絵の場所。人が付けた画像が無ければ、素体の商品の1枚目（持っていれば商品の絵、
    /// 持っていなければ控えの1枚。<see cref="AvatarImageSync.IconPath"/>）。
    /// </summary>
    /// <param name="findItem">商品 ID から手元の商品（持っていなければ null）。</param>
    public static string? IconPath(AppPaths paths, AvatarBaseGroup group, Func<string, ItemRecord?> findItem, bool showRemoved)
    {
        if (OwnFile(paths, group) is { } own)
        {
            return own;
        }

        return group.ItemId is { } itemId && StoreIds.IsItemId(itemId)
            ? AvatarImageSync.IconPath(paths, itemId, findItem(itemId), showRemoved)
            : null;
    }

    /// <summary>アプリが付ける形のファイル名か（区切りを含まず、<see cref="UserImageName.Prefix"/> で始まる）。</summary>
    public static bool IsValidName(string? fileName)
        => !string.IsNullOrEmpty(fileName)
            && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && UserImageName.IsUserAdded(fileName);

    /// <summary>この名前の画像を、どれかの素体（消した印の行も）がまだ指しているか。消す前に、錠の後の最新の登録簿で見る</summary>
    public static bool IsReferenced(AvatarRegistry registry, string fileName)
        => registry.BaseGroups.Any(group => string.Equals(group.Image, fileName, StringComparison.OrdinalIgnoreCase));
}
