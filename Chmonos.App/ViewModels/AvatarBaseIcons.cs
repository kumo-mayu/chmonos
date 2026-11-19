using System.Windows.Media.Imaging;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 共通素体の名前から絵を引く口（候補の行・札・つなぎ先など、名前しか持たない所）。決め方は <see cref="AvatarBaseImage.IconPath(Core.Storage.AppPaths, AvatarRegistry, string?, Func{string, ItemRecord?}, bool)"/>
/// 1つ（付けた画像 → 素体の商品の1枚目 → 頭の字）。
///
/// 登録簿は絵を引くその時に読む（<see cref="AppServiceContainer.CachedAvatars"/> はファイルが変わっていなければ読み直さない）。
/// 候補を組んだ時の登録簿を掴むと、アバターの管理で画像を変えた後に片付けた古いファイルを指して絵が消える
/// </summary>
public static class AvatarBaseIcons
{
    public static string? PathOf(AppServiceContainer services, string baseName, Func<string, ItemRecord?> findItem)
        => AvatarBaseImage.IconPath(services.Paths, services.CachedAvatars.Load(), baseName, findItem, services.Settings.ShowRemovedBoothImages);

    /// <param name="load">候補の行の頭は <c>LoadForTile</c>、乗せた時の大きな絵は <c>LoadForCard</c>（アバターの絵と同じ大きさに揃える）。</param>
    public static BitmapSource? Load(AppServiceContainer services, string baseName, Func<string, ItemRecord?> findItem, Func<string, BitmapSource?> load)
        => PathOf(services, baseName, findItem) is { } path ? load(path) : null;
}
