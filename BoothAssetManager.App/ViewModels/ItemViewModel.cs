using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

public sealed class GalleryImage : ViewModelBase
{
    private bool _isSelected;

    public required string Path { get; init; }

    public required BitmapSource? Image { get; init; }

    /// <summary>BOOTH側の一覧から消えた画像。手元には残しておく。</summary>
    public bool IsOrphaned { get; init; }

    /// <summary>今メインに出ている画像か。一覧のどれを見ているか分かるようにする。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }
}

public sealed class VariationRow
{
    public required string Name { get; init; }

    public required string PriceText { get; init; }

    public bool IsPurchased { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }
}

/// <summary>フォルダとして所有している1件。中身は個別に記録していない。</summary>
public sealed class LocalFolderRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string SummaryText { get; init; }

    /// <summary>登録した場所に今もあるか。無ければ指し直しが要る。</summary>
    public bool IsMissing { get; init; }

    /// <summary>
    /// 対応するzipが手元に入ったか。入っていればフォルダ登録は役目を終えている。
    /// 放っておくと容量が二重に数えられるので、その場で気付けるようにする。
    /// </summary>
    public bool HasArchive { get; init; }

    public string ArchiveNoticeText { get; init; } = string.Empty;
}

public sealed class LocalFileRow
{
    /// <summary>このファイルの同一性。商品から外すときに指す。</summary>
    public required string Hash { get; init; }

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public string? VariationLabel { get; init; }

    public bool HasVariationLabel => VariationLabel is not null;

    /// <summary>同じ中身が複数箇所にある状態。容量は1回しか数えない。</summary>
    public bool HasMultiplePaths => Paths.Count > 1;

    public string DuplicateNote => $"{Paths.Count}箇所に同じ実体";

    public bool IsMissing => Paths.Count == 0;
}

/// <summary>
/// 商品ページ。BOOTHの商品ページを参考にしつつ、ローカルの情報から組み立てる。
/// 閲覧専用にしているのは決定事項（編集はEdit画面へ一本化し、保存経路を1つに保つ）。
/// </summary>
public sealed class ItemViewModel : ViewModelBase, IInAppLinkNavigator
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;
    private readonly (string Label, Action Go)? _back;
    private int _selectedIndex;

    /// <param name="back">
    /// 戻り先。ショップから来たならショップへ戻したいので、呼び出し側から受け取る。
    /// 指定が無ければ検索（ほとんどの経路がそちらなので、既定にしておく）。
    /// </param>
    public ItemViewModel(
        ItemRecord item,
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails,
        (string Label, Action Go)? back = null)
    {
        Item = item;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        _back = back;
        BackText = back is { } destination ? $"← {destination.Label}に戻る" : "← 検索に戻る";
        BackCommand = new RelayCommand(() => (back?.Go ?? main.ShowSearch)());

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing);

        // 作者名からはアプリ内のショップ画面へ送る（BOOTHへは「BOOTHで開く」がある）。
        // 戻り先はこの商品ページにする。ショップ一覧へ返すと、来た道と違う場所に出てしまう
        OpenShopCommand = new RelayCommand(
            () => _ = main.ShowShopAsync(
                item.Booth.Shop!.Subdomain,
                (item.Booth.Name ?? "商品", () => main.ShowItem(item, back))),
            () => item.Booth.Shop is not null);
        OpenBoothCommand = new RelayCommand(OpenBooth);
        // 一度userTagを付けたitemは既定の編集キューに載らないので、ここから開く経路が要る
        EditCommand = new RelayCommand(() => _ = main.ShowEditAsync([item.Id]));
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, parameter => parameter is string);
        UnregisterFolderCommand = new RelayCommand(
            parameter => _ = UnregisterFolderAsync(parameter as string),
            parameter => parameter is string);
        DetachFileCommand = new RelayCommand(
            parameter => _ = DetachFileAsync(parameter as LocalFileRow),
            parameter => parameter is LocalFileRow);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);
        FetchImagesCommand = new RelayCommand(() => _ = FetchImagesAsync(), () => HasMissingImages);
        AddUsedOnCommand = new RelayCommand(parameter => _ = AddUsedOnAsync(parameter as string));
        AddAvatarCommand = new RelayCommand(parameter => _ = AddAvatarAsync(parameter as string));

        BuildGallery();
        BuildVariations();
        BuildLocalFiles();
        BuildLocalFolders();
    }

    public ItemRecord Item { get; private set; }

    public RelayCommand BackCommand { get; }

    public string BackText { get; }

    public RelayCommand OpenShopCommand { get; }

    /// <summary>
    /// この商品を今すぐ取り直す。
    ///
    /// 普段は次回取得予定を待つが、外部で更新を知ったときに待てないことがある。
    /// 走るのは普段と同じ処理（商品JSON → 説明文 → 画像 → ショップのアイコン）で、
    /// 違うのは予定日を待たずに始める点だけ。
    /// </summary>
    public RelayCommand RefreshCommand { get; }

    private bool _isRefreshing;
    private string _refreshStatus = string.Empty;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetField(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 何を取り直すのかを名前に入れる。
    ///
    /// 「今すぐ取り直す」は動作しか言っておらず、**何の話か分からない**。
    /// 隣が「この商品を編集」「BOOTHで開く」と対象を名乗っているので、ここだけ浮いていた。
    ///
    /// 「画像再取得」にしないのは狭すぎるため。実際に取り直すのは商品情報が主で、
    /// 名前・価格・バリエーション・タグ・販売状況・説明文が入れ替わる。画像はその後に続く。
    /// 「BOOTHから」と言うことで、**自分で入れたものは変わらない**ことも同時に伝わる。
    /// </summary>
    public string RefreshButtonText => IsRefreshing ? "取り直しています…" : "BOOTHから取り直す";

    public string RefreshStatus
    {
        get => _refreshStatus;
        private set
        {
            if (SetField(ref _refreshStatus, value))
            {
                OnPropertyChanged(nameof(HasRefreshStatus));
            }
        }
    }

    public bool HasRefreshStatus => RefreshStatus.Length > 0;

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        RefreshStatus = string.Empty;

        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.RefreshItem(Item.Id));

            if (result is CommandResult.Failed failure)
            {
                RunOnUiThread(() => RefreshStatus = failure.Message);
                return;
            }

            // 取り直しでは画像が増える（印が外れて取れるようになったものも含む）。
            // 画像フォルダの一覧は覚えているので、捨てないと増えたぶんが出てこない
            _thumbnails.ForgetDirectory(_services.Paths.ItemImagesDir(Item.Id));

            // 取り直した中身で開き直す。画像の枚数が変わることもあるので、画面ごと作り直す
            var updated = await _services.Store.Items.LoadAsync(Item.Id);
            RunOnUiThread(() =>
            {
                if (updated is not null)
                {
                    _main.ShowItem(updated, _back);
                }
            });
        }
        catch (Exception exception) when (exception is IOException or System.Net.Http.HttpRequestException)
        {
            RunOnUiThread(() => RefreshStatus = "取得できませんでした。時間をおいて試してください。");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>着せているアバターを足す。候補から選ぶ（登録簿に無い名前は受け取らない）。</summary>
    public RelayCommand AddUsedOnCommand { get; }

    /// <summary>対応アバターを手で足す。検出が拾えなかったときの補い。</summary>
    public RelayCommand AddAvatarCommand { get; }

    /// <summary>「対応アバターを足す」の候補。既に宣言されているものは出さない。</summary>
    public IReadOnlyList<string> SupportSuggestions { get; private set; } = [];

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand OpenInExplorerCommand { get; }

    public RelayCommand UnregisterFolderCommand { get; }

    /// <summary>
    /// フォルダの紐付けを解除する。ファイルには触らない。
    /// zipを後から手に入れると、展開先は自動で対象外になるが登録は残り、容量が二重に乗る。
    /// </summary>
    private async Task UnregisterFolderAsync(string? folderPath)
    {
        if (folderPath is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダの紐付けを解除します。\n\n{folderPath}\n\n"
            + "ファイルは消しません。以降このフォルダの中もスキャン対象に戻ります。",
            "フォルダの登録を解除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.UnregisterFolder(Item.Id, folderPath));

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            _main.ShowItem(reloaded);
        }
    }

    public RelayCommand DetachFileCommand { get; }

    /// <summary>
    /// ファイルをこの商品から外して未確定へ戻す。間違って紐付いたものを直す唯一の道。
    ///
    /// **IDを書き換える形にはしない。**商品IDはファイル名にもフォルダ名にもなっていて、
    /// 対応アバターの宣言など他所からも参照されている。書き換えると参照が迷子になる。
    /// 「このファイルの行き先が違う」が本当にやりたいことなので、ファイルの側を動かす。
    /// </summary>
    private async Task DetachFileAsync(LocalFileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"{row.FileName} をこの商品から外します。\n\n"
            + "ファイルは消しません。未確定に戻るので、そこで正しい商品を選び直せます。\n"
            + "次の取り込みでこの商品に戻ることもありません。",
            "この商品から外す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        // 手元に何も無くなるときだけ、商品を残すか聞く。
        // まだ他が残っていれば所持のままなので、聞くことが無い
        var deleteWhenEmpty = false;
        if (LocalFiles.Count == 1 && LocalFolders.Count == 0)
        {
            var keep = System.Windows.MessageBox.Show(
                "これが最後のファイルなので、この商品は手元に何も無い状態になります。\n\n"
                + "「はい」で商品の情報を残します（価格やタグは見られます。贈った商品と同じ扱いです）。\n"
                + "「いいえ」でこの商品を消します。メモや分類も一緒に消えます。",
                "商品を残しますか",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.Yes);

            deleteWhenEmpty = keep == System.Windows.MessageBoxResult.No;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.DetachFile(Item.Id, row.Hash, deleteWhenEmpty));

        if (result is CommandResult.FileDetached { Outcome: Core.Services.DetachOutcome.ItemDeleted })
        {
            // 開いていた商品が消えたので、戻る先は検索。一覧からも消えている必要がある
            await _main.ReloadLibraryAsync();
            _main.ShowSearch();
            return;
        }

        // 未確定が1件増えるので、ナビの件数を数え直す
        _main.RefreshBadges();

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            _main.ShowItem(reloaded);
        }
    }

    public RelayCommand SelectImageCommand { get; }

    /// <summary>この商品の画像を行列の先頭で取る。未取得があるときだけ押せる。</summary>
    public RelayCommand FetchImagesCommand { get; }

    public ObservableCollection<GalleryImage> Images { get; } = [];

    public ObservableCollection<VariationRow> Variations { get; } = [];

    public ObservableCollection<LocalFileRow> LocalFiles { get; } = [];

    public string Name => Item.Booth.Name ?? Item.Id;

    public string ShopName => Item.Booth.Shop?.Name ?? "(ショップ不明)";

    public string ShopSubdomain => Item.Booth.Shop?.Subdomain ?? string.Empty;

    public string CategoryText => Item.Booth.Category is null
        ? string.Empty
        : Item.Booth.Category.ParentName is null
            ? Item.Booth.Category.Name
            : $"{Item.Booth.Category.ParentName} / {Item.Booth.Category.Name}";

    public string IdText => $"ID {Item.Id}";

    public string PublishedText => Item.Booth.PublishedAt is null
        ? string.Empty
        : $"公開 {Item.Booth.PublishedAt:yyyy-MM-dd}";

    public string WishText => $"♡ {Item.Booth.WishListsCount:N0}";

    public IReadOnlyList<string> Tags => Item.Booth.Tags;

    public IReadOnlyList<UserTagAssignment> UserTags => Item.Local.UserTags;

    public bool HasUserTags => Item.Local.UserTags.Count > 0;

    public IReadOnlyList<H2Section> Sections => Item.Booth.H2Sections;

    public bool HasSections => Item.Booth.H2Sections.Count > 0;

    public string? Description => Item.Booth.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Booth.Description);

    public string? Memo => Item.Local.Memo;

    public bool HasMemo => !string.IsNullOrWhiteSpace(Item.Local.Memo);

    /// <summary>出品者が宣言している対応アバター。こちらは編集しない。</summary>
    public IReadOnlyList<AvatarRow> Avatars { get; private set; } = [];

    public bool HasAvatars => Avatars.Count > 0;

    /// <summary>この商品が名指ししている共通素体。</summary>
    public IReadOnlyList<string> AvatarBases { get; private set; } = [];

    public bool HasAvatarBases => AvatarBases.Count > 0;

    /// <summary>
    /// 自分が実際に着せているアバター。出品者の宣言とは別に持つ。
    /// 非対応衣装を着せることがあるので、宣言に無いアバターも入れられる。
    /// </summary>
    public ObservableCollection<AvatarUsageRow> UsedOn { get; } = [];

    public bool HasUsedOn => UsedOn.Count > 0;

    /// <summary>入力の候補。所有しているアバターを先に出す。</summary>
    public IReadOnlyList<string> AvatarSuggestions { get; private set; } = [];

    public string AvatarSectionNote => HasAvatars || HasAvatarBases
        ? "出品者が対応と書いているアバターです。"
        : "出品者の対応表明は見つかっていません。アバターの管理から検出できます。";

    public IReadOnlyList<AttributeBar> Attributes { get; private set; } = [];

    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>フォルダとして所有しているもの。zipが残っていない展開済みの配布物。</summary>
    public IReadOnlyList<LocalFolderRow> LocalFolders { get; private set; } = [];

    public bool HasLocalFolders => LocalFolders.Count > 0;

    public string FileSummary => Item.IsDownloaded
        ? $"{Item.Local.LocalFiles.Count} 件 / {FormatSize(Item.LogicalSizeBytes)}"
        : "ファイルなし";

    /// <summary>
    /// 入手日。手入力が無ければファイルの日付で代え、代えたことを明記する。
    /// 補った値を手入力と同じ顔で出すと、記録として信用できなくなる。
    /// </summary>
    public string AcquiredText
    {
        get
        {
            var acquired = AcquiredDateResolver.Resolve(Item);
            if (acquired.Value is not { } date)
            {
                return "-";
            }

            return acquired.IsFallback ? $"{date:yyyy-MM-dd}（ファイルの日付）" : date.ToString("yyyy-MM-dd");
        }
    }

    public string LastFetchedText => Item.Local.LastFetchedAt is null
        ? "-"
        : Item.Local.LastFetchedAt.Value.ToString("yyyy-MM-dd");

    public string NextFetchText => Item.Local.NextFetchDueAt is null
        ? "-"
        : Item.Local.NextFetchDueAt.Value.ToString("yyyy-MM-dd");

    public bool NotifyOnUpdate => Item.Local.NotifyOnUpdate;

    public int OrphanedImageCount => Images.Count(image => image.IsOrphaned);

    public bool HasOrphanedImages => OrphanedImageCount > 0;

    public string OrphanedImageText => $"BOOTHから削除された画像 {OrphanedImageCount} 枚（手元には残っています）";

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

            OnPropertyChanged(nameof(SelectedImage));
            OnPropertyChanged(nameof(GalleryCounter));
        }
    }

    public BitmapSource? SelectedImage => Images.Count == 0 ? null : Images[SelectedIndex].Image;

    public string GalleryCounter => Images.Count == 0 ? string.Empty : $"{SelectedIndex + 1} / {Images.Count}";

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

    public string UnavailableImageText => $"{UnavailableImageCount} 枚は取得できませんでした（商品を取り直すともう一度試します）";

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
                    Images.Clear();
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
        foreach (var entry in ItemImageOrder.Arrange(directory, Item.Booth.Images, onDisk))
        {
            Images.Add(new GalleryImage
            {
                Path = entry.Path,
                Image = _thumbnails.Load(entry.Path),
                IsOrphaned = entry.IsOrphaned,
            });
        }

        if (Images.Count > 0)
        {
            Images[0].IsSelected = true;
        }

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

    /// <summary>
    /// 対応アバターまわりを組み立てる。
    ///
    /// 出品者の宣言（Avatars / AvatarBases）と、自分が着せている記録（UsedOn）を分けて出す。
    /// 混ぜると「誰が言っていることなのか」が分からなくなる。
    /// </summary>
    private void BuildAvatars()
    {
        var registry = _services.Store.Avatars.Load();
        var names = registry.Entries.ToDictionary(
            entry => entry.ItemId,
            entry => entry.DisplayName ?? entry.BoothName ?? entry.ItemId,
            StringComparer.Ordinal);

        string NameOf(string id, string? cached)
            => names.TryGetValue(id, out var name) ? name : cached ?? id;

        Avatars = Item.Local.Avatars
            .Where(link => !link.Rejected)
            .Select(link => new AvatarRow
            {
                ItemId = link.AvatarItemId,
                Name = NameOf(link.AvatarItemId, link.Name),
                SourceText = SourceLabel(link.Source),
                IsUnconfirmed = !link.Confirmed,
                RejectCommand = new RelayCommand(() => _ = RejectAvatarAsync(link.AvatarItemId)),
            })
            .ToList();

        AvatarBases = Item.Local.AvatarBases
            .Where(link => !link.Rejected)
            .Select(link => link.BaseName)
            .ToList();

        UsedOn.Clear();
        foreach (var usage in Item.Local.UsedOn)
        {
            var id = usage.AvatarItemId;
            UsedOn.Add(new AvatarUsageRow
            {
                ItemId = id,
                Name = NameOf(id, null),
                Note = usage.Note ?? string.Empty,
                RemoveCommand = new RelayCommand(() => _ = RemoveUsedOnAsync(id)),
            });
        }

        // 対応アバターの候補。既に宣言されているものは出さない
        var declared = Avatars.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        SupportSuggestions = registry.Entries
            .Where(entry => AvatarService.IsAvatar(entry) && !declared.Contains(entry.ItemId))
            .Select(entry => entry.DisplayName ?? entry.BoothName ?? entry.ItemId)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        OnPropertyChanged(nameof(SupportSuggestions));

        var already = UsedOn.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);

        // 候補は手元にあるアバターを先に。実際に着せる相手は自分の持ち物であることが多い
        AvatarSuggestions = registry.Entries
            .Where(entry => AvatarService.IsAvatar(entry) && !already.Contains(entry.ItemId))
            .OrderByDescending(entry => entry.IsOwnedManually || _services.Store.Items.Exists(entry.ItemId))
            .ThenBy(entry => entry.DisplayName ?? entry.ItemId, StringComparer.CurrentCulture)
            .Select(entry => entry.DisplayName ?? entry.BoothName ?? entry.ItemId)
            .ToList();

        foreach (var name in new[]
        {
            nameof(Avatars), nameof(HasAvatars), nameof(AvatarBases), nameof(HasAvatarBases),
            nameof(HasUsedOn), nameof(AvatarSuggestions), nameof(AvatarSectionNote),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private static string SourceLabel(AvatarLinkSource source) => source switch
    {
        AvatarLinkSource.SupportSection => "対応アバター節",
        AvatarLinkSource.Tag => "タグ",
        AvatarLinkSource.Variation => "バリエーション名",
        AvatarLinkSource.H2Link => "説明文のリンク",
        AvatarLinkSource.Manual => "手入力",
        _ => string.Empty,
    };

    /// <summary>
    /// 「このアバターに着せている」を足す。名前から登録簿を引いてIDに直す。
    /// 検出は UsedOn を触らないので、ここで足したものが消えることはない。
    /// </summary>
    /// <summary>
    /// この対応は違う、と消す。
    ///
    /// 消しただけだと次の検出で復活するので、Manual に付け替えたうえで Rejected を立てる。
    /// 再検出のマージは Manual の宣言だけを残して他を作り直すので、
    /// Source を変えないと行ごと作り直されて Rejected が消える。
    ///
    /// 検出の適合率は実測で89%。1割は誤りが出るので、消せないと噛み合わない。
    /// </summary>
    private async Task RejectAvatarAsync(string avatarItemId)
    {
        var links = Item.Local.Avatars
            .Select(link => link.AvatarItemId == avatarItemId
                ? link with { Source = AvatarLinkSource.Manual, Rejected = true, Confirmed = true }
                : link)
            .ToList();

        await SaveLocalAsync(Item.Local with { Avatars = links }, LocalOwners.SupportedAvatars);
    }

    /// <summary>
    /// 対応アバターを手で足す。出品者が書き漏らしている場合や、検出が拾えなかった場合に使う。
    /// 足したものは Manual なので、次の検出でも消えない。
    /// </summary>
    private async Task AddAvatarAsync(string? name)
    {
        if (FindAvatarByName(name) is not { } match)
        {
            return;
        }

        var existing = Item.Local.Avatars.FirstOrDefault(link => link.AvatarItemId == match.ItemId);

        // 一度消したものを足し直す場合は、Rejected を下ろすだけ
        var links = existing is null
            ? [.. Item.Local.Avatars, new AvatarLink
            {
                AvatarItemId = match.ItemId,
                Name = match.DisplayName ?? match.BoothName,
                Source = AvatarLinkSource.Manual,
                Confirmed = true,
            }]
            : Item.Local.Avatars
                .Select(link => link.AvatarItemId == match.ItemId
                    ? link with { Source = AvatarLinkSource.Manual, Rejected = false, Confirmed = true }
                    : link)
                .ToList();

        await SaveLocalAsync(Item.Local with { Avatars = links }, LocalOwners.SupportedAvatars);
    }

    private AvatarRegistryEntry? FindAvatarByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _services.Store.Avatars.Load().Entries.FirstOrDefault(entry =>
            string.Equals(entry.DisplayName, name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(entry.BoothName, name, StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task AddUsedOnAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var registry = _services.Store.Avatars.Load();
        var match = registry.Entries.FirstOrDefault(entry =>
            string.Equals(entry.DisplayName, name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(entry.BoothName, name, StringComparison.CurrentCultureIgnoreCase));

        if (match is null)
        {
            return;
        }

        if (Item.Local.UsedOn.Any(usage => usage.AvatarItemId == match.ItemId))
        {
            return;
        }

        var local = Item.Local with
        {
            UsedOn = [.. Item.Local.UsedOn, new AvatarUsage { AvatarItemId = match.ItemId }],
        };

        await SaveLocalAsync(local, LocalOwners.Usage);
    }

    private async Task RemoveUsedOnAsync(string avatarItemId)
    {
        var local = Item.Local with
        {
            UsedOn = Item.Local.UsedOn.Where(usage => usage.AvatarItemId != avatarItemId).ToList(),
        };

        await SaveLocalAsync(local, LocalOwners.Usage);
    }

    /// <summary>
    /// この画面が決めた項目だけを書く。<paramref name="owns"/> に無い項目は、
    /// 保存の直前に読み直したものが残る。開いている間に検出や取り込みが書いたものを、
    /// 古い写しで潰さないため。
    /// </summary>
    private async Task SaveLocalAsync(LocalBlock local, IReadOnlyCollection<LocalField> owns)
    {
        await _services.Edit.SaveLocalAsync(Item.Id, local, owns);

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            Item = reloaded;
            BuildAvatars();
        }
    }

    private void BuildVariations()
    {
        // 同じ版を複数回買っていることがあるので、版ごとにまとめて回数も出す
        var ordered = Item.Local.Purchases
            .GroupBy(record => record.VariationId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var variation in Item.Booth.Variations)
        {
            var purchased = ordered.TryGetValue(variation.Id, out var group);
            Variations.Add(new VariationRow
            {
                Name = variation.Name ?? "（バリエーションなし）",
                PriceText = purchased ? PurchaseText(group!) : $"¥{variation.Price:N0}",
                IsPurchased = purchased,
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す
        var currentIds = Item.Booth.Variations.Select(variation => variation.Id).ToHashSet();
        foreach (var group in ordered.Where(pair => !currentIds.Contains(pair.Key)))
        {
            Variations.Add(new VariationRow
            {
                Name = group.Value[0].NameSnapshot ?? $"variation {group.Key}",
                PriceText = PurchaseText(group.Value),
                IsPurchased = true,
                IsGone = true,
            });
        }
    }

    /// <summary>
    /// 1つの版についての購入記録をまとめて1行にする。
    /// 同じ版を複数回買っていれば回数を出す（贈答・買い直しで起こる）。
    /// </summary>
    private static string PurchaseText(IReadOnlyList<Purchase> group)
    {
        var head = group[0];
        var kind = head.Kind switch
        {
            PurchaseKind.Received => "貰った",
            PurchaseKind.Given => "贈った",
            _ => "購入",
        };

        var price = head.Price is null ? "価格未入力" : $"¥{head.Price:N0}";
        var text = head.Price is null ? $"{price}（{kind}）" : $"{price} で{kind}";

        return group.Count > 1 ? $"{text} ほか {group.Count - 1} 件" : text;
    }

    private void BuildLocalFiles()
    {
        foreach (var file in Item.Local.LocalFiles)
        {
            var variation = file.VariationId is null
                ? null
                : Item.Booth.Variations.FirstOrDefault(entry => entry.Id == file.VariationId)?.Name;

            LocalFiles.Add(new LocalFileRow
            {
                Hash = file.Hash,
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : "(見つかりません)",
                SizeText = FormatSize(file.SizeBytes),
                Paths = file.Paths,
                VariationLabel = variation,
            });
        }
    }

    private void BuildLocalFolders()
    {
        LocalFolders = Item.Local.LocalFolders
            .Select(folder =>
            {
                // zipが手に入っていればフォルダ登録は役目を終えている。
                // 気付かずに置いておくと容量が二重に数えられる。
                var archive = RegisteredFolderSet.FindArchiveFor(folder.Path);

                return new LocalFolderRow
                {
                    Path = folder.Path,
                    Name = System.IO.Path.GetFileName(folder.Path),
                    SummaryText = $"{folder.FileCount} ファイル / {FormatSize(folder.TotalBytes)}",
                    IsMissing = !Directory.Exists(folder.Path),
                    HasArchive = archive is not null,
                    ArchiveNoticeText = archive is null
                        ? string.Empty
                        : $"{System.IO.Path.GetFileName(archive)} が見つかりました。"
                            + "そちらを取り込めば展開先は自動で対象から外れるので、この登録は解除してください。",
                };
            })
            .ToList();
    }

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

    /// <summary>
    /// 本文中のBOOTH商品リンクのうち、ライブラリに持っているものはアプリ内で開く。
    /// 対応アバターなど、説明文から辿った先が手元にある商品であることは多い。
    /// </summary>
    public bool CanNavigate(Uri uri)
    {
        var itemId = BoothUrlExtractor.TryExtractItemId(uri.AbsoluteUri);
        return itemId is not null && _services.Store.Items.Exists(itemId);
    }

    public void Navigate(Uri uri)
    {
        var itemId = BoothUrlExtractor.TryExtractItemId(uri.AbsoluteUri);
        if (itemId is not null)
        {
            _ = OpenLinkedItemAsync(itemId);
        }
    }

    private async Task OpenLinkedItemAsync(string itemId)
    {
        var record = await _services.Store.Items.LoadAsync(itemId);
        if (record is not null)
        {
            _main.ShowItem(record);
        }
    }

    private void OpenBooth()
    {
        var url = Item.Booth.Url ?? BoothClient.ItemPageUrl(Item.Id);
        TryStart(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    /// <summary>エクスプローラで開いて、そのファイルを選択した状態にする。</summary>
    private void OpenInExplorer(object? parameter)
    {
        if (parameter is not string path)
        {
            return;
        }

        if (File.Exists(path))
        {
            TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else
        {
            var directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory))
            {
                TryStart(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
        }
    }

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくてもアプリは動き続ける
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

public sealed class AttributeBar
{
    public required string Name { get; init; }

    public required int Value { get; init; }

    public double BarWidth => Value * 2.4;
}

/// <summary>商品ページに出す対応アバター1件（出品者の宣言）。</summary>
public sealed class AvatarRow
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    /// <summary>どこから拾ったか。推定を確定と同じ顔で出さないために添える。</summary>
    public required string SourceText { get; init; }

    public bool IsUnconfirmed { get; init; }

    /// <summary>
    /// どこから拾ったか、確認済みかをホバーで出す。
    /// 常時出すとチップが横に長くなり、1行に1〜2個しか入らなくなる。
    /// </summary>
    public string SourceTooltip => IsUnconfirmed
        ? $"{SourceText}から拾いました（未確認）"
        : $"{SourceText}から拾いました";

    /// <summary>この対応は違う、と消すための操作。行にホバーしたときだけ出す。</summary>
    public RelayCommand? RejectCommand { get; init; }
}

/// <summary>自分が着せている記録1件。</summary>
public sealed class AvatarUsageRow
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public string Note { get; init; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    public RelayCommand? RemoveCommand { get; init; }
}
