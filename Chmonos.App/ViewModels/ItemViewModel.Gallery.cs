using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using BoothZipInspector;

namespace Chmonos.App.ViewModels;

/// <summary>商品ページ：ギャラリー（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    public int OrphanedImageCount => Images.Count(image => image.IsOrphaned);

    public bool HasOrphanedImages => OrphanedImageCount > 0;

    public string OrphanedImageText => $"BOOTHから削除され、手元に残っている画像 {OrphanedImageCount} 枚";

    public int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            if (value < 0 || value >= Images.Count || value == _selectedIndex)
            {
                return;
            }

            Images[_selectedIndex].IsSelected = false;
            SetField(ref _selectedIndex, value);
            Images[_selectedIndex].IsSelected = true;

            // 右クリックのメニューは「いま見ている1枚」で中身が変わる。
            // ここで知らせないと、絵を送ってもメニューが前の絵のままになる
            NoteGalleryChanged();
        }
    }

    /// <summary>
    /// 大きく出す1枚。一覧の方は小さく縮めたものなので、ここは保存された大きさで読み直す（キャッシュに乗る）。
    /// 末尾の「＋」は画像ではないので何も出さない。
    /// </summary>
    /// <remarks>
    /// 裏で読み、読み終わるまでは一覧の小さな絵を引き伸ばして出しておく（灰色に戻すと、なぞって送るたびにちらつく）。
    /// 前は画面のスレッドで原寸をその場で読み、絵を送るたびに1枚ぶん止まっていた
    /// </remarks>
    public BitmapSource? SelectedImage => Images.Count == 0 || Images[SelectedIndex] is not { IsImage: true } selected
        ? null
        : _thumbnails.PeekFull(selected.Path, () => NoteFullImageLoaded(selected)) ?? selected.Image;

    /// <summary>原寸が届いた。まだその絵を見ているときだけ描き直させる（送った後に届いた前の絵で上書きしない）。</summary>
    private void NoteFullImageLoaded(GalleryImage image)
    {
        if (Images.Count > 0 && ReferenceEquals(Images[SelectedIndex], image))
        {
            OnPropertyChanged(nameof(SelectedImage));
        }
    }

    /// <summary>何枚目か。**2枚以上のときだけ出す**（空なら丸ごと隠す。1枚の「1 / 1」や、0枚の文字の無い黒い丸は要らない・U11 と同じ決まり）。</summary>
    public string GalleryCounter => Images.Count <= 1 ? string.Empty : $"{SelectedIndex + 1} / {Images.Count}";

    /// <summary>画像が1枚も無いとき、大きい絵の所に出す（ユーザ指示 2026-09-14：無いことが分かるようにする）。</summary>
    public string GalleryEmptyText => Images.Count > 0 ? string.Empty : "この商品の画像はまだありません。\n「＋」で自分の画像を追加できます。";

    /// <summary>サムネイル一覧にマウスを乗せるだけで切り替えるか。設定で変えられる。</summary>
    public bool SwitchOnHover => _services.Settings.GallerySwitchOnHover;

    /// <summary>乗ってから切り替わるまでの滞留時間（ミリ秒）。通過しただけでは切り替えないための間。</summary>
    public int HoverDelayMs => Math.Max(0, _services.Settings.GalleryHoverDelayMs);

    /// <summary>
    /// サムネイル一覧のホバーでメイン画像を切り替える。
    /// マウスが一覧から離れても戻さない。最後に見た画像がそのまま残る方が、
    /// 大きい画像をじっくり見るときに扱いやすいため。
    /// </summary>
    public void HoverImage(GalleryImage image)
    {
        if (SwitchOnHover)
        {
            SelectImage(image);
        }
    }

    /// <summary>まだ落としていない画像の枚数。0なら優先ボタンを出さない。</summary>
    public int MissingImageCount
    {
        get => _missingImageCount;
        private set
        {
            if (SetField(ref _missingImageCount, value))
            {
                OnPropertyChanged(nameof(HasMissingImages));
                OnPropertyChanged(nameof(MissingImageText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasMissingImages => MissingImageCount > 0 && !IsFetchingImages;

    public string MissingImageText => $"未取得の画像が {MissingImageCount} 枚あります";

    public bool IsFetchingImages
    {
        get => _isFetchingImages;
        private set
        {
            if (SetField(ref _isFetchingImages, value))
            {
                OnPropertyChanged(nameof(HasMissingImages));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private int _missingImageCount;
    private int _unavailableImageCount;
    private bool _isFetchingImages;

    /// <summary>
    /// 404で取れなかった枚数。
    ///
    /// 出すのは、**ギャラリーの枚数がBOOTHと合わない理由が、それ以外に説明できない**から。
    /// 商品を取り直せば印は消えるので、この行も自然に消える。
    /// </summary>
    public int UnavailableImageCount
    {
        get => _unavailableImageCount;
        private set
        {
            if (SetField(ref _unavailableImageCount, value))
            {
                OnPropertyChanged(nameof(HasUnavailableImages));
                OnPropertyChanged(nameof(UnavailableImageText));
            }
        }
    }

    public bool HasUnavailableImages => UnavailableImageCount > 0;

    public string UnavailableImageText => $"{UnavailableImageCount} 枚は取得できませんでした。商品情報を取り直すと、もう一度試します。";

    /// <summary>
    /// この商品の画像を行列の先頭で取る。
    ///
    /// 自動で「見えたものを優先」にしないのは、間隔が1,500msで1分あたり40枚しか
    /// 取れないため。勢いよくスクロールされると順番待ちが「もう見ていないもの」で埋まる。
    /// **押した意思の方が、見えたという事実より確か。**
    /// </summary>
    private async Task FetchImagesAsync()
    {
        IsFetchingImages = true;

        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.FetchItemImages(Item.Id));

            RunOnUiThread(() =>
            {
                if (result is CommandResult.ImagesFetched fetched)
                {
                    // 落とせた枚数ではなく、実際にディスクにある枚数で数え直す。
                    // 取れなかったものが残っていれば、その事実がそのまま出る
                    _thumbnails.ForgetDirectory(_services.Paths.ItemImagesDir(Item.Id));
                    BuildGallery();

                    ImageFetchNotice = fetched.Downloaded == 0
                        ? "取得できた画像はありませんでした。"
                        : $"{fetched.Downloaded} 枚を取得しました。";
                }
                else if (result is CommandResult.Failed failure)
                {
                    ImageFetchNotice = failure.Message;
                }
            });
        }
        finally
        {
            RunOnUiThread(() => IsFetchingImages = false);
        }
    }

    private string? _imageFetchNotice;

    public string? ImageFetchNotice
    {
        get => _imageFetchNotice;
        private set
        {
            if (SetField(ref _imageFetchNotice, value))
            {
                OnPropertyChanged(nameof(HasImageFetchNotice));
            }
        }
    }

    public bool HasImageFetchNotice => !string.IsNullOrEmpty(ImageFetchNotice);

    private void BuildGallery()
    {
        // 組み直すたびに消す。**足したあとに呼ばれるので、消さないと二重に並ぶ**
        Images.Clear();
        _selectedIndex = 0;

        var directory = _services.Paths.ItemImagesDir(Item.Id);
        var onDisk = _thumbnails.ListFiles(directory);

        // BOOTHが持っている枚数と、手元にあるもの＋取れないと分かったものの差。
        // フラグを持たないのは、実態とフラグがずれたときにどちらが正しいか分からなくなるため。
        //
        // 404だったものを引かないと、押しても何も起きないボタンを出し続けることになる。
        // **押しても何も起きないボタンは、壊れていると読まれる。**
        UnavailableImageCount = _services.Images.CountMissingMarkers(Item.Id);
        MissingImageCount = Math.Max(0, Item.Booth.Images.Count - onDisk.Count - UnavailableImageCount);

        // 並べ替えは共有の規則に任せる（カードと編集画面でも同じ順になる）
        var ordered = ItemImageOrder.Arrange(
            directory, Item.Booth.Images, onDisk, Item.Local.UserImages);

        foreach (var entry in ordered)
        {
            var fileName = System.IO.Path.GetFileName(entry.Path);

            var tile = new GalleryImage
            {
                Path = entry.Path,
                FileName = fileName,
                Number = Images.Count + 1,
                IsOrphaned = entry.IsOrphaned,
                IsUserAdded = entry.IsUserAdded,
                IsPinned = string.Equals(
                    fileName, Item.Local.ThumbnailImage, StringComparison.OrdinalIgnoreCase),
                Role = Core.Images.ItemImageOrder.RoleOf(entry, Item.Local.ImageRoles),
            };
            tile.LoadTile(_thumbnails);
            Images.Add(tile);
        }

        // 最初に出すのはサムネイルに指名した1枚。無ければ並びの1枚目。
        // 検索カードに出ている絵と、開いたときに見える絵を揃える
        if (Images.Count > 0)
        {
            var pinned = Images.FirstOrDefault(image => image.IsPinned);
            _selectedIndex = pinned is null ? 0 : Images.IndexOf(pinned);
            Images[_selectedIndex].IsSelected = true;
        }

        // 一覧に出すのは画像＋末尾の「足す」枠。
        // 数える側（何枚目／何枚）は Images だけを見るので、枠は混ざらない
        GalleryTiles.Clear();
        foreach (var image in Images)
        {
            GalleryTiles.Add(image);
        }

        GalleryTiles.Add(new GalleryImage
        {
            Path = string.Empty,
            FileName = string.Empty,
            Image = null,
            IsAddTile = true,
        });

        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(GalleryCounter));
        OnPropertyChanged(nameof(HasOrphanedImages));
        OnPropertyChanged(nameof(OrphanedImageText));

        Attributes = Item.Local.Attributes
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new AttributeBar { Name = pair.Key, Value = pair.Value })
            .ToList();

        BuildAvatars();
    }

    // ---- ギャラリーの操作 ----
    //
    // 閲覧は矢印、操作は右クリックのメニューに分けてある。
    // サムネイルは68×54しかないので、そこにボタンを乗せると絵が見えなくなる。

    /// <summary>いま大きく出ている1枚。右クリックのメニューはこれに対して働く。</summary>
    public GalleryImage? CurrentImage
        => Images.Count == 0 || SelectedIndex >= Images.Count ? null : Images[SelectedIndex];

    /// <summary>
    /// 送れるか。**端で止めず、最初と最後をつなぐ。**
    /// 端で止めると「もう無い」のか「押せていない」のかが分からない。
    /// 1枚しか無ければ送る先が無いので出さない。
    /// </summary>
    public bool CanGoPreviousImage => Images.Count > 1;

    public bool CanGoNextImage => Images.Count > 1;

    /// <summary>自分で足した画像か。並べ替えと削除はこれにだけ出す。</summary>
    public bool CurrentIsUserAdded => CurrentImage is { IsUserAdded: true };

    /// <summary>いま出ている1枚がサムネイルに指名されているか。</summary>
    public bool CurrentIsPinned => CurrentImage is { IsPinned: true };

    // ギャラリーの部品を改変と分け合うための値（改変の写真にはサムネイルの指名も役割も無い）
    public bool ShowsPinThumbnail => !CurrentIsPinned;

    public bool ShowsUnpinThumbnail => CurrentIsPinned;

    public bool ShowsImageRoles => true;

    public string AddImageTip => "この商品に画像を追加";

    // ---- 画像の役割 ----

    /// <summary>メニューの見出し。いま何が付いているかを見出しに出す。</summary>
    public string CurrentRoleHeader => CurrentImage is { } image
        ? $"役割：{Core.Models.ImageRoles.Label(image.Role)}"
        : "役割";

    public bool CurrentIsRoleBooth => CurrentImage is { Role: Core.Models.ImageRole.Booth };

    public bool CurrentIsRoleModified => CurrentImage is { Role: Core.Models.ImageRole.Modified };

    public bool CurrentIsRoleOther => CurrentImage is { Role: Core.Models.ImageRole.Other };

    /// <summary>
    /// いま出ている1枚に役割を付ける。
    ///
    /// 付けたら**一覧のサムネイルも変わりうる**ので、ライブラリを読み直す。
    /// </summary>
    private async Task SetRoleAsync(Core.Models.ImageRole role)
    {
        if (CurrentImage is not { IsImage: true } image)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetImageRole(
            Item.Id,
            image.FileName,
            role,
            image.IsUserAdded));

        await ReloadGalleryAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 前へ動かせるか。**自分の画像の中だけで動く。**
    /// BOOTHの画像は並べ替えない（観測した並びが正）。
    /// </summary>
    public bool CanMoveImageBack => CurrentIsUserAdded && UserImageIndex > 0;

    public bool CanMoveImageForward
        => CurrentIsUserAdded && UserImageIndex >= 0 && UserImageIndex < Item.Local.UserImages.Count - 1;

    /// <summary>いま出ている画像が、自分の画像の何番目か。自分の画像でなければ -1。</summary>
    private int UserImageIndex => CurrentImage is not { IsUserAdded: true } current
        ? -1
        : Item.Local.UserImages
            .Select((image, index) => (image.FileName, index))
            .FirstOrDefault(pair => string.Equals(
                pair.FileName, current.FileName, StringComparison.OrdinalIgnoreCase), (null!, -1))
            .index;

    /// <summary>見る絵を送る。最後の次は最初へ、最初の前は最後へ回る。</summary>
    private void GoToImage(int delta)
    {
        if (Images.Count <= 1)
        {
            return;
        }

        SelectedIndex = ((SelectedIndex + delta) % Images.Count + Images.Count) % Images.Count;
    }

    /// <summary>並べ替えたあと、同じ絵を選んだままにする。動かした先を目で追えるように。</summary>
    private async Task MoveImageAsync(int delta)
    {
        if (CurrentImage is not { IsUserAdded: true } current)
        {
            return;
        }

        var fileName = current.FileName;
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.MoveUserImage(Item.Id, fileName, delta));

        if (result is CommandResult.Failed failed)
        {
            RefreshStatus = failed.Message;
            return;
        }

        await ReloadImagesAsync(fileName);
    }

    /// <summary>サムネイルに指名する／指名を外す。**BOOTHの画像も指名できる。**</summary>
    private async Task PinThumbnailAsync(bool pin)
    {
        if (CurrentImage is not { } current)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.PinThumbnail(Item.Id, pin ? current.FileName : null));

        if (result is CommandResult.Failed failed)
        {
            RefreshStatus = failed.Message;
            return;
        }

        RefreshStatus = pin
            ? "この画像をサムネイルにしました。"
            : "サムネイルの指名を外しました。";

        await ReloadImagesAsync(current.FileName);

        // 検索のカードは読み込んだ写しを持っている。読み直さないと、
        // 指名したのにカードの絵が変わらない
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 自分で足した画像を消す。**取り返しがつかないので確かめる。**
    ///
    /// 「元のファイルには触りません」とは書かない——貼り付けたスクリーンショットには
    /// 元のファイルが無いので、場合によって嘘になる。
    /// </summary>
    private async Task RemoveImageAsync()
    {
        if (CurrentImage is not { IsUserAdded: true } current)
        {
            return;
        }

        var answer = Services.Notice.Show(
            "この画像を削除します。\n\n"
            + "元に戻せません。元のファイルが手元にあれば、もう一度追加できます。",
            "画像を削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveUserImage(Item.Id, current.FileName));

        if (result is CommandResult.Failed failed)
        {
            RefreshStatus = failed.Message;
            return;
        }

        await ReloadImagesAsync(null);
    }

    /// <summary>ファイルを選んで足す。落とす・貼るのほかに、選ぶ道も残しておく。</summary>
    private async Task AddImageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "この商品に追加する画像を選ぶ",
            Filter = "画像 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await AddImageFilesAsync(dialog.FileNames);
    }

    /// <summary>
    /// 画像のファイルを足す。落とした場合と選んだ場合で同じ道を通す。
    /// </summary>
    public async Task AddImageFilesAsync(IReadOnlyList<string> paths)
    {
        var added = 0;
        string? last = null;

        foreach (var path in paths)
        {
            byte[] bytes;
            try
            {
                bytes = await System.IO.File.ReadAllBytesAsync(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                RefreshStatus = $"{System.IO.Path.GetFileName(path)} を読めませんでした。";
                continue;
            }

            if (await AddImageBytesAsync(bytes) is { } fileName)
            {
                added++;
                last = fileName;
            }
            else
            {
                RefreshStatus = $"{System.IO.Path.GetFileName(path)} は画像として読めませんでした。";
            }
        }

        if (added > 0)
        {
            RefreshStatus = $"画像を {added} 枚追加しました。";
            await ReloadImagesAsync(last);
        }
    }

    /// <summary>画像の中身を1枚足す。貼り付けもここを通る。</summary>
    public async Task<string?> AddImageBytesAsync(byte[] bytes)
    {
        var result = await _services.Commands.ExecuteAsync(new UiCommand.AddUserImage(Item.Id, bytes));

        return result is CommandResult.UserImageAdded added ? added.FileName : null;
    }

    /// <summary>落として／貼って足したあとに、外から組み直させる。</summary>
    public Task ReloadGalleryAsync() => ReloadImagesAsync(null);

    /// <summary>
    /// 裏の取得（取り込みの④⑤・使っていない間の取得・期限の取り直し）が
    /// この商品の画像を置いた（UIスレッドで呼ばれる）。
    ///
    /// **見ていた絵は選んだまま組み直す。**画像が1枚届くたびに先頭へ戻されると、
    /// 取得の最中は落ち着いて見られない。
    /// </summary>
    public void NoteImagesSaved(string itemId)
    {
        if (!string.Equals(itemId, Item.Id, StringComparison.Ordinal))
        {
            return;
        }

        var keepFileName = _selectedIndex < Images.Count ? Images[_selectedIndex].FileName : null;

        BuildGallery();

        if (keepFileName is not null
            && Images.FirstOrDefault(image =>
                string.Equals(image.FileName, keepFileName, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            SelectedIndex = Images.IndexOf(found);
        }

        NoteGalleryChanged();
    }

    /// <summary>
    /// 記録を読み直してギャラリーを組み直す。
    /// <paramref name="keepFileName"/> を渡すと、その絵を選んだままにする。
    /// </summary>
    private async Task ReloadImagesAsync(string? keepFileName)
    {
        if (await _services.Store.Items.LoadAsync(Item.Id) is not { } reloaded)
        {
            return;
        }

        Item = reloaded;

        // 足した／消した直後なので、フォルダの写しを捨ててから読み直す
        _thumbnails.ForgetDirectory(_services.Paths.ItemImagesDir(Item.Id));
        BuildGallery();

        // 検索のカードは読み込んだ写しを持っている。知らせないと、消した画像のファイルを指したまま残り、
        // 足した画像もカードに出なかった（点検 2026-09-29）。フォルダの写しを捨てた後に渡すので、カードは今の画像で作り直る
        _main.Search.NoteItemChanged(reloaded);

        if (keepFileName is not null)
        {
            var found = Images.FirstOrDefault(image =>
                string.Equals(image.FileName, keepFileName, StringComparison.OrdinalIgnoreCase));

            if (found is not null)
            {
                SelectedIndex = Images.IndexOf(found);
            }
        }

        NoteGalleryChanged();
    }

    /// <summary>ギャラリーまわりの見た目をまとめて更新する。</summary>
    private void NoteGalleryChanged()
    {
        foreach (var name in new[]
        {
            nameof(SelectedImage), nameof(GalleryCounter), nameof(GalleryEmptyText), nameof(CurrentImage),
            nameof(CanGoPreviousImage), nameof(CanGoNextImage),
            nameof(CurrentIsUserAdded), nameof(CurrentIsPinned),
            nameof(ShowsPinThumbnail), nameof(ShowsUnpinThumbnail),
            nameof(CurrentRoleHeader), nameof(CurrentIsRoleBooth),
            nameof(CurrentIsRoleModified), nameof(CurrentIsRoleOther),
            nameof(CanMoveImageBack), nameof(CanMoveImageForward),
            nameof(HasOrphanedImages), nameof(OrphanedImageText),
            nameof(HasUserImages), nameof(UserImageText),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    public bool HasUserImages => Images.Any(image => image.IsUserAdded);

    public string UserImageText
        => $"自分で追加した画像 {Images.Count(image => image.IsUserAdded)} 枚";
    private void SelectImage(object? parameter)
    {
        if (parameter is GalleryImage image)
        {
            var index = Images.IndexOf(image);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }
    }
}
