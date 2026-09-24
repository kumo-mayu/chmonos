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

/// <summary>
/// 商品ページ。BOOTHの商品ページを参考にしつつ、ローカルの情報から組み立てる。
/// 閲覧専用にしているのは決定事項（編集はEdit画面へ一本化し、保存経路を1つに保つ）。
/// </summary>
public sealed partial class ItemViewModel : ViewModelBase, IInAppLinkNavigator, IGalleryHost, IPendingWrites
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

        // メモはこの画面でも書ける（I9）。編集画面の中に入れているときは、右の欄と二重になるので出さない
        ShowsMemoEditor = !forEditing;
        _memoDraft = item.Local.Memo ?? string.Empty;

        // 戻るは画面の履歴を遡る（U23）。以前は開くときに戻り先を1つ受け取っていた
        BackCommand = new RelayCommand(main.GoBack);

        // 仮IDの商品はBOOTHに存在しない。押せてしまうと「取り直したのに何も変わらない」
        // という説明の付かない結果になるので、押せなくして理由をツールチップに置く
        RefreshCommand = new RelayCommand(
            () => RefreshAsync().Forget(),
            () => !IsRefreshing && !item.IsLocalOnly);

        // 作者名からはアプリ内のショップ画面へ送る（BOOTHへは「BOOTHで開く」がある）。
        // 戻るとこの商品ページへ帰る（画面の履歴・U23）。
        // 自分で入れたショップにも飛べる。ショップ画面は鍵で束ねているので、
        // 手元だけの鍵でもその1店として開ける
        OpenShopCommand = new RelayCommand(
            () => main.ShowShopAsync(item.ShopSubdomain!).Forget(),
            () => item.ShopSubdomain is not null);
        // 仮IDの商品にはBOOTHページが無い。押せると404へ送ることになる
        OpenBoothCommand = new RelayCommand(OpenBooth, () => !item.IsLocalOnly);
        CopyIdCommand = new RelayCommand(CopyId);
        PreviousImageCommand = new RelayCommand(() => GoToImage(-1), () => CanGoPreviousImage);
        NextImageCommand = new RelayCommand(() => GoToImage(1), () => CanGoNextImage);
        // 商品ページの中でその場で直す操作は、取り込みの③が済むまで塞ぐ（U8・U10・ユーザ判断）。
        // 見る・Unityへ送る・改変に足す・お気に入りは塞がない（対応アバターの書き込みと取り合わない）
        MoveImageBackCommand = new RelayCommand(() => MoveImageAsync(-1).Forget(), () => CanMoveImageBack && !IsEditLocked);
        MoveImageForwardCommand = new RelayCommand(() => MoveImageAsync(1).Forget(), () => CanMoveImageForward && !IsEditLocked);
        PinThumbnailCommand = new RelayCommand(() => PinThumbnailAsync(true).Forget(), () => CurrentImage is not null && !CurrentIsPinned && !IsEditLocked);
        UnpinThumbnailCommand = new RelayCommand(() => PinThumbnailAsync(false).Forget(), () => CurrentIsPinned && !IsEditLocked);
        RemoveImageCommand = new RelayCommand(() => RemoveImageAsync().Forget(), () => CurrentIsUserAdded && !IsEditLocked);
        AddImageCommand = new RelayCommand(() => AddImageAsync().Forget(), () => !IsEditLocked);
        ChangeIdCommand = new RelayCommand(() => ChangeIdAsync().Forget(), () => !IsEditLocked);
        // 一度userTagを付けたitemは既定の編集キューに載らないので、ここから開く経路が要る
        EditCommand = new RelayCommand(() => main.ShowEditAsync([item.Id]).Forget(), () => !IsEditLocked);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, parameter => parameter is string);
        UnpackCommand = new RelayCommand(
            parameter => UnpackAsync(parameter as LocalFileRow).Forget(),
            parameter => parameter is LocalFileRow { CanUnpack: true });
        UnregisterFolderCommand = new RelayCommand(
            parameter => UnregisterFolderAsync(parameter as string).Forget(),
            parameter => parameter is string && !IsEditLocked);
        DetachFileCommand = new RelayCommand(
            parameter => DetachFileAsync(parameter as LocalFileRow).Forget(),
            parameter => parameter is LocalFileRow { IsDetached: false } && !IsEditLocked);
        ReattachFileCommand = new RelayCommand(
            parameter => ReattachFileAsync(parameter as LocalFileRow).Forget(),
            parameter => parameter is LocalFileRow { CanReattach: true } && !IsEditLocked);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);
        FetchImagesCommand = new RelayCommand(() => FetchImagesAsync().Forget(), () => HasMissingImages);
        AddAvatarCommand = new RelayCommand(parameter => AddAvatarAsync(parameter as string).Forget(), _ => !IsEditLocked);
        SendToUnityCommand = new RelayCommand(
            SendToUnity,
            parameter => parameter is Core.Services.UnityPackageEntry);
        SendToUnityWithRecordCommand = new RelayCommand(
            parameter => SendToUnityWithRecordAsync(parameter).Forget(),
            parameter => parameter is Core.Services.UnityPackageEntry);
        SelectInUnityCommand = new RelayCommand(
            parameter => SelectInUnityAsync(parameter).Forget(),
            parameter => parameter is Core.Services.UnityPackageEntry);
        AddToModificationCommand = new RelayCommand(() => AddToModificationAsync().Forget());
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
            () => SetRoleAsync(Core.Models.ImageRole.Booth).Forget(),
            () => CurrentImage is { IsImage: true } && !IsEditLocked);
        SetRoleModifiedCommand = new RelayCommand(
            () => SetRoleAsync(Core.Models.ImageRole.Modified).Forget(),
            () => CurrentImage is { IsImage: true } && !IsEditLocked);
        SetRoleOtherCommand = new RelayCommand(
            () => SetRoleAsync(Core.Models.ImageRole.Other).Forget(),
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

        // zip の中の unitypackage と、Unityのどこに入るかは読むのに時間が掛かるので待たない。行を出してから埋まる
        LoadUnityPackagesAsync().Forget();

        // 改変はファイルを読むので待たない。空で描いてから埋まる
        LoadModificationsAsync().Forget();
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
    ///
    /// 「BOOTHから取り直す」だと**ファイルをダウンロードするように読めた**（ユーザ指摘 2026-09-18）。
    /// 取り直すのは商品ページに載っている情報だけなので、そう名乗る
    /// </summary>
    public string RefreshButtonText => IsRefreshing ? "取り直しています…" : "商品情報を取り直す";

    public string OpenBoothTip => Item.IsLocalOnly
        ? "BOOTHに無い商品なので開けません。"
        : "BOOTHの商品ページをブラウザで開きます。アプリの外へ出ます。";

    public string RefreshButtonTip => Item.IsLocalOnly
        ? "BOOTHに無い商品なので取り直せません。"
        : "BOOTHの情報を取り直します。ファイルはダウンロードせず、自分の入力も残ります。";

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
                    ReplaceSelf(updated, $"「{updated.DisplayName}」の商品情報を取り直しました。");
                }
            });
        }
        catch (Exception exception)
        {
            // **どの失敗でも理由を出す。**前は IOException と HttpRequestException だけを受けていたので、
            // JSON が壊れていた・書き込めなかった といった失敗は、ボタンが元に戻るだけで
            // 何も起きなかったように見えていた（ログには Forget() が残す）。
            // 落ちた事実はログにも残す——画面の1行は消えるが、後から追えるようにしておく
            Core.Diagnostics.AppLog.Error("商品情報の取り直し", exception);
            // 例外の文はそのまま出さない（英語や内部のパスが混ざる）。見当と次の一手だけ（E5）
            RunOnUiThread(() => RefreshStatus = $"取り直せませんでした。{Core.Services.FailureText.Cause(exception)}");
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

    public string EditLockText => "取り込みの途中です。対応アバターの検出が終わると編集できます。";

    public string EditButtonTip => IsEditLocked ? EditLockText : "カテゴリ・ユーザータグ・属性などを直す画面を開きます";

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
    /// 組み込んだ持ち主が、このページをまだ出しているか。false なら開き直しが済んでも帯で知らせる。
    /// </summary>
    /// <remarks>
    /// <see cref="Replaced"/> を渡すと持ち主が差し替えを引き受けるので、単独の商品ページと違って
    /// 「離れた後に済んだ」ことが帯に出ていなかった（取り直しを待つ間に別の行・別の画面へ移ると、済んだことに気付けない）。
    /// 見えているかは持ち主にしか分からないので問い合わせる。null（編集画面）は今までどおり知らせない
    /// （編集画面は順番の中で読み直すので、前の商品の知らせを出すと次の商品の編集中に割り込む）。
    /// </remarks>
    public Func<bool>? IsShownByOwner { get; set; }

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

    /// <summary>戻るを出すか（V2）。組み込みのときと、戻る先が無いときは出さない。</summary>
    public bool ShowsBack => !IsEmbedded && _main.CanGoBack;

    /// <summary>組み込んだときは右側が窓より狭いので、単独の画面の最小幅（1060px）では横にはみ出す。</summary>
    /// <remarks>
    /// 単独の画面では、左の列の最小（460）＋右の列の最小320＋余白と内側の余白80。
    /// 覚えた左の幅（既定660・最大1100）で下限を決めていたときは、窓が狭いと右が約130pxまで潰れた。
    /// 今は覚えた幅を窓に合わせて頭打ちにする（PaneGrid）ので、下限は縮めきった形で決める（点検 2026-09-23）
    /// </remarks>
    public double BodyMinWidth => IsEmbedded ? 0 : LeftPane.MinPixels + 400;

    private PaneColumn? _leftPane;

    /// <summary>
    /// 左の列（ギャラリーと説明）。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。
    /// フォルダビューに組み込んだときも動かせる（ユーザ指示 2026-09-14：前は決め打ちの割合で動かせなかった）。
    /// 右側は窓より狭いので、組み込んだときは別の鍵（既定と範囲が小さい）で覚える
    /// </summary>
    public PaneColumn LeftPane => _leftPane ??= CreateLeftPane();

    /// <summary>組み込んだときに左の列の幅を覚える鍵。組み込む画面ごとに広さが違うので分ける（既定はフォルダビュー）。</summary>
    public string EmbeddedPaneKey { get; init; } = "folder.item.left";

    private PaneColumn CreateLeftPane()
    {
        var pane = new PaneColumn(_services.PaneWidths, IsEmbedded ? EmbeddedPaneKey : "item.left");
        pane.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BodyMinWidth));
        return pane;
    }

    /// <summary>開き直す。編集画面に入っていれば持ち主に任せ、商品ページなら画面ごと作り直す。</summary>
    /// <param name="awayText">このページを離れていたときに下の帯へ出す文。null なら「更新しました」。</param>
    /// <remarks>
    /// **このページがもう出ていなければ、画面を差し替えない。**取り直しは BOOTH の順番を待つので、
    /// 待つ間に別の画面へ移れる。前はそこへ商品ページを引き戻し、今いた画面は履歴に積まれず消えていた。
    /// 離れていたら帯で知らせるだけにする（登録し終えた商品の知らせと同じ考え方）。
    /// 組み込んだときの確かめは持ち主（<see cref="Replaced"/> を渡した画面）がする。
    /// </remarks>
    private void ReplaceSelf(ItemRecord? updated, string? awayText = null)
    {
        if (Replaced is { } replaced)
        {
            // 知らせるかは差し替えの前に聞く（持ち主が差し替えると、このページは出ていないことになる）。
            // 持ち主には知らせた後も渡す：木の読み直しや手元の控えの更新は、見えていなくても要る
            if (updated is not null && IsShownByOwner is { } isShown && !isShown())
            {
                _main.NoteItemChangedAway(updated, awayText ?? $"「{updated.DisplayName}」を更新しました。");
            }

            replaced(updated);
            return;
        }

        if (!ReferenceEquals(_main.CurrentViewModel, this))
        {
            // 消えた商品は開く先が無いので知らせない（検索の一覧からは読み直しで消えている）
            if (updated is not null)
            {
                _main.NoteItemChangedAway(updated, awayText ?? $"「{updated.DisplayName}」を更新しました。");
            }

            return;
        }

        if (updated is not null)
        {
            _main.ReplaceItem(updated);
        }
        else
        {
            // 登録を外した商品のページは履歴に積まない（取り直し・IDの付け替えと同じ扱い）。
            // 積むと、直後の「戻る」が無いものへ戻ろうとして更にもう1つ前へ飛ぶ
            _main.ReplaceWithSearch();
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

    public RelayCommand ToggleFavoriteCommand => _toggleFavoriteCommand ??= new RelayCommand(() => ToggleFavoriteAsync().Forget());

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

    /// <summary>unitypackage の入り先を、いま開いている Unity のプロジェクトタブで示す（入っていなければ言うだけ）。</summary>
    public RelayCommand SelectInUnityCommand { get; }

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

        var answer = Services.Notice.Show(
            $"次のフォルダの紐付けを解除します。\n\n{folderPath}\n\n"
            + "ファイルは消しません。以降このフォルダの中もスキャン対象に戻ります。",
            "フォルダの登録を外す",
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

        var answer = Services.Notice.Show(
            $"{row.FileName} をこの商品から外します。\n\n"
            + "ファイルは削除しません。未確定に戻り、正しい商品を選び直せます。\n"
            + "「この商品に戻す」で元に戻せます。",
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
                "これが最後のファイルです。この商品をどうしますか？",
                "「非表示にして残す（おすすめ）」\n検索に表示しなくなります。設定の「非表示にした商品」から戻せます。\n\n"
                + "「残す」\n商品の情報をそのまま残します。\n\n"
                + "「完全に削除」\nメモやユーザータグも削除します。元に戻せません。次の取り込みで、また登録されることがあります。",
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
        ? $"BOOTHの {shop.Subdomain} に自分で紐付けたショップです"
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
        ? "クリックすると仮のIDをコピーします"
        : "クリックすると商品IDをコピーします";

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
        => $"BOOTHに無い商品として、仮のID {Item.Id} で登録しています。BOOTHからは情報を取得しません。";

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
        ? "すべて折りたたむ"
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

    private string _memoDraft = string.Empty;
    private string _memoSaveText = string.Empty;
    private Debounced? _saveMemo;

    /// <summary>
    /// 商品のメモ。**ここで書けて、押さずに残る**（ユーザ判断 2026-09-20・I9）。
    /// 前は編集画面でしか書けず、しかもそこだけ「保存して次へ」で書く手動保存で、
    /// ほかのメモ（分類・属性・アバター・改変・ショップ）と作法が違っていた。
    /// 編集画面の中に入れているときは出さない（同じ物を2か所で書かせない）。
    /// </summary>
    /// <summary>メモの欄を出すか（編集画面の中に入れているときは出さない）。</summary>
    public bool ShowsMemoEditor { get; }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value ?? string.Empty))
            {
                // 打っている間は待ち、止まってから1回書く（ほかのメモと同じ 0.8 秒）
                MemoSaveText = "書いています…";
                (_saveMemo ??= new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync)).Request();
            }
        }
    }

    /// <summary>
    /// 保存できているかを出す（ユーザ判断 2026-09-20・I10）。押さずに書く欄は、
    /// **書けたのかどうかが画面から分からない**（0.8 秒待つので、打ってすぐ閉じると落ちる）。
    /// </summary>
    public string MemoSaveText
    {
        get => _memoSaveText;
        private set
        {
            if (SetField(ref _memoSaveText, value))
            {
                OnPropertyChanged(nameof(HasMemoSaveText));
            }
        }
    }

    public bool HasMemoSaveText => MemoSaveText.Length > 0;

    private async Task SaveMemoAsync()
    {
        var memo = _memoDraft.Trim();
        var result = await _services.Commands.ExecuteAsync(new UiCommand.SaveItemLocal(
            Item.Id,
            Item.Local with { Memo = memo.Length == 0 ? null : memo },
            LocalOwners.ItemPageMemo));

        MemoSaveText = result is CommandResult.Failed failed ? failed.Message : "保存しました。";

        if (await _services.Store.Items.LoadAsync(Item.Id) is { } saved)
        {
            Item = saved;
            OnPropertyChanged(nameof(Memo));
            OnPropertyChanged(nameof(HasMemo));
            _main.Search.NoteItemChanged(saved);
        }
    }

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前に呼ぶ。I10：打ってすぐ閉じると落ちていた）。</summary>
    public Task FlushPendingWritesAsync() => _saveMemo?.RunNowAsync() ?? Task.CompletedTask;

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

    /// <summary>
    /// この画面が決めた項目だけを書く。<paramref name="owns"/> に無い項目は、
    /// 保存の直前に読み直したものが残る。開いている間に検出や取り込みが書いたものを、
    /// 古い写しで潰さないため。
    /// </summary>
    private async Task SaveLocalAsync(LocalBlock local, IReadOnlyCollection<LocalField> owns)
    {
        await _services.Commands.ExecuteAsync(new UiCommand.SaveItemLocal(Item.Id, local, owns));

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            Item = reloaded;
            BuildAvatars();
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
            OpenLinkedItemAsync(itemId).Forget();
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
            RefreshStatus = $"{Item.Id} をコピーしました。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            RefreshStatus = "コピーできませんでした。もう一度押してください。";
        }
    }

    private void OpenBooth()
    {
        if (BoothClient.PageUrlFor(Item) is { } url)
        {
            TryStart(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }

}
