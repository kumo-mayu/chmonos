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

    /// <summary>
    /// このzipに入っている、Unityへ送れるもの。
    /// zipを開いて数えるので、商品ページを組むときに1回だけ読む。
    /// </summary>
    public IReadOnlyList<Core.Services.UnityPackageEntry> UnityPackages { get; init; } = [];

    public bool HasUnityPackages => UnityPackages.Count > 0;

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

        // 仮IDの商品はBOOTHに存在しない。押せてしまうと「取り直したのに何も変わらない」
        // という説明の付かない結果になるので、押せなくして理由をツールチップに置く
        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => !IsRefreshing && !item.IsLocalOnly);

        // 作者名からはアプリ内のショップ画面へ送る（BOOTHへは「BOOTHで開く」がある）。
        // 戻り先はこの商品ページにする。ショップ一覧へ返すと、来た道と違う場所に出てしまう
        // 自分で入れたショップにも飛べる。ショップ画面は鍵で束ねているので、
        // 手元だけの鍵でもその1店として開ける
        OpenShopCommand = new RelayCommand(
            () => _ = main.ShowShopAsync(
                item.ShopSubdomain!,
                (item.DisplayName, () => main.ShowItem(item, back))),
            () => item.ShopSubdomain is not null);
        // 仮IDの商品にはBOOTHページが無い。押せると404へ送ることになる
        OpenBoothCommand = new RelayCommand(OpenBooth, () => !item.IsLocalOnly);
        CopyIdCommand = new RelayCommand(CopyId);
        PreviousImageCommand = new RelayCommand(() => GoToImage(-1), () => CanGoPreviousImage);
        NextImageCommand = new RelayCommand(() => GoToImage(1), () => CanGoNextImage);
        MoveImageBackCommand = new RelayCommand(() => _ = MoveImageAsync(-1), () => CanMoveImageBack);
        MoveImageForwardCommand = new RelayCommand(() => _ = MoveImageAsync(1), () => CanMoveImageForward);
        PinThumbnailCommand = new RelayCommand(() => _ = PinThumbnailAsync(true), () => CurrentImage is not null && !CurrentIsPinned);
        UnpinThumbnailCommand = new RelayCommand(() => _ = PinThumbnailAsync(false), () => CurrentIsPinned);
        RemoveImageCommand = new RelayCommand(() => _ = RemoveImageAsync(), () => CurrentIsUserAdded);
        AddImageCommand = new RelayCommand(() => _ = AddImageAsync());
        ChangeIdCommand = new RelayCommand(() => _ = ChangeIdAsync());
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
        AddAvatarCommand = new RelayCommand(parameter => _ = AddAvatarAsync(parameter as string));
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
            () => CurrentImage is { IsImage: true });
        SetRoleModifiedCommand = new RelayCommand(
            () => _ = SetRoleAsync(Core.Models.ImageRole.Modified),
            () => CurrentImage is { IsImage: true });
        SetRoleOtherCommand = new RelayCommand(
            () => _ = SetRoleAsync(Core.Models.ImageRole.Other),
            () => CurrentImage is { IsImage: true });

        Sections = item.Booth.H2Sections.Select(section => new SectionRow(section)).ToList();

        BuildGallery();
        BuildVariations();
        BuildLocalFiles();
        BuildLocalFolders();

        // 改変はファイルを読むので待たない。空で描いてから埋まる
        _ = LoadModificationsAsync();
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

    public string OpenBoothTip => Item.IsLocalOnly
        ? "BOOTHに無い商品として登録したものなので、開く先がありません。"
        : "BOOTHの商品ページをブラウザで開きます。";

    public string RefreshButtonTip => Item.IsLocalOnly
        ? "BOOTHに無い商品として登録したものなので、取り直せません。"
        : "商品名・価格・バリエーション・説明文・画像をBOOTHから取り直します。"
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
    /// <summary>対応アバターを手で足す。検出が拾えなかったときの補い。</summary>
    public RelayCommand AddAvatarCommand { get; }

    /// <summary>「対応アバターを足す」の候補。既に宣言されているものは出さない。</summary>
    public IReadOnlyList<string> SupportSuggestions { get; private set; } = [];

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

    public RelayCommand OpenInExplorerCommand { get; }

    /// <summary>いま出ている画像に役割を付ける。3つで固定</summary>
    public RelayCommand SetRoleBoothCommand { get; }

    public RelayCommand SetRoleModifiedCommand { get; }

    public RelayCommand SetRoleOtherCommand { get; }

    /// <summary>zipの中の <c>.unitypackage</c> をUnityへ送る。</summary>
    public RelayCommand SendToUnityCommand { get; }

    public RelayCommand SendToUnityWithRecordCommand { get; }

    public RelayCommand AddToModificationCommand { get; }

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

        // 移した先の商品ページへ送る。元の商品はもう無いので、ここに残せない
        if (await _services.Store.Items.LoadAsync(toId) is { } moved)
        {
            _main.ShowItem(moved);
        }
        else
        {
            _main.ShowSearch();
        }
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

    public bool HasAvatars => Avatars.Count > 0;

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
        ? $"{Item.Local.LocalFiles.Count} 件 / {Core.Models.DisplayText.Size(Item.LogicalSizeBytes)}"
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
                Image = _thumbnails.Load(entry.Path),
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

        // 対応アバターの候補。既に宣言されているものは出さない
        var declared = Avatars.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        SupportSuggestions = registry.Entries
            .Where(entry => AvatarService.IsAvatar(entry) && !declared.Contains(entry.ItemId))
            .Select(entry => entry.DisplayName ?? entry.BoothName ?? entry.ItemId)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        OnPropertyChanged(nameof(SupportSuggestions));

        foreach (var name in new[]
        {
            nameof(Avatars), nameof(HasAvatars), nameof(AvatarBases), nameof(HasAvatarBases),
            nameof(AvatarSectionNote),
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
                Name = variation.Name ?? "（バリエーションなし）",
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
        foreach (var file in Item.Local.LocalFiles)
        {
            var variation = file.VariationId is null
                ? null
                : Item.Booth.Variations.FirstOrDefault(entry => entry.Id == file.VariationId)?.Name;

            LocalFiles.Add(new LocalFileRow
            {
                Hash = file.Hash,
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : "(見つかりません)",
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                Paths = file.Paths,
                VariationLabel = variation,
                VariationId = file.VariationId,
                UnityPackages = FindUnityPackages(file.Paths),
            });
        }
    }


    /// <summary>
    /// このファイルがzipなら、中の <c>.unitypackage</c> を数える。
    ///
    /// **1箇所目だけ見る。**同じ中身が複数箇所にあっても中身は同じなので、
    /// 全部開くのは無駄。zip以外（展開済みのフォルダやpdf）は対象外。
    /// </summary>
    private static IReadOnlyList<Core.Services.UnityPackageEntry> FindUnityPackages(IReadOnlyList<string> paths)
    {
        var path = paths.FirstOrDefault(File.Exists);
        return path is not null && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? Core.Services.UnityHandoff.FindPackages(path)
            : [];
    }

    /// <summary>
    /// Unityへ送る。
    ///
    /// **送り先はこちらで選べない。**Windowsが起動中のエディタへ転送するので、
    /// 開いていなければ何も起きない（Unity Hubの窓が出るだけ）。
    /// 押してから気付くのは最悪なので、先に数えて言い分ける。
    ///
    /// 複数開いているときは送らない。どれに入るかが分からないまま
    /// 取り込みを始めさせると、入れた先を間違えて後から剥がすことになる。
    /// </summary>
    /// <summary>
    /// 送り先のUnityを1つに絞る。絞れなければ理由を出して null を返す。
    ///
    /// **「改変に足して送る」と共通の門。**どちらのボタンでも同じ条件で
    /// 送れる／送れないが決まるべきで、片方だけ通ると挙動が読めなくなる。
    /// </summary>
    private Services.OpenUnityEditor? PickUnityTarget(string title)
    {
        var editors = Services.UnityEditors.Open();
        if (editors.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "送り先は、開いているUnityになります。\n\n"
                + "いまUnityが開いていないので送れません。プロジェクトを開いてから、もう一度押してください。",
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return null;
        }

        if (editors.Count > 1)
        {
            var names = string.Join("・", editors.Select(editor => editor.ProjectName ?? "名前の分からないプロジェクト"));
            System.Windows.MessageBox.Show(
                $"Unityが {editors.Count} つ開いています（{names}）。\n\n"
                + "どちらに入るかを選べないので、送るのをやめました。\n"
                + "入れたい方だけを開いた状態で、もう一度押してください。",
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return null;
        }

        return editors[0];
    }

    private void SendToUnity(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        if (PickUnityTarget("Unityへ送る") is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var answer = System.Windows.MessageBox.Show(
            $"「{package.Name}」を、Unityの「{target}」に送ります。\n\n"
            + "Unity側で取り込む内容の一覧が出るので、そこで確認してから取り込めます。",
            "Unityへ送る",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            Services.Shell.SendToUnity(package.VirtualPath);

            // 「使った」の足跡。Unityへ送ったことが一番強い証拠（ユーザ判断）。
            // Unity側で取り込みを取り消しても足跡は残るが、
            // 「送ろうとした」という事実は本当なので消さない
            _ = _services.Recent.TouchAsync(Item.Id, Core.Services.RecentKind.Used);
        }
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

        // **記録してから送る。**送るのはWindows任せで結果が返らないので、
        // 先に記録を確定させておく方が失うものが少ない
        var owner = LocalFiles.FirstOrDefault(file => file.UnityPackages.Contains(package));
        if (await CommitPickedModificationAsync(model, title, projectPath, owner, package.EntryPath)
            is not { } record)
        {
            return;
        }

        Services.Shell.SendToUnity(package.VirtualPath);
        _ = _services.Recent.TouchAsync(Item.Id, Core.Services.RecentKind.Used);

        UnityRecordNotice = $"「{record.Name}」に足して、Unityへ送りました。";
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

    /// <summary>ダイアログの中身を組む。送るときと足すだけのときで文言だけ変える。</summary>
    private async Task<PickModificationDialogViewModel> BuildPickModificationAsync(
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<Core.Models.ModificationRecord> records,
        string existingLabel,
        string commitLabel,
        string emptyText)
    {
        var registry = _services.Store.Avatars.Load();

        string NameOf(Core.Models.AvatarRegistryEntry entry)
            => entry.DisplayName ?? entry.BoothName ?? entry.ItemId;

        var avatarNames = registry.Entries
            .Select(NameOf)
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(text => text, StringComparer.CurrentCulture)
            .ToList();

        var rows = records
            .Select(record => new PickModificationRowViewModel
            {
                Record = record,
                AvatarText = registry.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                    is { } found
                        ? NameOf(found)
                        : record.AvatarItemId,
            })
            .ToList();

        await Task.CompletedTask;

        return new PickModificationDialogViewModel(
            title,
            headingText,
            contextText,
            rows,
            avatarNames,
            text => registry.Entries.FirstOrDefault(entry =>
                string.Equals(NameOf(entry), text, StringComparison.CurrentCultureIgnoreCase))?.ItemId)
        {
            ExistingLabel = existingLabel,
            CommitLabel = commitLabel,
            EmptyText = emptyText,
        };
    }

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
        var record = model.Picked?.Record;

        if (model.MakingNew)
        {
            if (model.NewAvatarItemId is not { } avatarItemId)
            {
                return null;
            }

            if (await _services.Commands.ExecuteAsync(
                    new Core.Commands.UiCommand.CreateModification(avatarItemId, model.NewName.Trim()))
                is Core.Commands.CommandResult.ModificationCreated created)
            {
                record = created.Record;

                // 作ったばかりの改変には紐付け先が無い。**いま送るプロジェクトで確定している**
                // ので、ここで付けておく（後から手で選ばせる意味が無い）
                if (project is not null)
                {
                    await _services.Commands.ExecuteAsync(
                        new Core.Commands.UiCommand.SetModificationProject(record.Id, project));
                }
            }
        }

        if (record is null)
        {
            System.Windows.MessageBox.Show(
                "改変を作れませんでした。名前を変えて、もう一度試してください。",
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
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

    /// <summary>改変の詳細へ。戻り先はこの商品にしておく（見比べに戻ってくる）。</summary>
    private void OpenModification(UsedInModificationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var item = Item;
        var back = _back;
        _main.ShowModification(
            row.Record,
            (BackLabelFor(item), () => _main.ShowItem(item, back)));
    }

    /// <summary>
    /// 戻る導線に載せる商品名。**長いものは詰める。**
    ///
    /// そのまま載せると上部バーを占領して、隣の情報（アバター名・作成日・更新日）を
    /// 押し出す。手元の15件で名前は中央28字・最長48字なので、
    /// **30字**にすると中央値は丸ごと入り、はみ出す5件だけが詰まる。
    /// </summary>
    private static string BackLabelFor(ItemRecord item)
    {
        const int limit = 30;

        var name = item.DisplayName;
        return name.Length <= limit ? $"{name} に戻る" : $"{name[..limit]}… に戻る";
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
                            ? found.DisplayName ?? found.BoothName ?? record.AvatarItemId
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
                _ => $"Unityが {editors.Count} つ開いています（1つだけにしてください）",
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

