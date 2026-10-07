using System.Windows.Media.Imaging;
using Chmonos.App.Services;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 探した結果の行（メモ76）と通知の行（ユーザ指示 2026-10-06）の左に出す、商品のサムネイル。商品の名前だけを大量に見比べると疲れるので、絵を添える。
///
/// **見えた行だけが読む**：<see cref="Image"/> は画面が行を作って読んだときに初めて動く（仮想化された一覧は見えている行しか作らない）。
/// 絵の場所の決定（商品の記録を読む）も、その最初の1回だけ裏で行う。行が作り直されても（再利用）、決めた場所は行が持っているので、読み直さない。
/// 絵は縮めて読む（<see cref="ThumbnailLoader.PeekForFill"/>）ので、3000行を流してもメモリは枠の大きさの分だけ。
/// </summary>
public sealed class ResultThumbnail : ViewModelBase
{
    /// <summary>
    /// 絵の枠の一辺（DIP）。ファイルの行の3行（商品名・ファイル名・場所）の高さを使い切る。小さいと見分けられない（ユーザ 2026-10-06）
    /// </summary>
    public const int EdgeDip = 56;

    private readonly Func<Task<string?>> _resolve;
    private readonly ThumbnailLoader? _loader;
    private State _state;
    private string? _path;

    private enum State
    {
        Unresolved,
        Resolving,
        Resolved,
    }

    /// <summary>
    /// 商品の記録から絵を決める行の絵（取り込みの探した結果・通知の行）。記録を読む・絵の一覧を取るのは、絵が初めて読まれたときに裏で行う。
    /// 商品が消えていれば絵は無く、頭文字のまま。<paramref name="fromCopy"/> は本体の記録が読めない商品のとき：絵の並びと★の指名を控えから取る。
    /// </summary>
    internal static ResultThumbnail ForItem(AppServiceContainer services, ThumbnailLoader? loader, string itemId, string? itemName, bool fromCopy = false) => new(
        itemName,
        () => Task.Run(async () =>
        {
            var item = fromCopy ? services.Store.Items.ReadCopy(itemId) : await services.Store.Items.LoadAsync(itemId);
            if (item is null || loader is null)
            {
                return null;
            }

            var directory = services.Paths.ItemImagesDir(item.Id);
            return PathOf(item, directory, loader.ListFiles(directory), services.Settings.ThumbnailRole, services.Settings.ShowRemovedBoothImages);
        }),
        loader);

    public ResultThumbnail(string? itemName, Func<Task<string?>> resolve, ThumbnailLoader? loader)
    {
        Initial = AvatarText.InitialOf(itemName);
        _resolve = resolve;
        _loader = loader;
    }

    /// <summary>絵が無い（まだ届いていない・商品が消えた）ときに枠へ出す頭文字（ほかの小さな絵と同じ出し方）。</summary>
    public string Initial { get; }

    /// <summary>決めた絵の場所。まだ決めていない・絵が無ければ null（試験用に出す）。</summary>
    public string? Path => _path;

    /// <summary>
    /// 絵。最初に読まれたとき、場所を裏で決めて、決まったら知らせ直す。絵そのものは裏で縮めて読み、届いたら知らせ直す
    /// （画面のスレッドで復号しない）。
    /// </summary>
    public BitmapSource? Image
    {
        get
        {
            if (_state == State.Unresolved)
            {
                _state = State.Resolving;
                ResolveAsync().Forget();
                return null;
            }

            return _path is { } path && _loader is not null
                ? _loader.PeekForFill(path, EdgeDip, () => OnPropertyChanged(nameof(Image)))
                : null;
        }
    }

    private async Task ResolveAsync()
    {
        try
        {
            _path = await _resolve();
        }
        finally
        {
            _state = State.Resolved;
        }

        OnPropertyChanged(nameof(Image));
    }

    /// <summary>
    /// 商品の絵の決め方は検索のカードと同じ（BOOTH の並び・自分で足した絵・★の指名・設定の役割。
    /// <c>ItemCardViewModel.FindRestingImagePath</c>）。絵が1枚も無ければ null。
    /// </summary>
    internal static string? PathOf(ItemRecord item, string imageDirectory, IReadOnlyList<string> files, ThumbnailRole role, bool showRemoved)
    {
        var ordered = ItemImageOrder.Arrange(imageDirectory, item.Booth.Images, files, item.Local.UserImages, showRemoved);
        return ItemImageOrder.Thumbnail(ordered, item.Local.ThumbnailImage, role, item.Local.ImageRoles);
    }
}
