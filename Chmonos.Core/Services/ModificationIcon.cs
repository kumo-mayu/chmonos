using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 改変1件を表す小さな絵の場所（改変の一覧の頭・改変を選ぶ窓の「今ある改変」の行）。
/// **改変に貼った写真の1枚目、無ければそのアバターの絵、どちらも無ければ null**（画面の側が既定の絵＝頭文字を出す）。
/// 改変の姿はアバターそのままではないので、写真があればそれが「この改変」を一番よく表す。
/// </summary>
public static class ModificationIcon
{
    public static string? PathOf(AppPaths paths, ModificationRecord record, ItemRecord? avatarItem, bool showRemoved)
    {
        if (record.Images.Count > 0)
        {
            var photo = Path.Combine(paths.ModificationImagesDir(record.Id), record.Images[0].FileName);
            if (File.Exists(photo))
            {
                return photo;
            }
        }

        return AvatarImageSync.IconPath(paths, record.AvatarItemId, avatarItem, showRemoved);
    }
}
