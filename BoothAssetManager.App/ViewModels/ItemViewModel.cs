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

    /// <summary>ユーザが自分で足した画像。**観測と入力を隠さない。**</summary>
    public bool IsUserAdded { get; init; }

    /// <summary>ファイル名。サムネイルの指名と、消すときに使う。</summary>
    public required string FileName { get; init; }

    /// <summary>サムネイルに指名されている1枚か。検索カードに出る絵。</summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// この画像の役割。付けていなければ出どころから決まる。
    /// 「改変例」だけは人が付けたものなので、札に出す。
    /// </summary>
    public Core.Models.ImageRole Role { get; init; }

    /// <summary>札に出す役割の名前。既定のままのものは出さない（札で埋まる）。</summary>
    public string RoleLabel => Role == Core.Models.ImageRole.Modified
        ? Core.Models.ImageRoles.Label(Role)
        : string.Empty;

    public bool HasRoleLabel => RoleLabel.Length > 0;

    /// <summary>
    /// 一覧の末尾に置く「足す」枠。画像ではない。
    ///
    /// 同じ並びに混ぜているのは、**折り返しても末尾に付いてくる**ようにするため。
    /// 別に置くと、画像が折り返したときだけ次の行へ落ちる。
    /// </summary>
    public bool IsAddTile { get; init; }

    public bool IsImage => !IsAddTile;

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
/// <summary>
/// 商品説明のh2セクション1つ。開閉を持つ。
///
/// **開閉は画面の状態なので Core には置かない。**`H2Section` は観測した中身で、
/// 畳んでいるかどうかは見る人の都合。
/// </summary>
public sealed class SectionRow(Core.Models.H2Section section) : ViewModelBase
{
    private bool _isOpen = true;

    public string Heading { get; } = section.Heading;

    public string Text { get; } = section.Text;

    /// <summary>既定は開いた状態（ユーザ指示）。畳んだ状態で出すと、あることに気付けない。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetField(ref _isOpen, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    /// <summary>開閉の印。畳めることが分からないと押されない。</summary>
    public string Marker => _isOpen ? "▾" : "▸";
}

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

/// <summary>この商品を使った改変1件。</summary>
public sealed class UsedInModificationRowViewModel
{
    public required Core.Models.ModificationRecord Record { get; init; }

    public required string AvatarText { get; init; }

    /// <summary>この商品が何回入っているか。同じ商品を別のバージョンで2回足せる。</summary>
    public required int UseCount { get; init; }

    public string Name => Record.Name;

    public string Detail => UseCount > 1
        ? $"{AvatarText}　この商品は {UseCount} 回入っています"
        : AvatarText;
}

/// <summary>Unityへ送れるもの1件と、Unityのどこに入るか。</summary>
public sealed class UnityPackageRow : ViewModelBase
{
    private string _destinationText = string.Empty;

    public required Core.Services.UnityPackageEntry Entry { get; init; }

    public string Name => Entry.Name;

    /// <summary>「Assets/〇〇 に入ります」。中を最後まで読むので、画面を出してから裏で埋まる。</summary>
    public string DestinationText
    {
        get => _destinationText;
        set
        {
            if (SetField(ref _destinationText, value))
            {
                OnPropertyChanged(nameof(HasDestination));
            }
        }
    }

    public bool HasDestination => DestinationText.Length > 0;
}

public sealed class LocalFileRow
{
    /// <summary>このファイルの同一性。商品から外すときに指す。</summary>
    public required string Hash { get; init; }

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public string? VariationLabel { get; init; }

    /// <summary>どの種類のファイルか。改変に積むときに、そのまま記録に入れる。</summary>
    public long? VariationId { get; init; }

    public bool HasVariationLabel => VariationLabel is not null;

    /// <summary>同じ中身が複数箇所にある状態。容量は1回しか数えない。</summary>
    public bool HasMultiplePaths => Paths.Count > 1;

    public string DuplicateNote => $"{Paths.Count}箇所に同じ実体";

    public bool IsMissing => Paths.Count == 0;

    /// <summary>この商品から外したファイル（ユーザ判断 2026-09-12：消さずに灰色で残す）。</summary>
    public bool IsDetached { get; init; }

    /// <summary>「この商品から外す」を出すか（外していない行だけ）。</summary>
    public bool IsAttached => !IsDetached;

    /// <summary>「この商品に戻す」を押せるか。外した後で別の商品へ紐付けてあれば押せない。</summary>
    public bool CanReattach { get; init; }

    /// <summary>「この商品に戻す」の説明。押せないときはその理由。</summary>
    public string ReattachTip { get; init; } = string.Empty;

    /// <summary>
    /// このzipに入っている、Unityへ送れるもの。
    /// zipを開いて数えるので、商品ページを組むときに1回だけ読む。
    /// </summary>
    public IReadOnlyList<Core.Services.UnityPackageEntry> UnityPackages { get; init; } = [];

    public bool HasUnityPackages => UnityPackages.Count > 0;

    /// <summary>一時フォルダへ展開できるか（手元にある zip のときだけ）。</summary>
    public bool CanUnpack { get; init; }

    /// <summary>画面に並べる行。入る先を後から埋めるので、中身とは別に持つ。</summary>
    public IReadOnlyList<UnityPackageRow> UnityPackageRows { get; init; } = [];

    /// <summary>
    /// 複数入っているときの注意。
    ///
    /// **順番を当てにいかない。**実データでは2件とも片方が依存物だったが、
    /// 2件から規則は決められない。人に決めてもらう。
    /// </summary>
    public bool HasManyUnityPackages => UnityPackages.Count > 1;

    public string UnityPackageNote =>
        $"Unityへ送れるもの {UnityPackages.Count} 件（依存するものを先に入れてください）";
}

/// <summary>
/// 商品ページ。BOOTHの商品ページを参考にしつつ、ローカルの情報から組み立てる。
/// 閲覧専用にしているのは決定事項（編集はEdit画面へ一本化し、保存経路を1つに保つ）。
/// </summary>
public sealed class ItemViewModel : ViewModelBase, IInAppLinkNavigator, IGalleryHost
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;
    private int _selectedIndex;

    /// <param name="forEditing">
    /// 編集画面の中に入れる（今開いている商品の分）。「使う」操作（Unityへ送る・展開して開く・改変）は
    /// 出さないので、そのためのzipの読み取りもしない。
    /// </param>
    public ItemViewModel(
        ItemRecord item,
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails,
        bool forEditing = false)
    {
        Item = item;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;
        ShowsUseActions = !forEditing;

        // 戻るは画面の履歴を遡る（U23）。以前は開くときに戻り先を1つ受け取っていた
        BackCommand = new RelayCommand(main.GoBack);

        // 仮IDの商品はBOOTHに存在しない。押せてしまうと「取り直したのに何も変わらない」
        // という説明の付かない結果になるので、押せなくして理由をツールチップに置く
        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => !IsRefreshing && !item.IsLocalOnly);

        // 作者名からはアプリ内のショップ画面へ送る（BOOTHへは「BOOTHで開く」がある）。
        // 戻るとこの商品ページへ帰る（画面の履歴・U23）。
        // 自分で入れたショップにも飛べる。ショップ画面は鍵で束ねているので、
        // 手元だけの鍵でもその1店として開ける
        OpenShopCommand = new RelayCommand(
            () => _ = main.ShowShopAsync(item.ShopSubdomain!),
            () => item.ShopSubdomain is not null);
        // 仮IDの商品にはBOOTHページが無い。押せると404へ送ることになる
        OpenBoothCommand = new RelayCommand(OpenBooth, () => !item.IsLocalOnly);
        CopyIdCommand = new RelayCommand(CopyId);
        PreviousImageCommand = new RelayCommand(() => GoToImage(-1), () => CanGoPreviousImage);
        NextImageCommand = new RelayCommand(() => GoToImage(1), () => CanGoNextImage);
        // 商品ページの中でその場で直す操作は、取り込みの③が済むまで塞ぐ（U8・U10・ユーザ判断）。
        // 見る・Unityへ送る・改変に足す・お気に入りは塞がない（対応アバターの書き込みと取り合わない）
        MoveImageBackCommand = new RelayCommand(() => _ = MoveImageAsync(-1), () => CanMoveImageBack && !IsEditLocked);
        MoveImageForwardCommand = new RelayCommand(() => _ = MoveImageAsync(1), () => CanMoveImageForward && !IsEditLocked);
        PinThumbnailCommand = new RelayCommand(() => _ = PinThumbnailAsync(true), () => CurrentImage is not null && !CurrentIsPinned && !IsEditLocked);
        UnpinThumbnailCommand = new RelayCommand(() => _ = PinThumbnailAsync(false), () => CurrentIsPinned && !IsEditLocked);
        RemoveImageCommand = new RelayCommand(() => _ = RemoveImageAsync(), () => CurrentIsUserAdded && !IsEditLocked);
        AddImageCommand = new RelayCommand(() => _ = AddImageAsync(), () => !IsEditLocked);
        ChangeIdCommand = new RelayCommand(() => _ = ChangeIdAsync(), () => !IsEditLocked);
        // 一度userTagを付けたitemは既定の編集キューに載らないので、ここから開く経路が要る
        EditCommand = new RelayCommand(() => _ = main.ShowEditAsync([item.Id]), () => !IsEditLocked);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, parameter => parameter is string);
        UnpackCommand = new RelayCommand(
            parameter => _ = UnpackAsync(parameter as LocalFileRow),
            parameter => parameter is LocalFileRow { CanUnpack: true });
        UnregisterFolderCommand = new RelayCommand(
            parameter => _ = UnregisterFolderAsync(parameter as string),
            parameter => parameter is string && !IsEditLocked);
        DetachFileCommand = new RelayCommand(
            parameter => _ = DetachFileAsync(parameter as LocalFileRow),
            parameter => parameter is LocalFileRow { IsDetached: false } && !IsEditLocked);
        ReattachFileCommand = new RelayCommand(
            parameter => _ = ReattachFileAsync(parameter as LocalFileRow),
            parameter => parameter is LocalFileRow { CanReattach: true } && !IsEditLocked);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);
        FetchImagesCommand = new RelayCommand(() => _ = FetchImagesAsync(), () => HasMissingImages);
        AddAvatarCommand = new RelayCommand(parameter => _ = AddAvatarAsync(parameter as string), _ => !IsEditLocked);
        SendToUnityCommand = new RelayCommand(
            SendToUnity,
            parameter => parameter is Core.Services.UnityPackageEntry);
        SendToUnityWithRecordCommand = new RelayCommand(
            parameter => _ = SendToUnityWithRecordAsync(parameter),
            parameter => parameter is Core.Services.UnityPackageEntry);
        AddToModificationCommand = new RelayCommand(() => _ = AddToModificationAsync());
        OpenModificationCommand = new RelayCommand(
            parameter => OpenModification(parameter as UsedInModificationRowViewModel),
            parameter => parameter is UsedInModificationRowViewModel);
        ToggleSectionCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is SectionRow row)
                {
                    row.IsOpen = !row.IsOpen;
                    OnPropertyChanged(nameof(ToggleAllSectionsText));
                }
            },
            parameter => parameter is SectionRow);
        ToggleAllSectionsCommand = new RelayCommand(ToggleAllSections, () => Sections.Count > 0);
        SetRoleBoothCommand = new RelayCommand(
            () => _ = SetRoleAsync(Core.Models.ImageRole.Booth),
            () => CurrentImage is { IsImage: true } && !IsEditLocked);
        SetRoleModifiedCommand = new RelayCommand(
            () => _ = SetRoleAsync(Core.Models.ImageRole.Modified),
            () => CurrentImage is { IsImage: true } && !IsEditLocked);
        SetRoleOtherCommand = new RelayCommand(
            () => _ = SetRoleAsync(Core.Models.ImageRole.Other),
            () => CurrentImage is { IsImage: true } && !IsEditLocked);

        Sections = item.Booth.H2Sections.Select(section => new SectionRow(section)).ToList();

        BuildGallery();
        BuildVariations();
        BuildLocalFiles();
        BuildLocalFolders();

        if (!ShowsUseActions)
        {
            return;
        }

        // Unityのどこに入るかは中を最後まで読むので待たない。行を出してから埋まる
        _ = LoadUnityDestinationsAsync();

        // 改変はファイルを読むので待たない。空で描いてから埋まる
        _ = LoadModificationsAsync();
    }

    public ItemRecord Item { get; private set; }

    public RelayCommand BackCommand { get; }

    /// <summary>戻るの文言。行き先は画面の履歴の直前の画面（U23）。</summary>
    public string BackText => _main.BackButtonText;

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

    public string OpenBoothTip => Item.IsLocalOnly
        ? "BOOTHに無い商品として登録したものなので、開く先がありません。"
        : "BOOTHの商品ページをブラウザで開きます。";

    public string RefreshButtonTip => Item.IsLocalOnly
        ? "BOOTHに無い商品として登録したものなので、取り直せません。"
        : "商品名・価格・種類・説明文・画像をBOOTHから取り直します。"
            + "\nメモや分類など自分で入れたものは変わりません。";

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
                    ReplaceSelf(updated);
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
    /// <summary>対応アバターを手で足す。検出が拾えなかったときの補い。</summary>
    public RelayCommand AddAvatarCommand { get; }

    /// <summary>「対応アバターを足す」の候補。既に宣言されているものは出さない。</summary>
    public IReadOnlyList<string> SupportSuggestions { get; private set; } = [];

    /// <summary>候補に出した名前から登録簿のIDを引く（U18：候補の頭に絵を出すため）。</summary>
    private Dictionary<string, string> _avatarIdsByName = new(StringComparer.CurrentCulture);

    /// <summary>
    /// 「対応アバターを足す」の候補の頭に出す絵（U18）。持っていれば商品の1枚目、持っていなければ控えの1枚。
    /// 似た名前のアバターを名前だけで選ぶと取り違える
    /// </summary>
    public Func<string, System.Windows.Media.ImageSource?> AvatarIconSelector => name =>
        _avatarIdsByName.TryGetValue(name, out var id) ? AvatarIcon(id, _thumbnails.LoadForTile) : null;

    private System.Windows.Media.Imaging.BitmapSource? AvatarIcon(
        string avatarItemId,
        Func<string, System.Windows.Media.Imaging.BitmapSource?> load)
        => Core.Services.AvatarImageSync.IconPath(_services.Paths, avatarItemId, _main.Search.FindItem(avatarItemId)) is { } path
            ? load(path)
            : null;

    public RelayCommand OpenBoothCommand { get; }

    /// <summary>この商品のIDを写す。統合先の指定に使う。</summary>
    public RelayCommand CopyIdCommand { get; }

    // ギャラリー。閲覧は矢印、操作は右クリックのメニュー
    public RelayCommand PreviousImageCommand { get; }

    public RelayCommand NextImageCommand { get; }

    public RelayCommand MoveImageBackCommand { get; }

    public RelayCommand MoveImageForwardCommand { get; }

    public RelayCommand PinThumbnailCommand { get; }

    public RelayCommand UnpinThumbnailCommand { get; }

    public RelayCommand RemoveImageCommand { get; }

    public RelayCommand AddImageCommand { get; }

    /// <summary>
    /// この商品まるごとを別のIDへ移す。
    /// 「この商品から外す」とは別——あちらはファイル、こちらは商品ごと。
    /// </summary>
    public RelayCommand ChangeIdCommand { get; }

    public RelayCommand EditCommand { get; }

    /// <summary>
    /// 取り込みの③（対応アバターの検出）がまだで、直す操作を塞いでいるか（U8・U10）。
    /// 検出の途中で対応アバターや画像を直すと、検出の書き込みと取り合いになる
    /// </summary>
    public bool IsEditLocked => _main.IsAwaitingDetection(Item.Id);

    public string EditLockText => "取り込みの途中です。対応アバターの検出が終わると編集できます（見る・Unityへ送る・改変に足すは今でもできます）";

    public string EditButtonTip => IsEditLocked ? EditLockText : "分類・タグ・属性などを直す画面を開きます";

    /// <summary>
    /// 画面内検索（U20）。畳んだ説明の中に一致があれば開いて見せる（ユーザ判断）。
    /// 畳んだ中身は画面に作られていないので、開かないと探しようがない。開いたものは開いたままにする
    /// （閉じ直すと、印を付けた場所が消える）
    /// </summary>
    public void RevealMatches(string text)
    {
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        foreach (var section in Sections.Where(section =>
                     !section.IsOpen && compare.IndexOf(section.Text, text, Views.FindInPage.Options) >= 0))
        {
            section.IsOpen = true;
        }

        OnPropertyChanged(nameof(ToggleAllSectionsText));
    }

    /// <summary>取り込みの③が済んだと主画面から知らされた。塞いでいた操作を開ける。</summary>
    public void RefreshEditLock()
    {
        OnPropertyChanged(nameof(IsEditLocked));
        OnPropertyChanged(nameof(EditButtonTip));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 編集画面の中に入れたときの、開き直しの受け口（ユーザ判断：編集画面でも商品ページと同じ操作をする）。
    /// 取り直し・ファイルやフォルダを外した・IDを変えた後、商品ページなら画面ごと開き直すが、
    /// 編集画面では画面を移らずに編集の中で読み直す。null（商品ページ）なら今まで通り商品ページを開き直す。
    /// 引数は開き直した商品。商品が消えたときは null。
    /// </summary>
    public Action<ItemRecord?>? Replaced { get; set; }

    /// <summary>
    /// 「使う」操作（Unityへ送る・展開して開く）を出すか。編集画面では出さない（ユーザ判断：
    /// 編集画面はJSONに関わる操作と確かめたいものを見せる所。その商品に何をしたいかを網羅するのは商品ページ）。
    /// </summary>
    public bool ShowsUseActions { get; }

    /// <summary>
    /// フォルダビューの右側に組み込んだとき（ユーザ判断 2026-09-13：商品ページをそのまま右に組み込む）。
    /// 戻るを隠し、左右の列を幅に合わせる（改変の画面に改変の画面を組み込んだのと同じ）。
    /// </summary>
    public bool IsEmbedded { get; init; }

    public bool ShowsBack => !IsEmbedded;

    /// <summary>組み込んだときは右側が窓より狭いので、単独の画面の最小幅（1060px）では横にはみ出す。</summary>
    public double BodyMinWidth => IsEmbedded ? 0 : 1060;

    public System.Windows.GridLength LeftColumnWidth => IsEmbedded
        ? new System.Windows.GridLength(3, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(660);

    /// <summary>開き直す。編集画面に入っていれば持ち主に任せ、商品ページなら画面ごと作り直す。</summary>
    private void ReplaceSelf(ItemRecord? updated)
    {
        if (Replaced is { } replaced)
        {
            replaced(updated);
            return;
        }

        if (updated is not null)
        {
            _main.ReplaceItem(updated);
        }
        else
        {
            _main.ShowSearch();
        }
    }

    public RelayCommand OpenInExplorerCommand { get; }

    /// <summary>いま出ている画像に役割を付ける。3つで固定</summary>
    public RelayCommand SetRoleBoothCommand { get; }

    public RelayCommand SetRoleModifiedCommand { get; }

    public RelayCommand SetRoleOtherCommand { get; }

    /// <summary>
    /// お気に入りの星（U21）。以前は検索のカードでしか付けられなかった。
    /// 保存は検索のカードと同じ道（お気に入りの項目だけを書く）なので、その間の取り込みや検出の書き込みを潰さない。
    /// </summary>
    public bool IsFavorite => Item.Local.IsFavorite;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string FavoriteTip => IsFavorite ? "お気に入りから外す" : "お気に入りに入れる";

    private RelayCommand? _toggleFavoriteCommand;

    public RelayCommand ToggleFavoriteCommand => _toggleFavoriteCommand ??= new RelayCommand(() => _ = ToggleFavoriteAsync());

    private async Task ToggleFavoriteAsync()
    {
        var next = !IsFavorite;
        SetFavorite(next);

        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SaveItemLocal(
            Item.Id, Item.Local with { IsFavorite = next }, Core.Models.LocalOwners.Favorite));

        if (result is Core.Commands.CommandResult.Failed)
        {
            // 書けなかったら戻す。付いたように見えて次に開くと消えている、を起こさない
            SetFavorite(!next);
            return;
        }

        // 検索の一覧は読み込んだ写しを持っている。戻ったときに古い星が出ないよう知らせる
        _main.Search.NoteFavoriteChanged(Item.Id, next);
    }

    /// <summary>
    /// 対応アバターの札から、そのアバターを開く（U13・ユーザ判断）。
    /// 手元に持っていればその商品ページ（戻るとこの商品へ）、持っていなければアバター画面でそのアバターを選んだ状態。
    /// 外の BOOTH へは飛ばさない（作者名からショップへ行くのと同じく、アプリの中で完結させる）。
    /// </summary>
    private async Task OpenAvatarAsync(string avatarItemId)
    {
        var avatar = await _services.Store.Items.LoadAsync(avatarItemId);
        if (avatar is null)
        {
            _main.ShowAvatar(avatarItemId);
            return;
        }

        _main.ShowItem(avatar);
    }

    private void SetFavorite(bool value)
    {
        Item = Item with { Local = Item.Local with { IsFavorite = value } };
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(FavoriteTip));
    }

    /// <summary>zipの中の <c>.unitypackage</c> をUnityへ送る。</summary>
    public RelayCommand SendToUnityCommand { get; }

    public RelayCommand SendToUnityWithRecordCommand { get; }

    public RelayCommand AddToModificationCommand { get; }

    /// <summary>見つからないファイルの札から、取り込みへ（動線の点検 D7）。移した先のフォルダを取り込むと付け直す。</summary>
    public RelayCommand ShowImportCommand => _main.ShowImportCommand;

    /// <summary>非表示にしている商品の、戻す場所へ（動線の点検 D9）。</summary>
    public RelayCommand ShowSettingsCommand => _main.ShowSettingsCommand;

    public RelayCommand OpenModificationCommand { get; }

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
            ReplaceSelf(reloaded);
        }
    }

    public RelayCommand DetachFileCommand { get; }

    /// <summary>灰色の行（外したファイル）をこの商品に戻す（ユーザ判断 2026-09-12）。</summary>
    public RelayCommand ReattachFileCommand { get; }

    private async Task ReattachFileAsync(LocalFileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.ReattachFile(Item.Id, row.Hash));
        if (result is CommandResult.Failed failed)
        {
            RefreshStatus = failed.Message;
            return;
        }

        // 未確定が1件減るので、ナビの件数を数え直す
        _main.RefreshBadges();

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            ReplaceSelf(reloaded);
        }
    }

    /// <summary>
    /// ファイルをこの商品から外して未確定へ戻す。間違って紐付いたものを直す唯一の道。
    ///
    /// **IDを書き換える形にはしない。**商品IDはファイル名にもフォルダ名にもなっていて、
    /// 対応アバターの宣言など他所からも参照されている。書き換えると参照が迷子になる。
    /// 「このファイルの行き先が違う」が本当にやりたいことなので、ファイルの側を動かす。
    /// </summary>

    /// <summary>
    /// 商品まるごとを別のIDへ移す。
    ///
    /// **「この商品から外す」とは別の操作。**あちらは*ファイル*を動かすもので、
    /// こちらは*商品ごと*を動かすもの。同じボタンに畳むと、押した結果が
    /// 「メモが残る／残らない」で変わることになる。
    ///
    /// 何が移って何が移らないかは、下見の画面で全部出してから押させる。
    /// </summary>
    private async Task ChangeIdAsync()
    {
        var model = new ChangeItemIdDialogViewModel(_services, Item.Id, Item.DisplayName);
        var dialog = new Views.ChangeItemIdDialog(model);

        if (dialog.ShowDialog() != true || model.Plan is null)
        {
            return;
        }

        var toId = model.ToId;
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.ChangeItemId(Item.Id, toId, model.SkippedPurchases));

        if (result is CommandResult.Failed failed)
        {
            RefreshStatus = failed.Message;
            return;
        }

        // 元の商品が消えて件数が変わるので、一覧を読み直す。
        // 読み直さないと、ナビの件数と検索の一覧が消えたはずの商品を数え続ける
        await _main.ReloadLibraryAsync();

        // 移した先の商品で開き直す。元の商品はもう無いので、ここに残せない
        // （編集画面なら、編集の順番の中の商品を移した先に差し替える）
        ReplaceSelf(await _services.Store.Items.LoadAsync(toId));
    }
    private async Task DetachFileAsync(LocalFileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"{row.FileName} をこの商品から外します。\n\n"
            + "ファイルは消しません。未確定に戻るので、そこで正しい商品を選び直せます。\n"
            + "次の取り込みでこの商品に戻ることもありません。\n"
            + "この欄には灰色で残り、「この商品に戻す」で戻せます。",
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
        var hideWhenEmpty = false;
        if (LocalFiles.Count(file => !file.IsDetached) == 1 && LocalFolders.Count == 0)
        {
            // 「はい／いいえ」は本文と対応を覚えないと押せない。ボタンに何が起きるかを名乗らせる（ユーザ指示）。
            // **非表示で残すのを勧め、削除は特別な操作にする**（ユーザ判断 2026-09-12）——残せば外した印も残り、
            // 次の取り込みで同じ商品が作り直されない。削除すると印も消え、再取り込みの対象になる
            var choice = Views.ChoiceDialog.Ask(
                "商品を残しますか",
                "これが最後のファイルなので、この商品は手元に何も無い状態になります。商品をどうしますか？",
                "非表示にして残す：検索やショップの件数には出さず、商品の情報と外した印を残します。"
                + "統計の支出には入ります。設定の「非表示にした商品」から戻せます。\n"
                + "残す：商品の情報を残します（価格やタグは見られます。贈った商品と同じ扱いです）。"
                + "外した印も残るので、次の取り込みでこのファイルがこの商品に戻ることはありません。\n"
                + "完全に削除：アプリ内の履歴から完全に削除します。メモや分類も一緒に消えます。"
                + "外した印も消えるので、次の取り込みで手掛かりがこの商品を指せば、再取り込みの対象になります。",
                "非表示にして残す（おすすめ）",
                "残す",
                "完全に削除");

            if (choice == Views.ChoiceDialogResult.Cancel)
            {
                return;
            }

            hideWhenEmpty = choice == Views.ChoiceDialogResult.First;
            deleteWhenEmpty = choice == Views.ChoiceDialogResult.Third;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.DetachFile(Item.Id, row.Hash, deleteWhenEmpty));

        if (result is CommandResult.FileDetached { Outcome: Core.Services.DetachOutcome.ItemDeleted })
        {
            // 開いていた商品が消えたので、戻る先は検索（編集画面なら次の商品へ）。一覧からも消えている必要がある
            await _main.ReloadLibraryAsync();
            ReplaceSelf(null);
            return;
        }

        // 「非表示にして残す」：外した後で、非表示の印だけを書く（ほかの項目は保存の直前に読み直したものが残る）。
        // 検索の一覧は写しを持っているので読み直す（読み直さないと、非表示にした商品が一覧に残る）
        if (hideWhenEmpty
            && result is CommandResult.FileDetached { Outcome: Core.Services.DetachOutcome.ItemNowEmpty }
            && await _services.Store.Items.LoadAsync(Item.Id) is { } emptied)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.SaveItemLocal(
                Item.Id, emptied.Local with { IsHidden = true }, LocalOwners.Visibility));
            await _main.ReloadLibraryAsync();
        }

        // 未確定が1件増えるので、ナビの件数を数え直す
        _main.RefreshBadges();

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            ReplaceSelf(reloaded);
        }
    }

    public RelayCommand SelectImageCommand { get; }

    /// <summary>この商品の画像を行列の先頭で取る。未取得があるときだけ押せる。</summary>
    public RelayCommand FetchImagesCommand { get; }

    public ObservableCollection<GalleryImage> Images { get; } = [];

    /// <summary>一覧に出す枠。画像に「足す」枠を1つ足したもの。</summary>
    public ObservableCollection<GalleryImage> GalleryTiles { get; } = [];

    public ObservableCollection<VariationRow> Variations { get; } = [];

    public ObservableCollection<LocalFileRow> LocalFiles { get; } = [];

    public string Name => Item.DisplayName;

    public string ShopName => Item.ShopName ?? "(ショップ不明)";

    public string ShopSubdomain => Item.ShopSubdomain ?? string.Empty;

    /// <summary>ショップをユーザが入れたか。観測と入力の区別を隠さない。</summary>
    public bool HasUserShop => Item.HasUserShop;

    /// <summary>
    /// 自分で入れたショップであることを示す1行。
    /// BOOTHのショップに結び付いているなら、そちらの店であることも言う。
    /// </summary>
    public string UserShopNotice => Item.Local.Shop is { IsOnBooth: true } shop
        ? $"このショップは自分で結び付けたものです（BOOTHの {shop.Subdomain}）"
        : "このショップ名は自分で入れたものです";

    /// <summary>
    /// 分類。ユーザが入れたものを優先する。親は同梱の表から引く
    /// （ユーザには子の名前しか入れさせないので、こちらで補う）。
    /// </summary>
    public string CategoryText => _services.Categories.TextFor(Item.Local.Category, Item.Booth.Category);

    /// <summary>分類をユーザが入れたか。観測と入力の区別を隠さない。</summary>
    public bool HasUserCategory => Item.HasUserCategory;

    public string IdText => $"ID {Item.Id}";

    /// <summary>
    /// 商品IDを写す。**IDそのものだけを写す**——「ID 12345」ごと写しても貼れない。
    ///
    /// 統合先の指定をIDやBOOTHのリンクで行うので、その商品のIDを
    /// 手で書き写さずに取り出せる必要がある。UI要素は増やさず、
    /// 出ているIDそのものを押せるようにした。
    /// </summary>
    public string IdCopyTip => Item.IsLocalOnly
        ? "クリックすると仮のIDを写します"
        : "クリックすると商品IDを写します";

    /// <summary>
    /// BOOTHに無い商品として登録したもの。**BOOTHへは問い合わせない。**
    /// 商品ページに常設で出す——登録時に言うだけでは、半年後に
    /// 「なぜこの商品だけ情報が増えないのか」の理由が思い出せない。
    /// </summary>
    public bool IsLocalOnly => Item.IsLocalOnly;

    /// <summary>名前をユーザが付けたか。観測と入力の区別を隠さない。</summary>
    public bool HasUserName => Item.Local.DisplayName is { Length: > 0 };

    /// <summary>
    /// ユーザが名付けたことを示す1行。BOOTHの名前も取れているなら並べて出す
    /// （どちらの名前で覚えていても辿り着けるように）。
    /// </summary>
    public string UserNameNotice => Item.Booth.Name is { Length: > 0 } booth
        ? $"この名前は自分で付けたものです。BOOTHでの名前は「{booth}」"
        : "この名前は自分で付けたものです";

    public string LocalOnlyNotice
        => $"BOOTHに無い商品として登録しています（仮のID {Item.Id}）。"
            + "BOOTHから情報を取り直さないので、名前も画像も増えません。";


    public string PublishedText => Item.Booth.PublishedAt is null
        ? string.Empty
        : $"公開 {Item.Booth.PublishedAt:yyyy-MM-dd}";

    public string WishText => $"♡ {Item.Booth.WishListsCount:N0}";

    public IReadOnlyList<string> Tags => Item.Booth.Tags;

    /// <summary>BOOTHのタグの見出しに添える件数。畳んでいても何件あるかは分かるように。</summary>
    public string BoothTagsCountText => $"（{Tags.Count}）";

    private ChipStrip<string>? _tagStrip;

    /// <summary>
    /// 並べるタグの札（ユーザ指示 2026-09-12：多い商品を開くのが遅い）。畳んでいる間は作らず、
    /// 多い商品は最初の一部と「残り n 件を表示」だけ。作った札は隠すだけで捨てない（<see cref="ChipStrip{TSource}"/>）。
    /// BOOTHのタグはこの画面を開いている間は変わらないので、最初に見られたときに1回だけ組む
    /// </summary>
    public ObservableCollection<object> TagTiles
    {
        get
        {
            if (_tagStrip is null)
            {
                _tagStrip = ChipLists.TagStrip();
                _tagStrip.Reset(Tags, IsBoothTagsExpanded, showAll: false);
            }

            return _tagStrip.Tiles;
        }
    }

    /// <summary>
    /// BOOTHのタグを開いているか（ユーザ指示 2026-09-12：多過ぎる商品があるので畳める）。
    /// 商品ページと編集画面で共通で、商品を移っても保つ（<see cref="SectionFolds"/>）。
    /// </summary>
    public bool IsBoothTagsExpanded
    {
        get => SectionFolds.BoothTagsExpanded;
        set
        {
            if (SectionFolds.BoothTagsExpanded != value)
            {
                SectionFolds.BoothTagsExpanded = value;
                OnPropertyChanged(nameof(IsBoothTagsExpanded));

                // 畳んでいる間は札を作らない。開いたときに作る。畳んでも作った札は捨てない
                _tagStrip?.SetExpanded(value);
            }
        }
    }

    public IReadOnlyList<UserTagAssignment> UserTags => Item.Local.UserTags;

    public bool HasUserTags => Item.Local.UserTags.Count > 0;

    /// <summary>
    /// 商品説明のh2セクション。
    ///
    /// **長すぎることがある**（Wendyは説明だけで画面を6枚ぶん流れる）ので、
    /// セクションごとに畳める。既定は全部開いた状態。
    /// </summary>
    public IReadOnlyList<SectionRow> Sections { get; private set; } = [];

    /// <summary>
    /// 1つずつ押さずにまとめて畳む／開く。
    ///
    /// 見出しだけ見て目当てを探したい場面と、通して読みたい場面の両方がある。
    /// いま1つでも開いていれば「すべて畳む」、全部畳んでいれば「すべて開く」。
    /// </summary>
    public RelayCommand ToggleAllSectionsCommand { get; }

    /// <summary>見出しを押して1つだけ畳む／開く。</summary>
    public RelayCommand ToggleSectionCommand { get; }

    public string ToggleAllSectionsText => Sections.Any(section => section.IsOpen)
        ? "すべて畳む"
        : "すべて開く";

    private void ToggleAllSections()
    {
        // 1つでも開いていれば畳む側に寄せる。「全部開く」を押したのに
        // 半分閉じたままになるのを避ける
        var open = Sections.Any(section => section.IsOpen);
        foreach (var section in Sections)
        {
            section.IsOpen = !open;
        }

        OnPropertyChanged(nameof(ToggleAllSectionsText));
    }

    public bool HasSections => Item.Booth.H2Sections.Count > 0;

    public string? Description => Item.Booth.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Booth.Description);

    public string? Memo => Item.Local.Memo;

    public bool HasMemo => !string.IsNullOrWhiteSpace(Item.Local.Memo);

    /// <summary>出品者が宣言している対応アバター。こちらは編集しない。</summary>
    public IReadOnlyList<AvatarRow> Avatars { get; private set; } = [];

    /// <summary>
    /// ユーザが消した対応アバター（ユーザ判断 2026-09-12）。以前は消すと画面のどこにも出ず、
    /// 消したことも戻せることも分からなかった（戻す道は名前を打ち直すことだけだった）。
    /// </summary>
    public IReadOnlyList<RejectedAvatarRow> RejectedAvatars { get; private set; } = [];

    public bool HasRejectedAvatars => RejectedAvatars.Count > 0;

    public string RejectedAvatarsHeader => $"消したもの {RejectedAvatars.Count} 件";

    private static bool s_rejectedAvatarsExpanded;

    /// <summary>「消したもの」を開いているか。既定は畳む。商品を移っても保つ（アプリを閉じるまで）。</summary>
    public bool IsRejectedAvatarsExpanded
    {
        get => s_rejectedAvatarsExpanded;
        set
        {
            if (s_rejectedAvatarsExpanded != value)
            {
                s_rejectedAvatarsExpanded = value;
                OnPropertyChanged(nameof(IsRejectedAvatarsExpanded));
            }
        }
    }

    public bool HasAvatars => Avatars.Count > 0;

    /// <summary>
    /// 札を絞り込む欄を出す境目（U26）。
    /// 20体までなら札は4〜5行に収まり、目で追える。それを超えると探す手間の方が大きい
    /// （友人のデータには248体を宣言している商品がある）。
    /// </summary>
    private const int AvatarFilterThreshold = 20;

    private string _avatarFilter = string.Empty;

    /// <summary>アバター名で札を絞る（U26）。ひらがな・カタカナ、全角・半角、大文字・小文字は区別しない。</summary>
    public string AvatarFilter
    {
        get => _avatarFilter;
        set
        {
            if (SetField(ref _avatarFilter, value ?? string.Empty))
            {
                ApplyAvatarFilter();
            }
        }
    }

    /// <summary>画面に出す札。絞り込み欄に何か入っていれば、名前が一致するものだけ。</summary>
    public IReadOnlyList<AvatarRow> VisibleAvatars { get; private set; } = [];

    /// <summary>
    /// 並べる札に、末尾の「＋ 追加」を混ぜたもの（ユーザ指示 2026-09-12）。画像の一覧の「足す」枠と同じく、
    /// 同じ並びに混ぜると折り返しても末尾に付いてくる。札が0件でも「＋ 追加」は出る。
    /// 作った札は隠すだけで捨てない（<see cref="ChipStrip{TSource}"/>・2回目以降に全部並べるのを速くする）
    /// </summary>
    public ObservableCollection<object> AvatarTiles => _avatarStrip.Tiles;

    private readonly ChipStrip<AvatarRow> _avatarStrip = new(row => row, "体", "対応アバター", AvatarAddTile.Instance);

    /// <summary>対応アバターの見出しに添える件数。畳んでいても何体あるかは分かるように。</summary>
    public string AvatarsCountText => Avatars.Count == 0 ? string.Empty : $"（{Avatars.Count} 体）";

    /// <summary>
    /// 対応アバターの欄を開いているか（ユーザ指示 2026-09-12：200体を超える商品があるので畳める）。
    /// 商品ページと編集画面で共通で、商品を移っても保つ（アプリを閉じるまで）。
    /// </summary>
    public bool IsAvatarsExpanded
    {
        get => SectionFolds.AvatarsExpanded;
        set
        {
            if (SectionFolds.AvatarsExpanded != value)
            {
                SectionFolds.AvatarsExpanded = value;
                OnPropertyChanged(nameof(IsAvatarsExpanded));

                // 畳んでいる間は札を作らない。開いたときに作る。畳んでも作った札は捨てない
                _avatarStrip.SetExpanded(value);
            }
        }
    }

    private bool _isAddingAvatar;

    /// <summary>「＋ 追加」を押して、足す入力欄を出しているか。商品を移ると畳んだ状態に戻る。</summary>
    public bool IsAddingAvatar
    {
        get => _isAddingAvatar;
        private set => SetField(ref _isAddingAvatar, value);
    }

    private RelayCommand? _startAddAvatarCommand;
    private RelayCommand? _stopAddAvatarCommand;

    public RelayCommand StartAddAvatarCommand => _startAddAvatarCommand ??= new RelayCommand(
        () => IsAddingAvatar = true,
        () => !IsEditLocked);

    public RelayCommand StopAddAvatarCommand => _stopAddAvatarCommand ??= new RelayCommand(() => IsAddingAvatar = false);

    public bool ShowsAvatarFilter => Avatars.Count > AvatarFilterThreshold;

    public string AvatarFilterPlaceholder => $"アバター名で絞る（{Avatars.Count} 体）";

    /// <summary>絞った結果の件数。0件のときに「無い」のか「絞り過ぎ」なのかを分ける。</summary>
    public string AvatarFilterResultText => _avatarFilter.Trim().Length == 0
        ? string.Empty
        : VisibleAvatars.Count == 0
            ? "一致するアバターはありません"
            : $"{Avatars.Count} 体中 {VisibleAvatars.Count} 体";

    private void ApplyAvatarFilter()
    {
        var needle = _avatarFilter.Trim();
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions options = System.Globalization.CompareOptions.IgnoreCase
            | System.Globalization.CompareOptions.IgnoreKanaType
            | System.Globalization.CompareOptions.IgnoreWidth;

        Func<AvatarRow, bool>? match = needle.Length == 0
            ? null
            : row => compare.IndexOf(row.Name, needle, options) >= 0;
        VisibleAvatars = match is null ? Avatars : Avatars.Where(match).ToList();

        // 札を並べるのは画面の中で重い（友人データの244体の商品で、札だけで約550ms・ユーザ指摘 2026-09-12）。
        // 畳んでいる間は作らず、多い商品は最初の一部と「残り n 体を表示」だけ（持っているアバターが先頭に来る並びのまま切る）。
        // 絞り込んでいるときは、探しているのだから一致したものを全部並べる。
        // 一致しない札も隠すだけなので、絞り込みを打ち替えても作り直さない
        _avatarStrip.Filter(match);

        OnPropertyChanged(nameof(VisibleAvatars));
        OnPropertyChanged(nameof(AvatarFilterResultText));
    }

    /// <summary>並べる対応アバターが同じか（札に出る中身で比べる）。</summary>
    private static bool SameAvatars(IReadOnlyList<AvatarRow> a, IReadOnlyList<AvatarRow> b)
        => a.Count == b.Count
            && a.Zip(b).All(pair => pair.First.ItemId == pair.Second.ItemId
                && pair.First.Name == pair.Second.Name
                && pair.First.SourceText == pair.Second.SourceText
                && pair.First.IsUnconfirmed == pair.Second.IsUnconfirmed
                && pair.First.IsOwned == pair.Second.IsOwned);

    /// <summary>この商品が名指ししている共通素体。</summary>
    public IReadOnlyList<string> AvatarBases { get; private set; } = [];

    public bool HasAvatarBases => AvatarBases.Count > 0;

    public string AvatarSectionNote => HasAvatars || HasAvatarBases
        ? "出品者が対応と書いているアバターです。"
        : "出品者の対応表明は見つかっていません。アバターの管理から検出できます。";

    public IReadOnlyList<AttributeBar> Attributes { get; private set; } = [];

    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>フォルダとして所有しているもの。zipが残っていない展開済みの配布物。</summary>
    public IReadOnlyList<LocalFolderRow> LocalFolders { get; private set; } = [];

    public bool HasLocalFolders => LocalFolders.Count > 0;

    public string FileSummary => Item.IsDownloaded
        ? $"{Item.Local.OwnedFiles.Count} 件 / {Core.Models.DisplayText.Size(Item.LogicalSizeBytes)}"
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

    /// <summary>
    /// 検索とショップの件数から除いているか。
    ///
    /// **自分で設定したものなので、設定した本人に見えていないといけない。**
    /// 件数が合わないときに、除いたせいなのかを確かめる先がここ以外に無い。
    /// </summary>
    public string HiddenText => Item.Local.IsHidden ? "している" : "していない";

    public bool IsHidden => Item.Local.IsHidden;

    /// <summary>
    /// 販売終了かどうか。
    ///
    /// **「終了」と「まだ確かめていない」を同じ顔にしない。**
    /// 見つからない回数を溜めてから終了と判断する作りなので、
    /// 途中の状態を「販売中」と言い切ると嘘になる。
    /// </summary>
    public string DelistedText => Item.Local.IsDelisted
        ? "終了"
        : Item.Local.ConsecutiveNotFoundCount > 0
            ? $"確認中（{Item.Local.ConsecutiveNotFoundCount} 回見つかりません）"
            : "販売中";

    /// <summary>
    /// アバターの検出をいつ走らせたか。
    /// 対応アバターが空のとき、検出していないのか検出して無かったのかを分ける。
    /// </summary>
    public string AvatarsDetectedText => Item.Local.AvatarsDetectedAt is null
        ? "まだ検出していません"
        : Item.Local.AvatarsDetectedAt.Value.ToString("yyyy-MM-dd");

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

            // 右クリックのメニューは「いま見ている1枚」で中身が変わる。
            // ここで知らせないと、絵を送ってもメニューが前の絵のままになる
            NoteGalleryChanged();
        }
    }

    /// <summary>
    /// 大きく出す1枚。一覧の方は小さく縮めたものなので、ここは保存された大きさで読み直す（キャッシュに乗る）。
    /// 末尾の「＋」は画像ではないので何も出さない。
    /// </summary>
    public BitmapSource? SelectedImage => Images.Count == 0 || Images[SelectedIndex] is not { IsImage: true } selected
        ? null
        : _thumbnails.Load(selected.Path);

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

            Images.Add(new GalleryImage
            {
                Path = entry.Path,
                FileName = fileName,
                Image = _thumbnails.LoadForTile(entry.Path),
                IsOrphaned = entry.IsOrphaned,
                IsUserAdded = entry.IsUserAdded,
                IsPinned = string.Equals(
                    fileName, Item.Local.ThumbnailImage, StringComparison.OrdinalIgnoreCase),
                Role = Core.Images.ItemImageOrder.RoleOf(entry, Item.Local.ImageRoles),
            });
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

    /// <summary>
    /// 対応アバターまわりを組み立てる。
    ///
    /// **ここは出品者の宣言（Avatars / AvatarBases）だけ。**
    /// 自分が着せた記録は改変（着せ替え1つ）を単位に持つことにしたので、
    /// 「この商品を使った改変」のカードに分けてある。
    /// 混ぜると「誰が言っていることなのか」が分からなくなる。
    /// </summary>
    private void BuildAvatars()
    {
        var registry = _services.Store.Avatars.Load();
        var names = AvatarNames.Map(registry.Entries);

        string NameOf(string id, string? cached)
            => names.TryGetValue(id, out var name) ? name : cached ?? id;

        // 持っているアバターを先に（U25）。100体を超える商品では、自分のアバターが
        // 札の山のどこにあるかを探すことになっていた。持っていない分は出品者の並びのまま
        var ownedIds = _main.Search.OwnedItemIds();
        var manuallyOwned = registry.Entries
            .Where(entry => entry.IsOwnedManually)
            .Select(entry => entry.ItemId)
            .ToHashSet(StringComparer.Ordinal);

        var rows = Item.Local.Avatars
            .Where(link => !link.Rejected)
            .Select(link => new AvatarRow
            {
                ItemId = link.AvatarItemId,
                Name = NameOf(link.AvatarItemId, link.Name),
                SourceText = SourceLabel(link.Source),
                IsUnconfirmed = !link.Confirmed,
                IsOwned = ownedIds.Contains(link.AvatarItemId) || manuallyOwned.Contains(link.AvatarItemId),
                RejectCommand = new RelayCommand(() => _ = RejectAvatarAsync(link.AvatarItemId), () => !IsEditLocked),
                // ツールチップに出す絵（R3）。乗せたときに初めて読む——248体の商品で全部を先に読むと開くのが遅れる
                IconFactory = () => AvatarIcon(link.AvatarItemId, _thumbnails.LoadForCard),
                OpenCommand = new RelayCommand(() => _ = OpenAvatarAsync(link.AvatarItemId)),
            })
            .OrderByDescending(row => row.IsOwned)
            .ToList();

        // 何か保存するたびにここを通る（お気に入りを付けただけでも）。並べる対応アバターが変わっていなければ、
        // 作った札（全部並べた分も）をそのまま使う。作り直すと244体の商品で全部並べ直すのに約800ms掛かる
        if (_avatarStrip.Tiles.Count == 0 || !SameAvatars(Avatars, rows))
        {
            Avatars = rows;
            _avatarStrip.Reset(Avatars, IsAvatarsExpanded, _avatarStrip.ShowsAll);
        }

        ApplyAvatarFilter();

        AvatarBases = Item.Local.AvatarBases
            .Where(link => !link.Rejected)
            .Select(link => link.BaseName)
            .ToList();

        // 消した対応は畳んだ欄に並べ、1件ずつ戻せるようにする（ユーザ判断 2026-09-12）
        RejectedAvatars = Item.Local.Avatars
            .Where(link => link.Rejected)
            .Select(link => new RejectedAvatarRow
            {
                Name = NameOf(link.AvatarItemId, link.Name),
                RestoreCommand = new RelayCommand(() => _ = RestoreAvatarAsync(link.AvatarItemId), () => !IsEditLocked),
            })
            .ToList();

        // 候補の名前から絵を引くための表（U18）
        _avatarIdsByName = new Dictionary<string, string>(StringComparer.CurrentCulture);
        foreach (var entry in registry.Entries)
        {
            _avatarIdsByName.TryAdd(names[entry.ItemId], entry.ItemId);
        }

        // 対応アバターの候補。既に宣言されているものは出さない
        var declared = Avatars.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        SupportSuggestions = registry.Entries
            .Where(entry => AvatarService.IsAvatar(entry) && !declared.Contains(entry.ItemId))
            .Select(entry => names[entry.ItemId])
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        OnPropertyChanged(nameof(SupportSuggestions));

        foreach (var name in new[]
        {
            nameof(Avatars), nameof(HasAvatars), nameof(AvatarBases), nameof(HasAvatarBases),
            nameof(AvatarSectionNote), nameof(ShowsAvatarFilter), nameof(AvatarFilterPlaceholder),
            nameof(RejectedAvatars), nameof(HasRejectedAvatars), nameof(RejectedAvatarsHeader),
            nameof(AvatarsCountText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private static string SourceLabel(AvatarLinkSource source) => source switch
    {
        AvatarLinkSource.SupportSection => "対応アバター節",
        AvatarLinkSource.Tag => "タグ",
        AvatarLinkSource.Variation => "種類の名前",
        AvatarLinkSource.H2Link => "説明文のリンク",
        AvatarLinkSource.SupportList => "説明文の対応一覧",
        AvatarLinkSource.Manual => "手入力",
        _ => string.Empty,
    };

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
    /// 消した対応を戻す（「消したもの」の欄の［戻す］・ユーザ判断 2026-09-12）。
    /// 出どころは消したときに「手入力」へ付け替えてあるので、元の出どころ（対応アバター節・タグなど）には戻らない。
    /// 手入力のままにするのは、次の検出でも消えないようにするため（手で足したのと同じ扱い）
    /// </summary>
    private async Task RestoreAvatarAsync(string avatarItemId)
    {
        var links = Item.Local.Avatars
            .Select(link => link.AvatarItemId == avatarItemId
                ? link with { Rejected = false, Confirmed = true }
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
                Name = AvatarNames.ShownName(match),
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

        // 候補に出した名前（同じ名前ならショップ名付き）と、正式名のどちらでも引けるようにする
        var entries = _services.Store.Avatars.Load().Entries;
        var names = AvatarNames.Map(entries);
        return entries.FirstOrDefault(entry =>
            string.Equals(names[entry.ItemId], name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(AvatarNames.ShownName(entry), name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(entry.BoothName, name, StringComparison.CurrentCultureIgnoreCase));
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
        // ToLookup は null の鍵を持てる（ToDictionary は持てない）。
        // 「どのバリエーションも指していない」記録がここに入る
        var ordered = Item.Local.Purchases.ToLookup(record => record.VariationId);

        foreach (var variation in Item.Booth.Variations)
        {
            var group = ordered[variation.Id].ToList();
            Variations.Add(new VariationRow
            {
                Name = variation.Name ?? "（1種類のみ）",
                PriceText = group.Count > 0 ? PurchaseText(group) : $"¥{variation.Price:N0}",
                IsPurchased = group.Count > 0,
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す。
        // バリエーションを指していない記録（null）もここへ落ちる——
        // 指す先が無いので「現存する」側には入らない
        var currentIds = Item.Booth.Variations.Select(variation => (long?)variation.Id).ToHashSet();
        foreach (var group in ordered.Where(entry => !currentIds.Contains(entry.Key)))
        {
            var purchases = group.ToList();
            Variations.Add(new VariationRow
            {
                Name = purchases[0].NameSnapshot ?? DisplayText.VariationLabel(group.Key),
                PriceText = PurchaseText(purchases),
                IsPurchased = true,

                // 指していない記録は「消えた」わけではない。指す先が無いだけ
                IsGone = group.Key is not null,
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

        // 括弧の中は名詞、文の中は動詞。同じ語を両方に使うと
        // 「¥100 で自分用」か「価格未入力（買った）」のどちらかが崩れる
        var price = head.Price is null ? "価格未入力" : $"¥{head.Price:N0}";
        var text = head.Price is null
            ? $"{price}（{DisplayText.PurchaseKindLabel(head.Kind)}）"
            : $"{price} で{DisplayText.PurchaseKindVerb(head.Kind)}";

        return group.Count > 1 ? $"{text} ほか {group.Count - 1} 件" : text;
    }

    private void BuildLocalFiles()
    {
        // 外したファイルは灰色で後ろに残す（ユーザ判断 2026-09-12）。持っているファイルを先に
        foreach (var file in Item.Local.LocalFiles.OrderBy(file => file.Detached))
        {
            var variation = file.VariationId is null
                ? null
                : Item.Booth.Variations.FirstOrDefault(entry => entry.Id == file.VariationId)?.Name;
            // 編集画面では「使う」操作を出さないので、zipを開いて数えることもしない（1件進むたびに開くことになる）。
            // 外したファイルも使う対象ではない
            var usable = ShowsUseActions && !file.Detached;
            var packages = usable ? FindUnityPackages(file) : [];

            // 外した後で別の商品へ紐付けてあれば戻せない（同じファイルが2つの商品の持ち物になる）
            var owner = file.Detached ? _main.Search.FindFileOwner(file.Hash, Item.Id) : null;

            LocalFiles.Add(new LocalFileRow
            {
                Hash = file.Hash,
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : "(見つかりません)",
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                Paths = file.Paths,
                VariationLabel = variation,
                VariationId = file.VariationId,
                UnityPackages = packages,
                IsDetached = file.Detached,
                CanReattach = file.Detached && owner is null,
                ReattachTip = owner is null
                    ? "このファイルをこの商品に戻します。未確定からは消えます。"
                    : $"外した後で「{owner.DisplayName}」に紐付けてあるので、戻せません。先にそちらの商品から外してください。",
                CanUnpack = usable && file.Paths.Any(path =>
                    path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path)),
                UnityPackageRows = packages.Select(package => new UnityPackageRow { Entry = package }).ToList(),
            });
        }
    }

    /// <summary>
    /// Unityのどこに入るかを、行ごとに裏で読んで埋める。
    ///
    /// **画面を組むときに同期で読まない。**入る先は unitypackage を最後まで解かないと
    /// 分からない（手元の実測で 40MB の物が 0.2 秒ほど）。1つずつ順に読むのは、
    /// 同じzipを並んで開いてディスクを取り合わないため。
    /// </summary>
    private async Task LoadUnityDestinationsAsync()
    {
        foreach (var row in LocalFiles.SelectMany(file => file.UnityPackageRows).ToList())
        {
            var roots = await Task.Run(() => Core.Services.UnityHandoff.ReadDestinations(row.Entry));
            row.DestinationText = Core.Services.UnityHandoff.DescribeDestinations(roots);
        }
    }


    /// <summary>
    /// このファイルがzipなら、中の <c>.unitypackage</c> を数える。
    ///
    /// **1箇所目だけ見る。**同じ中身が複数箇所にあっても中身は同じなので、
    /// 全部開くのは無駄。zip以外（展開済みのフォルダやpdf）は対象外。
    /// </summary>
    private static IReadOnlyList<Core.Services.UnityPackageEntry> FindUnityPackages(Core.Models.LocalFileRecord file)
    {
        var path = file.Paths.FirstOrDefault(File.Exists);

        // zip のハッシュを持たせる。入り先を取り込みの裏で読んだ控えから引ける（zip を解き直さない）
        return path is not null && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? Core.Services.UnityHandoff.FindPackages(path).Select(package => package with { ZipHash = file.Hash }).ToList()
            : [];
    }

    /// <summary>
    /// 送り先のUnityを1つに絞る。絞れなければ理由を出して null を返す。
    ///
    /// **「改変に足して送る」と共通の門。**どちらのボタンでも同じ条件で
    /// 送れる／送れないが決まるべきで、片方だけ通ると挙動が読めなくなる。
    /// </summary>
    private Services.OpenUnityEditor? PickUnityTarget(string title)
    {
        // 連続送りの最中は混ぜない。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (Services.UnityImportQueue.IsRunning)
        {
            System.Windows.MessageBox.Show(
                Services.UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return null;
        }

        // 窓を名指しして送る道なので、複数開いていても選べば送れる（U14・ユーザ判断）。
        // 以前はファイルの関連付けに渡していて、どれに入るかを指名できず、2つ以上開いていると断っていた
        return Services.UnityTargetPicker.Pick(title);
    }

    private void SendToUnity(object? parameter) => _ = SendToUnityAsync(parameter);

    private async Task SendToUnityAsync(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        const string title = "Unityへ送る";

        if (PickUnityTarget(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var answer = System.Windows.MessageBox.Show(
            $"「{package.Name}」を、Unityの「{target}」に送ります。\n\n"
            + "Unity側で取り込む内容の一覧が出るので、そこで確認してから取り込めます。",
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            await SendOneToUnityAsync(editor, package, title);
        }
    }

    /// <summary>
    /// 1件を、選んだ Unity の窓へ名指しで送る（U14）。検索の複数選択・改変と同じ道（1件だけの列）。
    /// 取り込みの終わりをログで見るので、Cancel されたかも分かる。
    /// </summary>
    /// <returns>取り込み画面を出せたか。</returns>
    private async Task<bool> SendOneToUnityAsync(
        Services.OpenUnityEditor editor,
        Core.Services.UnityPackageEntry package,
        string title)
    {
        var outcomes = await Services.UnityImportQueue.RunAsync(
            editor.ProcessId, [package], progress: null, CancellationToken.None);
        var outcome = outcomes.FirstOrDefault();

        if (outcome is null || !outcome.Opened)
        {
            System.Windows.MessageBox.Show(
                $"「{package.Name}」をUnityへ送れませんでした。\n\n{outcome?.Problem ?? "理由が分かりませんでした。"}",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // 「使った」の足跡。Unityへ送ったことが一番強い証拠（ユーザ判断）。
        // 取り込み画面で Cancel された物は入っていないので付けない（検索の複数選択と同じ扱い）
        if (!outcome.Cancelled)
        {
            _ = _services.Recent.TouchAsync(Item.Id, Core.Services.RecentKind.Used);
        }

        return true;
    }

    /// <summary>
    /// 改変に足して送る。
    ///
    /// **「送る」と別のボタンにしてある**（ユーザ判断）。送る前に「記録しますか」と
    /// 聞くと、記録を使っていない人の邪魔になる。ボタンで分ければ、
    /// **押した人だけが記録の話に入る。**
    /// </summary>
    private async Task SendToUnityWithRecordAsync(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        const string title = "改変に足して送る";

        if (PickUnityTarget(title) is not { } editor)
        {
            return;
        }

        // 送り先のプロジェクトを、窓のタイトルの名前から実体のパスに直す。
        // HubにもVCCにも載っていないプロジェクトだと引けない——そのときは
        // 候補を絞らずに全部出す（**推定で絞ると、正しい改変が消える**）
        var projectPath = editor.ProjectName is { } name
            ? await Task.Run(() => Core.Services.UnityProjects.Discover()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))?.Path)
            : null;

        var records = projectPath is not null
            ? await _services.Modifications.LoadForProjectAsync(projectPath)
            : (await _services.Modifications.LoadAllAsync()).Modifications;

        var model = await BuildPickModificationAsync(
            title,
            $"「{package.Name}」を送って、改変に足します。",
            projectPath is not null
                ? $"送り先：Unityの「{editor.ProjectName}」"
                : $"送り先：Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」"
                    + "（一覧に無いプロジェクトなので、改変は全部出しています）",
            records,
            existingLabel: "このプロジェクトの改変に足す",
            commitLabel: "足して送る",
            emptyText: "このプロジェクトに紐付いた改変はまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        // **記録してから送る。**送るのは Unity 側の取り込み画面を待つので時間がかかり、
        // 途中で窓を閉じられることもある。先に記録を確定させておく方が失うものが少ない
        var owner = LocalFiles.FirstOrDefault(file => file.UnityPackages.Contains(package));
        if (await CommitPickedModificationAsync(model, title, projectPath, owner, package.EntryPath)
            is not { } record)
        {
            return;
        }

        // 窓を名指しして送る（U14）。取り込み画面を出せなかったら、記録だけ済んだと正直に言う
        var sent = await SendOneToUnityAsync(editor, package, title);

        UnityRecordNotice = sent
            ? $"「{record.Name}」に足して、Unityへ送りました。"
            : $"「{record.Name}」に足しました。Unityへは送れませんでした。";
    }

    /// <summary>
    /// 改変に足す（送らない）。
    ///
    /// **見せる場所と足す場所を同じにする**（ユーザ指摘）。
    /// 使った改変を出しているカードから、そのまま足せるようにした。
    /// </summary>
    private async Task AddToModificationAsync()
    {
        const string title = "改変に足す";

        var model = await BuildPickModificationAsync(
            title,
            $"「{Item.DisplayName}」を改変に足します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（Unityへ送ると残ります）。",
            (await _services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に足す",
            commitLabel: "足す",
            emptyText: "改変がまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        if (await CommitPickedModificationAsync(model, title, project: null, owner: null, package: null)
            is not { } record)
        {
            return;
        }

        UnityRecordNotice = $"「{record.Name}」に足しました。";
        await LoadModificationsAsync();
    }

    /// <summary>ダイアログの中身を組む。送るときと足すだけのときで文言だけ変える（組み方は検索画面と共通）。</summary>
    private Task<PickModificationDialogViewModel> BuildPickModificationAsync(
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<Core.Models.ModificationRecord> records,
        string existingLabel,
        string commitLabel,
        string emptyText)
        => Task.FromResult(ModificationPicking.BuildDialog(
            _services, title, headingText, contextText, records, existingLabel, commitLabel, emptyText));

    /// <summary>
    /// ダイアログの答えを記録に落とす。作る側なら先に作る。
    /// 作れなかったときは理由を出して null を返す。
    /// </summary>
    private async Task<Core.Models.ModificationRecord?> CommitPickedModificationAsync(
        PickModificationDialogViewModel model,
        string title,
        string? project,
        LocalFileRow? owner,
        string? package)
    {
        if (await ModificationPicking.ResolvePickedAsync(_services, model, title, project) is not { } record)
        {
            return null;
        }

        await _services.Commands.ExecuteAsync(
            new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new Core.Models.ModificationMember
                {
                    ItemId = Item.Id,
                    VariationId = owner?.VariationId,
                    FileHash = owner?.Hash,
                    Package = package,
                    AddedAt = DateTimeOffset.Now,
                }));

        return record;
    }

    private string? _unityRecordNotice;

    /// <summary>
    /// 直前に改変へ積んだ結果。
    ///
    /// **積んだことは画面のどこにも出ない。**Unityへ渡した先の反応は
    /// こちらに返ってこないので、記録が入ったことだけは言っておく。
    /// </summary>
    public string? UnityRecordNotice
    {
        get => _unityRecordNotice;
        private set
        {
            if (SetField(ref _unityRecordNotice, value))
            {
                OnPropertyChanged(nameof(HasUnityRecordNotice));
            }
        }
    }

    public bool HasUnityRecordNotice => !string.IsNullOrEmpty(UnityRecordNotice);

    /// <summary>
    /// この商品を使った改変。
    ///
    /// **改変から辿れば分かる情報を商品ページで隠さない。**
    /// 「持っているのに出していない」を直した直後なので、同じ指摘を作らない。
    /// </summary>
    public ObservableCollection<UsedInModificationRowViewModel> UsedInModifications { get; } = [];

    public bool HasUsedInModifications => UsedInModifications.Count > 0;

    public string UsedInModificationsEmptyText =>
        "まだどの改変にも入っていません。下の「改変に足す」で残せます。";

    /// <summary>改変の詳細へ。戻るとこの商品へ帰る（見比べに戻ってくる。画面の履歴・U23）。</summary>
    private void OpenModification(UsedInModificationRowViewModel? row)
    {
        if (row is not null)
        {
            _main.ShowModification(row.Record);
        }
    }

    private async Task LoadModificationsAsync()
    {
        var records = await _services.Modifications.LoadUsingItemAsync(Item.Id);
        var registry = _services.Store.Avatars.Load();

        RunOnUiThread(() =>
        {
            UsedInModifications.Clear();
            foreach (var record in records)
            {
                UsedInModifications.Add(new UsedInModificationRowViewModel
                {
                    Record = record,
                    AvatarText = registry.Entries.FirstOrDefault(entry =>
                        string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                        is { } found
                            ? AvatarNames.ShownName(found)
                            : record.AvatarItemId,

                    // 同じ商品を別のバージョンで2回足せるので、何回入っているかを出す
                    UseCount = record.Members.Count(member =>
                        string.Equals(member.ItemId, Item.Id, StringComparison.Ordinal)),
                });
            }

            OnPropertyChanged(nameof(HasUsedInModifications));
        });
    }

    /// <summary>この商品にUnityへ送れるものが1つでもあるか。無ければ送り先の話もしない。</summary>
    public bool HasAnyUnityPackage => LocalFiles.Any(file => file.HasUnityPackages);

    /// <summary>
    /// 送り先の表示を読み直す。
    ///
    /// **Unityの開き閉じはこのアプリの外で起きる。**画面を組んだときの値を
    /// 持ち続けると、「開いていません」と出したまま実は開いている状態になる。
    /// ウィンドウが手前に戻ったら読み直す（<see cref="MainViewModel.NoteWindowActivated"/>）。
    /// </summary>
    public void NoteUnityChanged() => OnPropertyChanged(nameof(UnityTargetText));

    /// <summary>いま送るとどこへ行くか。押す前に見えている必要がある。</summary>
    public string UnityTargetText
    {
        get
        {
            var editors = Services.UnityEditors.Open();
            return editors.Count switch
            {
                0 => "Unityが開いていません（開いてから送れます）",
                1 => $"送り先：Unityの「{editors[0].ProjectName ?? "名前不明のプロジェクト"}」",
                // 窓を名指しして送るので、複数開いていても送るときに選べる（U14）
                _ => $"Unityが {editors.Count} つ開いています（送るときにどれへ送るか選べます）",
            };
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
                    SummaryText = $"{folder.FileCount} ファイル / {Core.Models.DisplayText.Size(folder.TotalBytes)}",
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

    public string AddImageTip => "この商品に画像を足す";

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

        var answer = System.Windows.MessageBox.Show(
            "この画像を消します。\n\n"
            + "ライブラリから消えるので、元に戻せません。\n"
            + "（元のファイルが手元にあれば、もう一度足せます）",
            "画像を消す",
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
            Title = "この商品に足す画像を選ぶ",
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
            RefreshStatus = $"画像を {added} 枚足しました。";
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
            nameof(SelectedImage), nameof(GalleryCounter), nameof(CurrentImage),
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
        => $"自分で足した画像 {Images.Count(image => image.IsUserAdded)} 枚";
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

    /// <summary>
    /// 商品IDをクリップボードへ。
    ///
    /// **IDそのものだけを写す。**画面には「ID 12345」と出ているが、
    /// その飾りごと写しても貼り先で使えない。
    /// </summary>
    private void CopyId()
    {
        try
        {
            System.Windows.Clipboard.SetText(Item.Id);
            RefreshStatus = $"{Item.Id} を写しました。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            RefreshStatus = "写せませんでした。もう一度押してください。";
        }
    }

    private void OpenBooth()
    {
        if (BoothClient.PageUrlFor(Item) is { } url)
        {
            TryStart(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }

    /// <summary>エクスプローラで開いて、そのファイルを選択した状態にする。</summary>
    /// <summary>zip を一時フォルダへ展開してエクスプローラで開く（#56）。</summary>
    public RelayCommand UnpackCommand { get; }

    private async Task UnpackAsync(LocalFileRow? row)
    {
        var zip = row?.Paths.FirstOrDefault(path =>
            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
        if (zip is null)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.UnpackToTemporary(zip));
        if (result is CommandResult.Unpacked unpacked)
        {
            TryStart(new ProcessStartInfo { FileName = unpacked.Folder, UseShellExecute = true });
        }
        else if (result is CommandResult.Failed failed)
        {
            System.Windows.MessageBox.Show(failed.Message, "展開して開く",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

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

}

public sealed class AttributeBar
{
    public required string Name { get; init; }

    public required int Value { get; init; }

    public double BarWidth => Value * 2.4;
}

/// <summary>商品ページに出す対応アバター1件（出品者の宣言）。</summary>
public sealed class AvatarRow : ChipTile
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    /// <summary>どこから拾ったか。推定を確定と同じ顔で出さないために添える。</summary>
    public required string SourceText { get; init; }

    public bool IsUnconfirmed { get; init; }

    /// <summary>このアバターを持っているか（U25）。札を「所持」の緑にして先頭へ寄せる。</summary>
    public bool IsOwned { get; init; }

    private System.Windows.Media.Imaging.BitmapSource? _icon;
    private bool _iconLoaded;

    /// <summary>ツールチップに出す絵を作るもの（R3）。</summary>
    public Func<System.Windows.Media.Imaging.BitmapSource?>? IconFactory { get; init; }

    /// <summary>ツールチップの絵。乗せたときに初めて読む。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = IconFactory?.Invoke();
            }

            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    /// <summary>
    /// どこから拾ったか、確認済みかをホバーで出す。
    /// 常時出すとチップが横に長くなり、1行に1〜2個しか入らなくなる。
    /// 所持は色だけに頼らず、ここでも言葉で言う。
    /// </summary>
    public string SourceTooltip => (IsOwned ? "持っているアバターです。" : string.Empty)
        + (IsUnconfirmed
            ? $"{SourceText}から拾いました（未確認）。押すとこのアバターを開きます"
            : $"{SourceText}から拾いました。押すとこのアバターを開きます");

    /// <summary>この対応は違う、と消すための操作。行にホバーしたときだけ出す。</summary>
    public RelayCommand? RejectCommand { get; init; }

    /// <summary>このアバターを開く（U13）。持っていれば商品ページ、持っていなければアバター画面で選んだ状態。</summary>
    public RelayCommand? OpenCommand { get; init; }
}

/// <summary>
/// 畳める欄の開き具合。商品ページと編集画面で共通にし、商品を移っても保つ（アプリを閉じるまで・ユーザ指示 2026-09-12）。
/// 既定は開く——畳むのは多過ぎる商品を見たときの操作で、普段は見えている方が早い。
/// </summary>
public static class SectionFolds
{
    public static bool BoothTagsExpanded { get; set; } = true;

    public static bool AvatarsExpanded { get; set; } = true;
}

/// <summary>ユーザが消した対応アバターの1行（「消したもの」の欄）。</summary>
public sealed class RejectedAvatarRow
{
    public required string Name { get; init; }

    /// <summary>この対応を戻す。出どころは「手入力」になる。</summary>
    public RelayCommand? RestoreCommand { get; init; }
}

