using System.Collections.ObjectModel;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変の一覧の1行。
///
/// **同じ名前を許してあるので、日付が見分けの手掛かり**（`docs/history/modifications.md` Q27）。
/// </summary>
/// <summary>一覧の1行。</summary>
public sealed class AvatarRowViewModel : ViewModelBase, IHasItemCard
{
    public required AvatarSummary Summary { get; init; }

    private ItemRecord? _item;

    /// <summary>この行の商品（アバターは商品でもある）。右クリックをカードと同じにするために持つ（M2）。手元に無ければ null。</summary>
    public ItemRecord? Item
    {
        get => _item;
        init => _item = value;
    }

    /// <summary>
    /// 検索がまだ商品を読み終えていないか。読み終えるまでは、商品を引けなくても「手元にありません」と言い切らない
    /// （起動直後に開くと、持っているアバターまで手元に無いと出ていた。2026-10-02）
    /// </summary>
    public bool IsItemPending { get; private set; }

    /// <summary>名前だけの札に「この名前の商品は手元にありません」を添えるか。</summary>
    public bool ShowsMissingNote => !IsItemPending;

    /// <summary>
    /// 検索の読み込みが済んだときに、この行の商品を引き直す。**行を作った時点の写しに頼らない**——
    /// 画面を開いたのが検索の読み込みより先だと、持っているアバターまで名前だけの札のまま残っていた（2026-10-02）。
    /// 手元に有る／無いが変わったときだけカードを作り直す（取り込み中は10秒ごとに読み直すので、毎回全行のカードを作り直さない）
    /// </summary>
    public void RefreshItem(ItemRecord? item, bool pending)
    {
        if (IsItemPending != pending)
        {
            IsItemPending = pending;
            OnPropertyChanged(nameof(IsItemPending));
            OnPropertyChanged(nameof(ShowsMissingNote));
        }

        if ((_item is null) == (item is null))
        {
            return;
        }

        _item = item;
        _card = null;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Card));
        OnPropertyChanged(nameof(HasCard));

        // 頭の絵も、持っているアバターは商品の1枚目から探す（持っていないときは控えの1枚）
        RefreshImages();
    }

    /// <summary>カードを作るのに要る物（画像の置き場と設定）。</summary>
    public AppServiceContainer? Services { get; init; }

    /// <summary>
    /// カードを作るもの。**検索画面と同じ作り方を通す**（タグ・属性の管理と同じ）。
    /// 自分で <see cref="ItemCardViewModel"/> を組むと、所持・容量・札の値を入れ忘れて
    /// 所持している商品にまで「未所持」の襷が掛かった（ユーザ指摘 2026-09-20）。
    /// </summary>
    public Func<ItemCardViewModel?>? CardFactory { get; init; }

    private ItemCardViewModel? _card;

    /// <summary>右クリックとカード表示で使うカード。**要るときに初めて作る**（一覧は数百行並ぶ）。</summary>
    public ItemCardViewModel? Card => _card ??= Item is null ? null : CardFactory?.Invoke();

    /// <summary>
    /// 商品のカードを出せるか。**名前が挙がっただけのアバターには商品が無い**ので、
    /// そのときカードの型を当てると、中の札の結び付け先が全部外れて既定の「出す」になり、
    /// 「所持」と「未所持」が同時に出ていた（ユーザ指摘 2026-09-20）。名前だけの札に切り替える。
    /// </summary>
    public bool HasCard => Card is not null;

    /// <summary>一覧のグループ見出し。所有しているものを先に固めて出す。</summary>
    public string GroupName { get; set; } = string.Empty;

    public string ItemId => Summary.Entry.ItemId;

    /// <summary>絵を読むもの。一覧は見えている行しか作らないので、絵も見えた行だけで読む（U18）。</summary>
    public Chmonos.App.Services.ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>絵の場所を探すもの（U18）。</summary>
    public Func<string?>? IconPathFactory { get; init; }

    private string? _iconPath;
    private bool _iconPathLoaded;

    /// <summary>
    /// 絵の場所。**見えた行で初めて探す。**一覧を組むときに約600体ぶん探すと、
    /// アバター画面を開くのが遅れる疑いがあった（開いてから一覧が出るまで4.6〜8.0秒）
    /// </summary>
    private string? IconPath
    {
        get
        {
            if (!_iconPathLoaded)
            {
                _iconPathLoaded = true;
                _iconPath = IconPathFactory?.Invoke();
            }

            return _iconPath;
        }
    }

    /// <summary>頭に絵を出すか。無ければ頭文字を出す（U18）。</summary>
    public bool HasIcon => IconPath is not null;

    /// <summary>
    /// 裏の取得がこの商品の画像を置いた。頭の絵の場所を探し直し、カードも引き直す（洗い出し 6。
    /// 場所は見えた行で一度だけ探すので、取り込みの④⑤の最中に開くと、知らせないと頭文字のまま残る）
    /// </summary>
    public void RefreshImages()
    {
        _iconPathLoaded = false;
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(Icon));
        _card?.RefreshImages();
    }

    /// <summary>頭の絵。持っているアバターは商品の1枚目、持っていないアバターは控えの1枚（U18）。</summary>
    /// <remarks>裏で読み、届いたら描き直す。その場で読むと、画面を開くのが遅れた（<see cref="Chmonos.App.Services.ThumbnailLoader.PeekForTile"/>）。</remarks>
    public System.Windows.Media.Imaging.BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>画面に出す名前。手で付けた名前か、正式名から計算した名前（同じ名前ならショップ名付き）。</summary>
    public string Name => string.IsNullOrWhiteSpace(Summary.Name) ? Summary.Entry.ItemId : Summary.Name;

    public bool IsOwned => Summary.IsOwned;

    /// <summary>手で「アバターとして扱わない」にしたもの。一覧には残すが、そうと分かるようにする。</summary>
    public bool IsExcluded => Summary.Entry.AvatarOverride == false;

    public string BaseText => Summary.Entry.BaseName ?? string.Empty;

    public bool HasBase => !string.IsNullOrWhiteSpace(Summary.Entry.BaseName);

    /// <summary>
    /// 手で決めた素体が無く、名前から推した素体だけがあるときの、一覧の行に出す薄い字（ユーザ判断 2026-10-05）。
    /// 手で決めた素体の字（<see cref="BaseText"/>）と見分けて出す。出さないと、詳細では「入っています」と言うのに行には素体名が無く食い違う
    /// </summary>
    public string InferredBaseText => Summary.InferredBaseName is { Length: > 0 } name ? $"名前から「{name}」" : string.Empty;

    public bool HasInferredBase => InferredBaseText.Length > 0;

    /// <summary>直接対応と素体経由は分けて出す。素体経由は推定なので同じ顔で並べない。</summary>
    public string CountText => Summary.ViaBaseCount > 0
        ? $"{Summary.DirectCount} + 素体経由 {Summary.ViaBaseCount}"
        : $"{Summary.DirectCount}";

    /// <summary>飾り記号を飛ばした頭文字。そのままだと「【」ばかり並ぶ</summary>
    public string Initial => AvatarText.InitialOf(Name);

    /// <summary>BOOTHの正式名。短い表示名だけでは分からないときのために持ち回る</summary>
    public string BoothName => Summary.Entry.BoothName ?? string.Empty;

    /// <summary>
    /// この行を引ける語。表示名・商品ID・BOOTHの正式名・別名。
    ///
    /// 表示名だけで引けると思うと**引けない場面がある**。表示名は短くしてあるので、
    /// BOOTHの正式名の一部で探すと当たらない。VRChatでは商品IDで探す習慣もある。
    /// 別名（誤記や略称の受け皿）も入れておく。
    /// </summary>
    public bool Matches(string query)
        => AvatarSearch.Matches(Summary.Entry, Name, query, out _);

    private string _matchNote = string.Empty;

    /// <summary>
    /// 探す欄の語が名前でなく呼び方・正式名で当たったときの「何で当たったか」。名前で当たった行・語が空のときは空。
    /// 名前に無い語で当たると、なぜこの行が残ったのか分からないため（候補の欄の「正式名「…」」と同じ作り）
    /// </summary>
    public string MatchNote
    {
        get => _matchNote;
        private set
        {
            if (_matchNote != value)
            {
                _matchNote = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMatchNote));
            }
        }
    }

    public bool HasMatchNote => _matchNote.Length > 0;

    /// <summary>探す語に合わせて札を付け替える。<see cref="Matches"/> と同じ照らし方（<see cref="AvatarSearch.Matches"/>）の答えを使う。</summary>
    public void UpdateMatchNote(string query)
        => MatchNote = query.Length > 0 && AvatarSearch.Matches(Summary.Entry, Name, query, out var hint)
            ? hint?.Label ?? string.Empty
            : string.Empty;
}

/// <summary>素体グループの1行。</summary>
public sealed class AvatarBaseRowViewModel : ViewModelBase
{
    public required AvatarBaseSummary Summary { get; init; }

    public string Name => Summary.Group.Name;

    private string _nameInput = string.Empty;

    /// <summary>
    /// 名前の欄。**アバターの名前と同じく欄の中で変える**（ユーザ指示 2026-09-20・B10）。
    /// ここだけ OS の古い入力窓（InputBox）で、同じ画面の中で名前を変える作法が2つに割れていた。
    /// </summary>
    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                OnPropertyChanged(nameof(HasNameChange));
                NameNote.Clear();
            }
        }
    }

    /// <summary>名前の欄のすぐ下の知らせ。行ごとに持つので、別の素体を選べば消える（2026-10-04）</summary>
    public AreaNotice NameNote { get; } = new();

    /// <summary>素体の商品の欄のすぐ下の知らせ</summary>
    public AreaNotice ItemIdNote { get; } = new();

    /// <summary>アバターを足す欄のすぐ下の知らせ（ほかの素体から移した・足せなかった。メモ46）</summary>
    public AreaNotice MemberNote { get; } = new();

    /// <summary>欄を今の名前から変えたか。「名前を変える」はそのときだけ押せる（アバターと同じ作法）。</summary>
    public bool HasNameChange => NameInput.Trim().Length > 0 && NameInput.Trim() != Name;

    private bool _isEditingName;

    /// <summary>
    /// 名前を欄にしているか。普段は名前を文字で出し、「名前を変更」を押したときだけ欄にする
    /// （ユーザ判断 2026-09-29：常に欄だと、名前を変えられることが分からなかった）。アバターの名前と同じ作法
    /// </summary>
    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetField(ref _isEditingName, value);
    }

    private RelayCommand? _startRenameCommand;

    public RelayCommand StartRenameCommand => _startRenameCommand ??= new RelayCommand(() =>
    {
        NameInput = Name;
        IsEditingName = true;
    });

    private RelayCommand? _cancelRenameCommand;

    /// <summary>取り消したら欄を今の名前に戻す。打ちかけを残すと、次に開いたときに前の打ちかけが出る</summary>
    public RelayCommand CancelRenameCommand => _cancelRenameCommand ??= new RelayCommand(() =>
    {
        IsEditingName = false;
        NameInput = Name;
    });

    public string MemberText => $"アバター {Summary.MemberCount}（所有 {Summary.OwnedMemberCount}）";

    public string ItemText => $"この素体向けと書かれた商品 {Summary.ItemCount} 件";

    public bool InferClothing => Summary.Group.InferClothing;

    public bool HasItemId => !string.IsNullOrWhiteSpace(Summary.Group.ItemId);

    public string ItemIdText => HasItemId ? $"素体の商品：{Summary.Group.ItemId}" : "素体の商品：なし";

    private string _itemIdInput = string.Empty;

    /// <summary>
    /// 配布されている素体の商品ID。BOOTHの商品URLを貼っても読む。
    /// 結んでおくと、その素体のページへ行けるようになり、所有の判定にも使える。
    /// </summary>
    public string ItemIdInput
    {
        get => _itemIdInput;
        set
        {
            if (SetField(ref _itemIdInput, value))
            {
                ItemIdNote.Clear();
            }
        }
    }

    public RelayCommand? ToggleInferCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }

    public RelayCommand? SetItemIdCommand { get; set; }

    /// <summary>素体そのものの商品の候補（名前に素体名を含み、アバターではない登録簿の商品）。押すと結ぶ（自動では結ばない）。</summary>
    public IReadOnlyList<BaseItemCandidate> ItemCandidates { get; init; } = [];

    public bool ShowsItemCandidates => !HasItemId && ItemCandidates.Count > 0;
}

/// <summary>素体の商品の候補1件。</summary>
public sealed record BaseItemCandidate(string ItemId, string Label, RelayCommand UseCommand);

/// <summary>
/// アバターの管理。
///
/// 一覧は所有しているアバターを先に出す。検出が育つと未所有のアバターが何百と並ぶので、
/// 素体でグループ化した一覧にすると「素体の指定なし」に大半が落ちて読めなくなる
/// （実データでは独自素体が大半）。素体の管理は別の欄に分ける。
/// </summary>
public sealed partial class AvatarsViewModel : ViewModelBase, IPendingWrites, ILeavingScreen, IItemCardHost, IItemImagesListener
{
    /// <summary>名前の候補を出す数。並べすぎると選べない。</summary>
    private const int MaxNameSuggestions = 5;

    private readonly AppServiceContainer _services;

    /// <summary>
    /// 離れたら一覧の読み直しを取り消す（既知 P8）。この画面は開くたびに作り直すので、離れた後の一覧は誰も見ない。
    /// **取り消すのは読み直しだけ。**押した書き込み（名前・素体・メモ）は読み直しの前に済んでいるので止まらない
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    public void OnLeaving()
    {
        _leaving.Cancel();

        // 主画面はアプリと同じ寿命、この画面は開くたびに作り直す。外さないと捨てた画面が知らせを受け続ける
        _main.PropertyChanged -= OnMainChanged;
        _main.Search.PropertyChanged -= OnSearchChanged;
    }

    /// <summary>
    /// 検索がまだ商品を一度も読み終えていないか。読み直しの最中は前に読んだ商品を引けるので、待つのは最初の1回だけ
    /// （検索は作られた時点で読み始め、読み終えるまで <see cref="SearchViewModel.IsLoading"/> が立っている）
    /// </summary>
    private bool ItemsPending => _main.Search.IsLoading && _main.Search.TotalCount == 0;

    /// <summary>
    /// 検索の読み込みが済んだら、行の商品と右の欄の所有を引き直す（2026-10-02）。
    /// 行は開いた時点の検索の写しで商品を引いていて、検索より先に開くと、持っているアバターまで
    /// 「この名前の商品は手元にありません」の札になり、右の欄も「所有していない」と出ていた
    /// </summary>
    private void OnSearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SearchViewModel.IsLoading))
        {
            return;
        }

        var pending = ItemsPending;
        foreach (var row in _all)
        {
            row.RefreshItem(_main.Search.FindItem(row.ItemId), pending);
        }

        foreach (var name in new[]
                 {
                     nameof(IsOwnedByFile), nameof(ShowsOwnedToggle), nameof(SelectedOwnedText), nameof(CanOpenItem),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.LongJobBlockedNote))
        {
            OnPropertyChanged(nameof(DetectHint));
        }
    }
    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "avatars.list");
    private readonly MainViewModel _main;

    private ManageItemView? _itemView;

    /// <summary>一覧をカードで出すかリストで出すか（ユーザ指示 2026-09-20・M4）。ほかの画面と同じ切り替え。</summary>
    private ManageItemView ItemView => _itemView ??= new ManageItemView(_services, "avatar", () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
    });

    public bool IsCardMode => ItemView.IsCardMode;

    public bool IsListMode => ItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => ItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => ItemView.Set(true));

    private AvatarRowViewModel? _selected;
    private bool _isLoading = true;
    private bool _isDetecting;
    private string _status = string.Empty;
    private string _query = string.Empty;
    private string _baseInput = string.Empty;
    private string _aliasInput = string.Empty;
    private string _nameInput = string.Empty;
    private List<AvatarRowViewModel> _all = [];

    void IItemImagesListener.NoteItemImagesSaved(string itemId)
    {
        foreach (var row in _all)
        {
            if (string.Equals(row.ItemId, itemId, StringComparison.Ordinal))
            {
                row.RefreshImages();
            }
        }
    }

    /// <summary>開いたときに選んでおくアバター。最初の読み込みで1回だけ使う。</summary>
    private string? _openWith;

    /// <summary>開いたときに素体の詳細で開いておく素体。最初の読み込みで1回だけ使う。</summary>
    private string? _openBaseWith;

    /// <param name="selectItemId">
    /// 開いたときに選んでおくアバター（商品ページの対応アバターの札から来たとき、U13）。
    /// </param>
    /// <param name="selectBaseName">
    /// 戻るで素体の詳細へ戻すとき、開いておく素体（共通素体の見方で開く）。
    /// </param>
    public AvatarsViewModel(AppServiceContainer services, MainViewModel main, string? selectItemId = null, string? selectBaseName = null)
    {
        _services = services;
        _main = main;
        _openWith = selectItemId;
        _openBaseWith = selectBaseName;

        // ほかの長い作業（書き出し・移動・候補の検索など）が走っている間は押せない。帯は1本しか持てないので、
        // 重ねると後から始めた方が帯と「中止」の宛先を奪い、先に終わった方が帯ごと消していた（ユーザ判断 2026-10-01）
        DetectCommand = new RelayCommand(() => DetectAsync().Forget(), () => !IsDetecting && !_main.IsLongJobRunning);
        _main.PropertyChanged += OnMainChanged;
        _main.Search.PropertyChanged += OnSearchChanged;
        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);
        SetBaseCommand = new RelayCommand(() => SetBaseAsync().Forget());

        // 候補付きの欄から決める（I6）。候補を押しても、打った新しい名前を Enter で決めても、ここへ来る
        PickBaseCommand = new RelayCommand(parameter =>
        {
            if (parameter is string name)
            {
                BaseInput = name;
                SetBaseAsync().Forget();
            }
        });
        ClearBaseCommand = new RelayCommand(() => ClearBaseAsync().Forget(), CanClearBase);
        AddAliasCommand = new RelayCommand(() => AddAliasAsync().Forget());
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        RenameCommand = new RelayCommand(() => RenameAsync().Forget());
        UseNameSuggestionCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is string name)
                {
                    NameInput = name;
                    IsEditingName = true;
                }
            },
            parameter => parameter is string);
        RemoveAliasCommand = new RelayCommand(parameter => RemoveAliasAsync(parameter as string).Forget());
        ToggleOwnedCommand = new RelayCommand(() => ToggleOwnedAsync().Forget());
        OpenItemCommand = new RelayCommand(() => OpenItemAsync().Forget());
        RecheckCommand = new RelayCommand(() => RecheckAsync().Forget());
        TreatAsAvatarCommand = new RelayCommand(parameter => SetOverrideAsync(parameter as string).Forget());
        OpenBoothCommand = new RelayCommand(parameter => OpenBoothPage(parameter));
        ShowItemsCommand = new RelayCommand(ShowItems);
        CreateModificationCommand = new RelayCommand(
            () => CreateModificationAsync().Forget(),
            () => !_creatingModification && Selected is not null && ModificationNameInput.Trim().Length > 0);
        // 改変の行（絵・名前）と使ったものの行は、どれも改変に入るだけ。ここには右のビューが無い（ユーザ判断 2026-09-17）
        OpenModificationCommand = new RelayCommand(
            parameter =>
            {
                var record = parameter switch
                {
                    HubModificationRow row => row.Record,
                    HubMemberRow member => member.Record,
                    _ => null,
                };
                if (record is not null)
                {
                    // 戻り先をアバターの管理にしておく。改変からは必ずここへ帰る
                    _main.ShowModification(record);
                }
            },
            parameter => parameter is HubModificationRow or HubMemberRow);
        RestoreAliasCommand = new RelayCommand(parameter => RestoreAliasAsync(parameter as string).Forget(), parameter => parameter is string);

        // 既定のビューに見出しを付ける。ListBoxはこのビューを通して並べる
        System.Windows.Data.CollectionViewSource.GetDefaultView(Rows).GroupDescriptions.Add(
            new System.Windows.Data.PropertyGroupDescription(nameof(AvatarRowViewModel.GroupName)));

        LoadAsync().Forget();
    }

    /// <summary>
    /// 一覧は1つにまとめ、所有/未所有はグループ見出しで分ける。
    /// ListBoxを2つ縦に積むと、ScrollViewerの中で高さが無限になって描画が壊れる。
    /// </summary>
    public RangeObservableCollection<AvatarRowViewModel> Rows { get; } = [];

    public ObservableCollection<AvatarBaseRowViewModel> Bases { get; } = [];

    public ObservableCollection<string> BaseNames { get; } = [];

    public RelayCommand DetectCommand { get; }

    public RelayCommand SetBaseCommand { get; }

    /// <summary>候補付きの欄から共通素体を決める（I6）。</summary>
    public RelayCommand PickBaseCommand { get; }

    public RelayCommand ClearBaseCommand { get; }

    public RelayCommand AddAliasCommand { get; }


    public RelayCommand RenameCommand { get; }

    public RelayCommand RemoveAliasCommand { get; }

    public RelayCommand ToggleOwnedCommand { get; }

    /// <summary>
    /// 取り込んであるアバターの商品ページを開く（動線の点検 D2）。逆向き（商品ページの対応アバターの札から、
    /// 持っていれば商品ページ）はあったが、アバターの画面からファイルや改変を見に行く道が無かった
    /// </summary>
    public RelayCommand OpenItemCommand { get; }

    /// <summary>
    /// 商品として取り込んであるか。手で「所有として扱う」にしただけのアバターは、開く商品ページが無い
    /// </summary>
    public bool CanOpenItem => Selected is not null && _services.Store.Items.Exists(Selected.ItemId);

    private async Task OpenItemAsync()
    {
        if (Selected is not null && await _services.Store.Items.LoadAsync(Selected.ItemId) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    public RelayCommand RecheckCommand { get; }

    public RelayCommand TreatAsAvatarCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand ShowItemsCommand { get; }

    private RelayCommand? _backCommand;

    /// <summary>
    /// 戻る（U23）。商品ページの対応アバターの札から持っていないアバターを開くと、
    /// 元の商品へ戻る手段が無かった。行き先は画面の履歴の直前の画面
    /// </summary>
    public RelayCommand BackCommand => _backCommand ??= new RelayCommand(_main.GoBack);

    public string BackText => _main.BackButtonText;

    public bool CanGoBack => _main.CanGoBack;

    // ---- 改変の記録 ----

    /// <summary>選んでいるアバターの改変。新しく作った順。</summary>
    public ObservableCollection<HubModificationRow> Modifications { get; } = [];

    public bool HasModifications => Modifications.Count > 0;

    public RelayCommand CreateModificationCommand { get; }

    /// <summary>改変の詳細を開く。商品ページと同じ格の画面へ差し替える</summary>
    public RelayCommand OpenModificationCommand { get; }

    public RelayCommand RestoreAliasCommand { get; }

    private string _modificationNameInput = string.Empty;

    /// <summary>作る改変の名前。「普段着」「制服」のような呼び分け。</summary>
    public string ModificationNameInput
    {
        get => _modificationNameInput;
        set
        {
            if (SetField(ref _modificationNameInput, value))
            {
                ModificationNote.Clear();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 改変が無いときに出す文。
    ///
    /// **何のための場所かを書く。**空欄だけだと、使い方が分からないまま放置される
    /// （`usedOn` が15件中0件だったのと同じ道を避ける）。
    /// </summary>
    public string ModificationEmptyText =>
        "まだありません。着せ替えごとに1つ作ると、使った衣装やギミックをまとめて残せます。";

    private async Task LoadModificationsAsync()
    {
        Modifications.Clear();

        if (Selected is { } row)
        {
            // 改変の画面のアバターの項目と同じ行（絵・名前・Unityプロジェクト・畳んだ使ったもの）。絵の読み込みに商品が要る
            // 同じ ID が2件あると ToDictionary が投げ、改変の一覧が黙って空になった。改変の画面（ModificationHubViewModel）と同じく先の1件を使う
            var items = _main.Search.SnapshotItems()
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var builder = new ModificationRowBuilder(_services, _main.Thumbnails, items);
            foreach (var record in await _services.Modifications.LoadForAvatarAsync(row.ItemId))
            {
                Modifications.Add(builder.Build(record, $"avatars:mod:{record.Id}", openByDefault: false,
                    row.Name, showsAvatar: false, showsProject: true));
            }
        }

        OnPropertyChanged(nameof(HasModifications));
    }

    private bool _creatingModification;

    private async Task CreateModificationAsync()
    {
        if (_creatingModification || Selected is not { } row)
        {
            return;
        }

        // **作っている間は押せない**（外部の点検 2026-10-06）。同じ名前の確かめと保存を待つ間に2回目が入ると、
        // 両方が「まだ無い」と判断して、同じアバター・同じ名前の改変が2つできた。印は最初の待ちより前に立てる
        _creatingModification = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            await CreateModificationCoreAsync(row);
        }
        finally
        {
            _creatingModification = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CreateModificationCoreAsync(AvatarRowViewModel row)
    {
        var name = ModificationNameInput.Trim();

        // **同じ名前を許すが、黙って2つ並べない。**
        // 作り直したいのか、間違えて2つ目を作ろうとしているのかは人にしか分からない
        if (await _services.Modifications.HasSameNameAsync(row.ItemId, name))
        {
            var answer = Services.Notice.Show(
                $"「{name}」という改変が既にあります。\n\n"
                + "同じ名前でも作れます。\n"
                + "一覧では作った日付で見分けられます。",
                "同じ名前の改変があります",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.Cancel);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }
        }

        if (await WriteAsync(new UiCommand.CreateModification(row.ItemId, name), "改変を作れませんでした。", ModificationNote.Warn) is null)
        {
            return;
        }

        ModificationNameInput = string.Empty;
        ModificationNote.Show($"改変「{name}」を作りました。");
        await LoadModificationsAsync();
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasNoItems));
                OnPropertyChanged(nameof(CanDetectFromItems));
            }
        }
    }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (SetField(ref _isDetecting, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(DetectButtonText));
                OnPropertyChanged(nameof(DetectHint));
            }
        }
    }

    public string DetectButtonText => IsDetecting ? "検出しています…" : "対応アバターを検出する";

    /// <summary>
    /// 検出のボタンの吹き出し。ほかの長い作業で押せない間は、その理由に差し替える（黙って押せなくしない）。
    /// 自分が検出している間はボタンの文が「検出しています…」になるので、普段の説明のまま。
    /// </summary>
    public string DetectHint => !IsDetecting && _main.IsLongJobRunning
        ? _main.LongJobBlockedNote
        : "説明文などから読み取ります。カテゴリ不明の商品はBOOTHに問い合わせます。";

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>1体も見つかっていないとき。数字ではなく次にやることを出す。</summary>
    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    /// <summary>
    /// **探して0件**か（E2）。そもそも1体も無いのと言い分ける（検索・フォルダ・改変と同じ3通りの書き分け）。
    /// 絞った結果を「まだ見つかっていません・検出してください」と言うと、
    /// 打ち込んだ言葉のせいで消えているだけなのに、登録簿が空だと読める。
    /// </summary>
    public bool IsFilteredEmpty => IsEmpty && _query.Trim().Length > 0;

    /// <summary>
    /// 商品を1件も取り込んでいないか。そのときは検出を押しても何も見つからないので、先に取り込みへ案内する
    /// （ユーザ判断 2026-09-17：空表示には次にやることを書く）
    /// </summary>
    public bool HasNoItems => IsEmpty && !IsFilteredEmpty && _main.Search.TotalCount == 0;

    public bool CanDetectFromItems => IsEmpty && !IsFilteredEmpty && !HasNoItems;

    public string EmptyTitle => IsFilteredEmpty
        ? "当てはまるアバターがありません"
        : "対応アバターがまだ見つかっていません";

    /// <summary>探して0件のときだけ、絞り込みを消して戻す。</summary>
    public RelayCommand ClearQueryCommand { get; }

    public RelayCommand ShowImportCommand => _main.ShowImportCommand;

    public bool HasBases => Bases.Count > 0;

    // ---- 見る物の切り替え（U16） ----
    //
    // 以前は共通素体の管理を、どのアバターを選んでも右の詳細の下に常に出していた。
    // アバター1体のことを見ている最中に全素体の一覧が付いて回り、どちらの話か分からなかった。
    // 左の一覧の上で「アバター／共通素体」を切り替え、右はそのとき選んでいる1つのことだけを出す

    private bool _isBaseMode;
    private AvatarBaseRowViewModel? _selectedBase;
    private RelayCommand? _showAvatarModeCommand;
    private RelayCommand? _showBaseModeCommand;
    private RelayCommand? _openMemberCommand;

    /// <summary>左の一覧に共通素体を並べているか。既定はアバター。</summary>
    public bool IsBaseMode
    {
        get => _isBaseMode;
        set
        {
            if (SetField(ref _isBaseMode, value))
            {
                OnPropertyChanged(nameof(ShowsAvatarDetail));
                OnPropertyChanged(nameof(ShowsNoBases));
                OnPropertyChanged(nameof(ShowsBaseDetail));
            }
        }
    }

    public string AvatarModeText => $"アバター（{_all.Count}）";

    public string BaseModeText => $"共通素体（{Bases.Count}）";

    /// <summary>右にアバターの詳細を出すか。素体を見ているときは出さない。</summary>
    public bool ShowsAvatarDetail => HasSelection && !IsBaseMode;

    /// <summary>素体を見ようとしたが1つも無いとき。空欄ではなく作り方を出す。</summary>
    public bool ShowsNoBases => IsBaseMode && !HasBases;

    public RelayCommand ShowAvatarModeCommand => _showAvatarModeCommand ??= new RelayCommand(() => IsBaseMode = false);

    public RelayCommand ShowBaseModeCommand => _showBaseModeCommand ??= new RelayCommand(() => IsBaseMode = true);

    // ---- 共通素体を手で足す（ユーザ判断 2026-09-28） ----
    //
    // 一覧には検出で見つかった素体しか並ばず、人が素体を足す口が無かった。
    // アバターを選ばずに、一覧の上の欄から素体だけを足せるようにする

    private RelayCommand? _addBaseCommand;
    private string _newBaseName = string.Empty;

    /// <summary>
    /// 足す欄。**候補を付けない、ふつうの入力欄**（メモ9-⑥ 2026-10-02。「入力欄には候補を付ける」の例外）。
    /// 新しい素体を名付ける欄なので、今ある名前を候補に並べても重複を誘うだけだった。今ある名前と同じなら足さずに「既にあります」と言う
    /// </summary>
    public string NewBaseName
    {
        get => _newBaseName;
        set
        {
            if (SetField(ref _newBaseName, value ?? string.Empty))
            {
                AddBaseNote.Clear();
            }
        }
    }

    /// <summary>共通素体を足す欄のすぐ下の知らせ（既にあります・入れてください）</summary>
    public AreaNotice AddBaseNote { get; } = new();

    /// <summary>名前の欄（右の詳細）のすぐ下の知らせ</summary>
    public AreaNotice AvatarNameNote { get; } = new();

    /// <summary>IDの行（IDのコピー・商品情報を取り直す）のすぐ下の知らせ</summary>
    public AreaNotice IdNote { get; } = new();

    /// <summary>共通素体の欄のすぐ下の知らせ</summary>
    public AreaNotice BaseFieldNote { get; } = new();

    /// <summary>呼び方の欄のすぐ下の知らせ</summary>
    public AreaNotice AliasNote { get; } = new();

    /// <summary>新しい改変の欄のすぐ下の知らせ</summary>
    public AreaNotice ModificationNote { get; } = new();

    /// <summary>メモの欄のすぐ下の知らせ。保存できたときは出さない（自動で残す欄は、できたことを知らせない）</summary>
    public AreaNotice MemoNote { get; } = new();

    /// <summary>所有の切り替えボタンの右の知らせ。書けなかったときだけ出る</summary>
    public AreaNotice OwnedNote { get; } = new();

    /// <summary>アバターかどうかの切り替えの右の知らせ。書けなかったときだけ出る</summary>
    public AreaNotice JudgementNote { get; } = new();

    /// <summary>別のアバターへ移ったら、前のアバターの欄の知らせは消す（別のアバターの欄に残ると、何の知らせか分からない）</summary>
    private void ClearAvatarNotes()
    {
        AvatarNameNote.Clear();
        IdNote.Clear();
        BaseFieldNote.Clear();
        AliasNote.Clear();
        ModificationNote.Clear();
        MemoNote.Clear();
        OwnedNote.Clear();
        JudgementNote.Clear();
    }

    /// <summary>上の段へ出す。書き込みの共通口（WriteAsync）に出し先として渡す。画面全体の状態と、押した所ごと消える操作の結果だけが使う</summary>
    private void ShowInHeader(string text) => Status = text;

    private AvatarBaseRowViewModel? BaseRow(string name) => Bases.FirstOrDefault(row => row.Name == name);

    /// <summary>
    /// 素体の詳細の欄の知らせを、その素体の行に出す。その素体を右に出していないとき（統合で行が無くなった・読み直しで選びが移った）は、
    /// 見えない所へ出すと知らせが消えたように見えるので上の段へ出す
    /// </summary>
    private void ShowOnBase(string baseName, Func<AvatarBaseRowViewModel, AreaNotice> slot, string text, bool warn)
    {
        if (BaseRow(baseName) is { } row && ReferenceEquals(row, SelectedBase))
        {
            if (warn)
            {
                slot(row).Warn(text);
            }
            else
            {
                slot(row).Show(text);
            }

            return;
        }

        Status = text;
    }

    /// <summary>一覧の上の欄から共通素体を足す。Enter でも「追加」でも、ここへ来る。</summary>
    public RelayCommand AddBaseCommand => _addBaseCommand ??= new RelayCommand(() => AddBaseAsync(NewBaseName).Forget());

    private async Task AddBaseAsync(string? input)
    {
        // 改行やタブは空白に寄せて1行にする（I13）
        var name = Core.Services.NameText.Normalize(input);

        // 空のまま押したときに黙って終わらない（I1）
        if (name.Length == 0)
        {
            AddBaseNote.Warn("共通素体の名前を入れてから押してください。");
            return;
        }

        if (Core.Services.NameText.IsTooLong(name))
        {
            AddBaseNote.Warn(Core.Services.NameText.TooLongMessage("共通素体の名前"));
            return;
        }

        // 一覧にある名前なら書かずに、その素体を選んで見せる（大文字小文字を変えて打っても同じ素体）
        if (Bases.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase)) is { } existing)
        {
            AddBaseNote.Warn($"共通素体「{existing.Name}」は既にあります。");
            SelectedBase = existing;
            return;
        }

        var result = await WriteAsync(new UiCommand.AddBase(name), "共通素体を追加できませんでした。", AddBaseNote.Warn);
        if (result is null)
        {
            return;
        }

        var outcome = result is CommandResult.BaseAdded added
            ? added.Outcome
            : AvatarBaseAddOutcome.AlreadyThere;
        NoteRegistryChanged();

        // 足せたら欄を空ける（続けて別の素体を足せるように）。既にあったときは打った名前を残す（直して足し直せる）。
        // 欄を空けると知らせも消えるので、知らせは空けた後に出す
        if (outcome != AvatarBaseAddOutcome.AlreadyThere)
        {
            NewBaseName = string.Empty;
        }

        // 足していないのに「追加しました」と言わない（I2）。もうある素体は、選んで見せる。
        // 足せたときは、欄が空き、素体が選ばれて右に出るので、知らせは出さない
        switch (outcome)
        {
            case AvatarBaseAddOutcome.Restored:
                AddBaseNote.Show($"削除していた共通素体「{name}」を戻しました。");
                break;
            case AvatarBaseAddOutcome.AlreadyThere:
                AddBaseNote.Warn($"共通素体「{name}」は既にあります。");
                break;
        }

        await LoadAsync();

        // 足した素体を右に出す。名前は一覧の表記に合わせる（大文字小文字を変えて打っても同じ素体）
        SelectedBase = Bases.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase))
            ?? SelectedBase;
    }

    /// <summary>素体を見ているときに選んでいる1つ。</summary>
    public AvatarBaseRowViewModel? SelectedBase
    {
        get => _selectedBase;
        set
        {
            if (SetField(ref _selectedBase, value))
            {
                SyncBaseMembers();
                OnPropertyChanged(nameof(MemberCandidates));
                OnPropertyChanged(nameof(HasSelectedBase));
                OnPropertyChanged(nameof(ShowsBaseDetail));
            }
        }
    }

    public bool HasSelectedBase => SelectedBase is not null;

    /// <summary>右に素体の管理を出すか。アバターを見ているときは出さない。</summary>
    public bool ShowsBaseDetail => IsBaseMode && HasSelectedBase;

    /// <summary>
    /// 選んだ素体を使っているアバター。持っているものを先に。
    /// 所属は素体の人数と同じ数え方（名前から推した仲間を含む）で引く。
    /// 手で決めた所属（BaseName）だけで引くと、「アバター 8」なのに一覧が空になった
    /// </summary>
    /// <remarks>
    /// **一覧は1つを持ち続け、足した行・外した行だけを足し引きする**（メモ68）。
    /// 前は変わるたびに新しい一覧を返していて、画面が全行を作り直し、乗せていた行のホバーが効かなくなった
    /// </remarks>
    public ObservableCollection<AvatarRowViewModel> SelectedBaseMembers { get; } = [];

    private string? _membersOf;

    private void SyncBaseMembers()
    {
        var wanted = SelectedBase is null
            ? []
            : _all
                .Where(row => SelectedBase.Summary.MemberIds.Contains(row.ItemId))
                .OrderByDescending(row => row.IsOwned)
                .ThenBy(row => row.Name, StringComparer.CurrentCulture)
                .ToList();

        // 別の素体へ移ったときは入れ替える（行が変わるのでホバーの話ではない）
        if (SelectedBase?.Name != _membersOf)
        {
            _membersOf = SelectedBase?.Name;
            SelectedBaseMembers.Clear();
            foreach (var row in wanted)
            {
                SelectedBaseMembers.Add(row);
            }

            return;
        }

        var wantedIds = wanted.Select(row => row.ItemId).ToHashSet();
        for (var i = SelectedBaseMembers.Count - 1; i >= 0; i--)
        {
            if (!wantedIds.Contains(SelectedBaseMembers[i].ItemId))
            {
                SelectedBaseMembers.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < SelectedBaseMembers.Count && SelectedBaseMembers[i].ItemId == wanted[i].ItemId)
            {
                // 読み直しで行は作り直されるが、見える物（名前・持っているか）が同じなら今の行を残す。
                // 入れ替えると、その行だけホバーが途切れる
                if (SelectedBaseMembers[i].Name != wanted[i].Name || SelectedBaseMembers[i].IsOwned != wanted[i].IsOwned)
                {
                    SelectedBaseMembers[i] = wanted[i];
                }

                continue;
            }

            var existing = SelectedBaseMembers.Select((row, index) => (row, index))
                .FirstOrDefault(pair => pair.row.ItemId == wanted[i].ItemId);
            if (existing.row is null)
            {
                SelectedBaseMembers.Insert(i, wanted[i]);
            }
            else
            {
                SelectedBaseMembers.Move(existing.index, i);
            }
        }
    }

    /// <summary>
    /// 素体の中のアバターを押すと、アバターの一覧へ切り替えてそれを選ぶ。
    /// 切り替える前の素体の詳細を履歴に積み、戻る（Alt+←）で素体の詳細へ戻れるようにする（メモ68）。
    /// </summary>
    public RelayCommand OpenMemberCommand => _openMemberCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is not AvatarRowViewModel shown)
            {
                return;
            }

            // 一覧の行は読み直しの前の物のことがあるので、今の行を ID で引く
            var row = _all.FirstOrDefault(other => other.ItemId == shown.ItemId) ?? shown;

            if (IsBaseMode)
            {
                _main.RememberAvatarsStep(this);
            }

            // 絞り込みで外れていると選べないので、そのときだけ絞り込みを外す
            if (!Rows.Contains(row))
            {
                Query = string.Empty;
            }

            IsBaseMode = false;
            Selected = row;
        },
        parameter => parameter is AvatarRowViewModel);

    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value))
            {
                Rebuild();
            }
        }
    }

    public string BaseInput
    {
        get => _baseInput;
        set
        {
            if (SetField(ref _baseInput, value))
            {
                // 打った名前が既にある素体かどうかで、ボタンの文字と置き文字が変わる（U19）
                OnPropertyChanged(nameof(NewBaseHint));
                OnPropertyChanged(nameof(HasNewBaseHint));
                OnPropertyChanged(nameof(HasBaseInput));
                BaseFieldNote.Clear();
            }
        }
    }

    public string AliasInput
    {
        get => _aliasInput;
        set
        {
            if (SetField(ref _aliasInput, value))
            {
                AliasNote.Clear();
            }
        }
    }

    /// <summary>表示名の編集欄。BOOTHの正式名は別に残す。</summary>
    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                OnPropertyChanged(nameof(HasNameChange));
                AvatarNameNote.Clear();
            }
        }
    }

    /// <summary>
    /// 名前の欄を今の名前から変えたか。「名前を保存」はそのときだけ出す（ユーザ指示 2026-09-17：メモは押さずに残るのに、
    /// 名前だけ保存のボタンが常に出ていて揃っていなかった）。名前は一覧の並びと見出しを変えるので、打っている途中では書かない
    /// </summary>
    public bool HasNameChange => Selected is not null && NameInput.Trim().Length > 0 && NameInput.Trim() != Selected.Name;

    private bool _isEditingName;

    /// <summary>
    /// 名前を欄にしているか。普段は名前を文字で出し、「名前を変更」を押したときだけ欄にする
    /// （ユーザ判断 2026-09-29：常に欄だと枠も地も無く、名前を変えられることが分からなかった）。
    /// 言い方と作法はタグの管理の「名前を変更」に揃える（Enter で確定・Esc で取り消し）
    /// </summary>
    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetField(ref _isEditingName, value);
    }

    private RelayCommand? _startRenameCommand;

    public RelayCommand StartRenameCommand => _startRenameCommand ??= new RelayCommand(() =>
    {
        if (Selected is null)
        {
            return;
        }

        NameInput = Selected.Name;
        IsEditingName = true;
    });

    private RelayCommand? _cancelRenameCommand;

    /// <summary>取り消したら欄を今の名前に戻し、打ちかけの控えも捨てる（次に開いたときに前の打ちかけが出ないように）</summary>
    public RelayCommand CancelRenameCommand => _cancelRenameCommand ??= new RelayCommand(() =>
    {
        IsEditingName = false;
        if (Selected is not null)
        {
            _drafts.Remove(Selected.ItemId);
            NameInput = Selected.Name;
        }
    });

    private string _memoInput = string.Empty;

    /// <summary>
    /// このアバターについての覚え書き。
    /// 「素体は同じだが肩幅が違う」のような、検出では拾えない事情を残す場所。
    /// </summary>
    public string MemoInput
    {
        get => _memoInput;
        set
        {
            // 押さなくても残す（ユーザ指示 2026-09-17）。打っている間は待ち、止まってから1回書く。
            // 選び直しで欄を入れ替えたときは書かない
            if (SetField(ref _memoInput, value ?? string.Empty) && !_swappingMemo && Selected is { } row)
            {
                _memoItemId = row.ItemId;
                _saveMemo.Request();
            }
        }
    }

    private readonly Debounced _saveMemo;

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => _saveMemo.RunNowAsync();

    /// <summary>書くのを待っているメモの持ち主。待ちの間に別のアバターへ移っても、元のアバターに書くため</summary>
    private string? _memoItemId;

    private bool _swappingMemo;

    /// <summary>
    /// このセッションで書いたメモ。保存のたびに一覧を読み直すと、打っている途中の欄が保存した時点の文に戻るので読み直さない。
    /// そのかわり、選び直したときに読み込み時の古いメモが出ないよう、こちらを優先する
    /// </summary>
    private readonly Dictionary<string, string> _writtenMemos = new(StringComparer.Ordinal);

    /// <summary>名前・呼び方・素体の打ちかけ（ユーザ判断 2026-09-20・I5）。</summary>
    /// <remarks>null の欄は打ちかけが無い（保存してある値を出す）。</remarks>
    private readonly record struct AvatarDraft(string? Name, string? Alias, string? Base);

    /// <summary>
    /// **打ちかけは行を選び直しても残す**（I5）。前は黙って消えていて、
    /// 同じ画面のメモだけが書き切られるという食い違いがあった。
    /// 保存したらその行の控えは捨てる（保存済みの値より古い打ちかけが勝たないように）。
    /// </summary>
    private readonly Dictionary<string, AvatarDraft> _drafts = new(StringComparer.Ordinal);

    public AvatarRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!ReferenceEquals(value, _selected))
            {
                FlushMemo();

                // 読み直しで同じアバターの新しい行へ選び直すときは残す（書いた直後の知らせが消える）
                if (_selected?.ItemId != value?.ItemId)
                {
                    ClearAvatarNotes();
                }

                // 離れる前に打ちかけを控える（I5）。**保存してある値と同じ欄は打ちかけではない**ので控えない。
                // 書き込みの後の読み直しでも、同じアバターの古い行から新しい行へ選び直す。そこで欄を丸ごと控えると、
                // 「BOOTHの名前に戻す」の後に戻す前の名前が打ちかけとして残り、名前の欄が開いたままになった（メモ9-③ 2026-10-02）
                if (_selected is { } leaving)
                {
                    var draft = new AvatarDraft(
                        NameInput == leaving.Name ? null : NameInput,
                        AliasInput.Length == 0 ? null : AliasInput,
                        BaseInput == (leaving.Summary.Entry.BaseName ?? string.Empty) ? null : BaseInput);
                    if (draft == default)
                    {
                        _drafts.Remove(leaving.ItemId);
                    }
                    else
                    {
                        _drafts[leaving.ItemId] = draft;
                    }
                }
            }

            if (SetField(ref _selected, value))
            {
                var draft = value is not null && _drafts.TryGetValue(value.ItemId, out var kept)
                    ? (AvatarDraft?)kept
                    : null;
                BaseInput = draft?.Base ?? value?.Summary.Entry.BaseName ?? string.Empty;
                AliasInput = draft?.Alias ?? string.Empty;
                NameInput = draft?.Name ?? value?.Name ?? string.Empty;
                // 打ちかけの名前があれば欄のまま戻す（I5）。無ければ文字で出す
                IsEditingName = value is not null && NameInput != value.Name;
                _swappingMemo = true;
                MemoInput = value is null ? string.Empty
                    : _writtenMemos.TryGetValue(value.ItemId, out var written) ? written : value.Summary.Entry.Memo ?? string.Empty;
                _swappingMemo = false;

                foreach (var name in new[]
                {
                    nameof(HasSelection), nameof(SelectedName), nameof(SelectedIdText),
                    nameof(HasManualName), nameof(ResetNameTip),
                    nameof(SelectedCategoryText), nameof(SelectedCountText), nameof(Aliases),
                    nameof(OwnedButtonText), nameof(SelectedOwnedText), nameof(SelectedSeenAsText), nameof(CanOpenItem),
                    nameof(SelectedCheckedText), nameof(SelectedBaseNote), nameof(HasSelectedBaseNote),
                    nameof(SelectedInferredBaseText), nameof(HasSelectedInferredBase),
                    nameof(SelectedBoothName), nameof(HasSelectedBoothName),
                    nameof(NeedsName), nameof(NameSuggestions), nameof(HasNameSuggestions),
                    nameof(ReferencedByText), nameof(HasReferencedBy),
                    nameof(HasModifications),
                    nameof(IsOverrideAuto), nameof(IsForcedAvatar), nameof(IsForcedNotAvatar),
                    nameof(HasBaseInput), nameof(ShowsAvatarDetail),
                    nameof(IsOwnedByFile), nameof(ShowsOwnedToggle), nameof(RejectedAliases), nameof(HasRejectedAliases),
                    nameof(AliasesTitle), nameof(JudgementText), nameof(NewBaseHint), nameof(HasNewBaseHint), nameof(HasNameChange),
                })
                {
                    OnPropertyChanged(name);
                }

                // 「BOOTHの名前に戻す」の押せるかは WPF が入力のたびにしか問い直さない。
                // 名前を変えた後の読み直しは入力を伴わないので、押せないまま残っていた
                RelayCommand.RaiseCanExecuteChanged();

                // 選んだアバターの改変を読み直す。待たせないので投げっぱなしにする
                ModificationNameInput = string.Empty;
                LoadModificationsAsync().Forget();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// 名前がまだ無いか、商品IDのままの項目か。
    /// BOOTHが404を返す項目は名前を引けないので、手元の材料から候補を出す。
    /// </summary>
    public bool NeedsName => Selected is not null && Selected.Name == Selected.ItemId;

    /// <summary>
    /// 名前の候補。**別名から取る。**
    ///
    /// 別名（<c>nameHints</c>）は <c>IsAvatar</c> の判定を通さずに溜まるので、
    /// 404の項目にも「くうた」「くうた対応」のような呼び名が残っている。
    ///
    /// **絞った1つを先頭に出し、残りも並べる。**絞る根拠が弱いから——
    /// 「対応アバター」節由来は1件ずつしか溜まらないことが多く、回数が並ぶと選べない。
    /// 1つに絞って外していたらユーザは打ち直すことになる。
    ///
    /// <c>ShortenName</c> による切り出しは**別名が1件も無いときの最後の手段**。
    /// 参照商品の名前をそのまま入れるのは明確に誤り——それは*衣装*の名前で、
    /// アバターの名前ではない。
    /// </summary>
    public IReadOnlyList<string> NameSuggestions
    {
        get
        {
            if (Selected is null)
            {
                return [];
            }

            var entry = Selected.Summary.Entry;

            var fromAliases = entry.Aliases
                .Where(alias => !alias.Rejected && alias.Text.Trim().Length >= 2)
                .OrderByDescending(alias => alias.Count)
                .ThenBy(alias => alias.Text.Length)
                .Select(alias => alias.Text.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxNameSuggestions)
                .ToList();

            if (fromAliases.Count > 0)
            {
                return fromAliases;
            }

            var shortened = Core.Services.AvatarText.ShortenName(entry.BoothName);
            return string.IsNullOrWhiteSpace(shortened) || shortened == entry.ItemId
                ? []
                : [shortened];
        }
    }

    public bool HasNameSuggestions => NameSuggestions.Count > 0;

    /// <summary>
    /// このアバターを対応先として挙げている所持商品。
    /// **IDと件数だけでは数字で判断させることになる**ので、商品名を並べる。
    /// </summary>
    public string ReferencedByText
    {
        get
        {
            var names = Selected?.Summary.ReferencedBy ?? [];
            if (names.Count == 0)
            {
                return string.Empty;
            }

            var total = Selected!.Summary.DirectCount;
            var rest = total - 1;

            return rest > 0
                ? $"「{names[0]}」ほか {rest} 件が対応先として挙げています"
                : $"「{names[0]}」が対応先として挙げています";
        }
    }

    public bool HasReferencedBy => ReferencedByText.Length > 0;

    public RelayCommand UseNameSuggestionCommand { get; }

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedIdText => Selected is null ? string.Empty : $"ID {Selected.ItemId}";

    /// <summary>ID の吹き出し。商品ページの ID と同じ文（同じ物を押すと同じことが起きる）。</summary>
    public string IdCopyTip => "クリックすると商品IDをコピーします";

    private RelayCommand? _copyIdCommand;

    /// <summary>
    /// 選んだアバターの商品IDを写す（メモ9-② 2026-10-02：商品ページと同じく、ID を押すとコピーできるべき）。
    /// **IDそのものだけを写す**——「ID 12345」ごと写しても貼れない。言い方は商品ページの <c>CopyId</c> と同じ
    /// </summary>
    public RelayCommand CopyIdCommand => _copyIdCommand ??= new RelayCommand(() =>
    {
        if (Selected is not { } row)
        {
            return;
        }

        // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
        if (_services.CopyText(row.ItemId))
        {
            IdNote.Show($"{row.ItemId} をコピーしました。");
        }
        else
        {
            IdNote.Warn("コピーできませんでした。もう一度押してください。");
        }
    });

    /// <summary>BOOTHの正式名。表示名を短くしている分、元の名前も読めるようにする。</summary>
    public string SelectedBoothName => Selected?.BoothName ?? string.Empty;

    public bool HasSelectedBoothName => SelectedBoothName.Length > 0 && SelectedBoothName != SelectedName;

    /// <summary>BOOTHのcategoryはそのまま出す。判定の根拠が読めるようにするため。</summary>
    public string SelectedCategoryText => Selected?.Summary.Entry.Category ?? "（販売終了などで確認できていません）";

    public string SelectedCountText => Selected is null
        ? string.Empty
        : $"対応している商品 {Selected.Summary.DirectCount} 件・共通素体経由 {Selected.Summary.ViaBaseCount} 件";

    /// <summary>所有の表示。取り込んだファイルで所有しているときは固定（ユーザ判断 2026-09-17）、していないときだけ手動で切り替えられる。</summary>
    public string SelectedOwnedText => Selected is null
        ? string.Empty
        : IsOwnedByFile ? "所有している（ファイルあり）"
        : Selected.Summary.Entry.IsOwnedManually ? "所有している（手動で指定）"
        // 検索が商品を読み終えるまでは、ファイルで持っているかが分からない。分からないうちに言い切らない
        : ItemsPending ? "所有しているか確かめています…"
        : "所有していない";

    public string OwnedButtonText => Selected?.Summary.Entry.IsOwnedManually == true
        ? "所有の指定を外す"
        : "所有しているものとして扱う";

    /// <summary>どの文脈で候補に挙がったか。判定の根拠なので隠さない。</summary>
    public string SelectedSeenAsText
    {
        get
        {
            if (Selected is null)
            {
                return string.Empty;
            }

            // 無いときも「なし」と言う（ユーザ指摘 2026-10-07。空だと1行ぶんの空白が間延びして見えた。手で登録したアバターは挙がった所が無い）
            if (Selected.Summary.Entry.SeenAs.Count == 0)
            {
                return "見つかった場所：なし";
            }

            var parts = Selected.Summary.Entry.SeenAs
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{Label(pair.Key)} {pair.Value} 件");

            return "見つかった場所：" + string.Join(" / ", parts);
        }
    }

    public string SelectedCheckedText => Selected?.Summary.Entry.CheckedAt is { } at
        ? $"BOOTHで確認した日 {at:yyyy-MM-dd}"
        : string.Empty;

    // ── アバターかどうか（判定の上書き）の3択（U17）──
    // 以前はボタンを押すだけで、今どれが効いているかを出していなかった

    public bool IsOverrideAuto => Selected?.Summary.Entry.AvatarOverride is null;

    public bool IsForcedAvatar => Selected?.Summary.Entry.AvatarOverride == true;

    public bool IsForcedNotAvatar => Selected?.Summary.Entry.AvatarOverride == false;

    // ── 共通素体の欄（U19）──

    public bool HasBaseInput => BaseInput.Trim().Length > 0;

    /// <summary>素体を指定したときに何が起きるかをその場に書く。推定が広がる操作なので。</summary>
    public string SelectedBaseNote
    {
        get
        {
            if (Selected?.Summary.Entry.BaseName is not { } name)
            {
                return string.Empty;
            }

            var group = Bases.FirstOrDefault(row =>
                string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase));

            if (group is null)
            {
                return string.Empty;
            }

            if (!group.InferClothing)
            {
                return $"「{name}」は衣装の互換を広げない設定です。他のアバター向けの衣装は、素体経由として出ません。";
            }

            // 数ではなく名前で並べる（U19）。「他の 3 体」ではどのアバターか分からず、入れてよいかを判断できない
            var siblings = _all
                .Where(row => row.ItemId != Selected.ItemId
                    && string.Equals(row.Summary.Entry.BaseName, name, StringComparison.CurrentCultureIgnoreCase))
                .Select(row => row.Name)
                .ToList();

            if (siblings.Count == 0)
            {
                return $"「{name}」に属しているのはこのアバターだけです。";
            }

            // 多いと1行が長くなりすぎるので8体まで。残りは数で添える
            const int shown = 8;
            var names = string.Join("・", siblings.Take(shown));
            var rest = siblings.Count > shown ? $" ほか {siblings.Count - shown} 体" : string.Empty;
            return $"「{name}」の他のアバター（{names}{rest}）向けの衣装も、素体経由として一緒に表示されます。";
        }
    }

    public bool HasSelectedBaseNote => SelectedBaseNote.Length > 0;

    /// <summary>
    /// 照合に使っている別名。**消したものは出さない。**
    /// 行はデータに残っているが（消したという事実を次の検出まで持ち越すため）、
    /// 一覧に出すと「消したのに残っている」と読まれる。
    /// </summary>
    public IReadOnlyList<string> Aliases => Selected?.Summary.Entry.Aliases
        .Where(alias => !alias.Rejected)
        .OrderByDescending(alias => alias.Count)
        .Select(alias => alias.Count > 0 ? $"{alias.Text}（{alias.Count}）" : alias.Text)
        .ToList() ?? [];

    private static string Label(string source) => source switch
    {
        "SupportSection" => "説明文の「対応アバター」の見出し",
        "Tag" => "タグ",
        "Variation" => "バリエーションの名前",
        "H2Link" => "説明文のリンク",
        "SupportList" => "説明文の対応一覧",
        "Manual" => "手入力",
        _ => source,
    };

    private async Task LoadAsync()
    {
        // 読めなかったときも「読み込み中」を下ろす（成功した道でしか下ろしていなかった）
        try
        {
            await LoadCoreAsync(_leaving.Token);
        }
        catch (OperationCanceledException) when (_leaving.IsCancellationRequested)
        {
            // 画面を離れた。投げ直さないのは、書き込みの後に読み直していた呼び手を失敗に見せないため
            // （呼び手の続きは出ない画面の状況の文を書くだけ）
        }
        finally
        {
            RunOnUiThread(() => IsLoading = false);
        }
    }

    private async Task LoadCoreAsync(CancellationToken token)
    {
        var avatars = await Task.Run(() => _services.Avatars.LoadAsync(token), token);
        var bases = await Task.Run(() => _services.Avatars.LoadBasesAsync(token), token);

        RunOnUiThread(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            _all = avatars.Select(summary => new AvatarRowViewModel
            {
                Summary = summary,
                Thumbnails = _main.Thumbnails,
                // 検索がまだ読み終えていなければ引けない。読み終えたら引き直す（OnSearchChanged）
                Item = _main.Search.FindItem(summary.Entry.ItemId),
                // 一覧は1つだけ選ぶ物（右に詳細を出す）。カードの選ぶ箱を出すと何枚でも印が付き、選んでも何もできなかった（メモ9-⑤）
                CardFactory = () => _main.Search.CardFor(summary.Entry.ItemId) is { } card ? WithoutSelection(card) : null,
                Services = _services,
                IconPathFactory = () => AvatarImageSync.IconPath(
                    _services.Paths, summary.Entry.ItemId, _main.Search.FindItem(summary.Entry.ItemId)),
            }).ToList();

            var pending = ItemsPending;
            foreach (var row in _all)
            {
                row.RefreshItem(row.Item, pending);
            }

            // 素体の設定を変えると読み直すので、選んでいた素体を名前で戻す
            var selectedBaseName = _openBaseWith ?? SelectedBase?.Name;
            var openBase = _openBaseWith is not null;
            _openBaseWith = null;
            Bases.Clear();
            BaseNames.Clear();

            foreach (var summary in bases)
            {
                var name = summary.Group.Name;
                // 素体そのものの商品の候補（ユーザ指摘 2026-09-17：素体単体の配布を検知できていなかった）。押したら結ぶ
                var candidates = Core.Services.AvatarBaseItemFinder.Candidates(summary.Group, avatars.Select(avatar => avatar.Entry))
                    .Take(3)
                    .Select(entry => new BaseItemCandidate(
                        entry.ItemId,
                        $"{entry.BoothName}（{entry.ItemId}）",
                        new RelayCommand(() => SetBaseItemIdAsync(name, entry.ItemId).Forget())))
                    .ToList();
                var baseRow = new AvatarBaseRowViewModel
                {
                    Summary = summary,
                    ItemCandidates = candidates,
                    ItemIdInput = summary.Group.ItemId ?? string.Empty,
                    NameInput = name,
                    ToggleInferCommand = new RelayCommand(() => ToggleInferAsync(name, !summary.Group.InferClothing).Forget()),
                    DeleteCommand = new RelayCommand(() => ConfirmDeleteBaseAsync(name).Forget()),
                };
                baseRow.SetItemIdCommand = new RelayCommand(() => SetBaseItemIdAsync(name, baseRow.ItemIdInput).Forget());
                baseRow.RenameCommand = new RelayCommand(() => RenameBase(name, baseRow.NameInput));
                Bases.Add(baseRow);
                BaseNames.Add(name);
            }

            // 同じ行を入れ直しても、所属の数が変わっていることがあるので必ず知らせ直す
            _selectedBase = null;
            SelectedBase = Bases.FirstOrDefault(row => row.Name == selectedBaseName) ?? Bases.FirstOrDefault();

            IsLoading = false;
            Rebuild();

            // 戻るで素体の詳細へ戻すとき。素体が消えていたら（戻る前に消した）アバターの見方のまま
            if (openBase && SelectedBase?.Name == selectedBaseName)
            {
                IsBaseMode = true;
            }
        });
    }

    private static ItemCardViewModel WithoutSelection(ItemCardViewModel card)
    {
        card.CanSelect = false;
        return card;
    }

    private const string OwnedGroup = "所有しているアバター";
    private const string ExcludedGroup = "アバターとして扱わないもの";
    private const string SeenGroup = "対応商品で名前が挙がったアバター";

    /// <summary>
    /// 畳んだ見出し。この画面は開くたびに作り直されるので、アプリを閉じるまでここに持つ。
    /// </summary>
    private static readonly HashSet<string> CollapsedGroups = new(StringComparer.Ordinal);

    public static bool IsGroupCollapsed(string group) => CollapsedGroups.Contains(group);

    public static void SetGroupCollapsed(string group, bool collapsed)
    {
        if (collapsed)
        {
            CollapsedGroups.Add(group);
        }
        else
        {
            CollapsedGroups.Remove(group);
        }
    }

    private void Rebuild()
    {
        var selectedId = Selected?.ItemId;

        // 並びは「所有していてアバター扱い → アバターとして扱わない → 未所有」（ユーザ判断 2026-09-12）。
        // 扱わないものは所有していても真ん中に固める。見出しの中の並びは読み込んだ順のまま
        // （OrderBy は同じ順位の中の並びを変えない）
        foreach (var row in _all)
        {
            row.UpdateMatchNote(_query);
        }

        var matched = _all
            .Where(row => _query.Length == 0 || row.Matches(_query))
            .OrderBy(row => row.IsExcluded ? 1 : row.IsOwned ? 0 : 2)
            .ToList();

        // 未所有も必ず出す。手持ちの衣装の対応先が未所有アバターなのは普通で、
        // 隠すと一覧がほぼ空になる（実データでも主力の対応先が未所有だった）。
        // 見出しの名前に件数を入れない。件数が変わると別の見出しになり、畳んだ状態を覚えられない
        // （件数は見出しの側で数える）
        foreach (var row in matched)
        {
            row.GroupName = row.IsExcluded ? ExcludedGroup : row.IsOwned ? OwnedGroup : SeenGroup;
        }

        // 1回で差し替える。1件ずつ足すと、見出しでまとめた一覧は1件ごとに振り分け直すので、
        // 絞り込みの1文字ごとに約400回の知らせと振り分けが走っていた（知らせは Clear の分と合わせ401回 → 1回）
        Rows.ReplaceAll(matched);

        OnPropertyChanged(nameof(HasBases));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(CanDetectFromItems));
        OnPropertyChanged(nameof(AvatarModeText));
        OnPropertyChanged(nameof(BaseModeText));
        OnPropertyChanged(nameof(ShowsNoBases));

        // 商品ページの札から来たときは、そのアバターを選んだ状態で開く（U13）。指名は最初の1回だけ
        var wanted = _openWith ?? selectedId;
        _openWith = null;
        Selected = matched.FirstOrDefault(row => row.ItemId == wanted) ?? matched.FirstOrDefault();
    }

    private async Task DetectAsync()
    {
        // **どの画面からでも止められるようにする**（ユーザ判断 2026-09-21・C1）。
        // 止める手立てが一切無く、友人データの初回で約37分ぶら下がっていた
        using var stop = new CancellationTokenSource();
        var job = _main.BeginLongJob("対応アバターを検出しています", "この間、アバターの編集と取り込みの検出は待たされます", stop,
            "検出をやめます。分かった分は書き込んであります。");
        if (job is null)
        {
            return;
        }

        IsDetecting = true;
        Status = "手元の説明文とタグを読んでいます…";

        try
        {
            var progress = new Progress<AvatarDetectProgress>(report =>
            {
                Status = $"{report.Phase}　{report.Done} / {report.Total}";
                _main.ReportLongJob($"対応アバターを検出中　{report.Phase}　{report.Done} / {report.Total}", report.Done, report.Total);
            });

            // **UiCommand を通す。**直接呼ぶと CommandHandler の優先度の包みの外に
            // 出てしまい、既定の Metadata（取り込みの①②と同じ順位）で順番待ちする。
            // 押した人は画面の前で結果を待っているので User に乗せたい。
            // 取り込みの中の③は内側で Detection を指定しているので、そのまま待てる側に残る
            var outcome = await _services.Commands.ExecuteAsync(
                new UiCommand.DetectAvatars(progress), cancellationToken: stop.Token);
            if (outcome is CommandResult.Failed detectFailed)
            {
                Status = detectFailed.Message;
                return;
            }

            if (outcome is not CommandResult.AvatarsDetected detected)
            {
                return;
            }

            var result = detected.Result;

            var parts = new List<string>
            {
                // 数字は詰めて書く。更新が0件のときに「0件を更新しました」だと、何か起きたのか分かりにくい（ユーザ判断 2026-09-17）
                result.ItemsUpdated > 0
                    ? $"{result.ItemsScanned} 件を調べ、{result.ItemsUpdated} 件の対応アバターを更新しました"
                    : $"{result.ItemsScanned} 件を調べました。対応アバターに変わりはありませんでした",
                $"アバター{result.AvatarsFound}体",
            };

            if (result.BaseGroupsFound > 0)
            {
                parts.Add($"共通素体{result.BaseGroupsFound}グループ");
            }

            if (result.Requests > 0)
            {
                parts.Add($"BOOTHへの問い合わせ{result.Requests}回");
            }

            // 打ち切ったときは原因と次の一手を言う。「通信できなかった n 件」では、つながっていないのか BOOTH が悪いのか分からない
            if (Core.Services.FailureText.Outage(result.Outage) is { } outage)
            {
                parts.Add(result.Outage == Core.Booth.BoothOutageKind.Offline
                    ? $"{outage}つながってからもう一度押すと、残りを確かめます"
                    : $"{outage}少し待ってからもう一度押すと、残りを確かめます");
            }
            else if (result.Unresolved > 0)
            {
                parts.Add($"通信できなかった {result.Unresolved} 件は次回もう一度試します");
            }

            Status = string.Join(" / ", parts);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // 中止。そこまでに書き込んだ分はそのまま残る（1件ずつ書いている）
            Status = "検出を中止しました。そこまでに分かった分は書き込んであります。もう一度押すと続きから試します。";
            await LoadAsync();
        }
        catch (Exception exception)
        {
            // 検出は途中まで進んでいることがあり、もう一度押せば続きから走る
            Core.Diagnostics.AppLog.Error("アバターの画面：対応アバターの検出", exception);
            Status = $"検出の途中で止まりました。{Core.Services.FailureText.Cause(exception)}　もう一度押すと続きから試します。";
        }
        finally
        {
            job.Dispose();
            IsDetecting = false;
        }
    }

    private async Task SetBaseAsync()
    {
        if (Selected is null)
        {
            return;
        }

        // 空のまま押したときに黙って終わらない（I1）
        if (string.IsNullOrWhiteSpace(BaseInput))
        {
            BaseFieldNote.Warn("共通素体の名前を入れてから押してください。");
            return;
        }

        if (Core.Services.NameText.IsTooLong(BaseInput))
        {
            BaseFieldNote.Warn(Core.Services.NameText.TooLongMessage("共通素体の名前"));
            return;
        }

        var baseFor = Selected.ItemId;
        var baseName = BaseInput;
        if (await WriteAsync(new UiCommand.SetAvatarBase(baseFor, baseName), "素体を保存できませんでした。", BaseFieldNote.Warn) is null)
        {
            return;
        }

        AfterWritten(baseFor, draft => draft with { Base = draft.Base == baseName ? null : draft.Base }, () => { });
        NoteRegistryChanged();
        await LoadAsync();
    }

    private async Task ClearBaseAsync()
    {
        // 手で決めた素体も、名前から推した素体も外す（推した素体は「素体に入れない」の印を立てる。メモ46 1-B）。
        // 前は素体名を空にするだけで、推した素体は空にしても次に推されて外せなかった
        if (Selected is null || CurrentBaseOfSelected is not { } current)
        {
            return;
        }

        if (await WriteAsync(new UiCommand.RemoveAvatarFromBase(Selected.ItemId, current), "素体を外せませんでした。", BaseFieldNote.Warn) is null)
        {
            return;
        }

        BaseInput = string.Empty;
        NoteRegistryChanged();
        await LoadAsync();
    }

    /// <summary>
    /// 素体に配布商品を結ぶ。空にすると外れる。
    /// 数字でもBOOTHの商品URLでも受ける（ブラウザから来るのは普通URLの方）。
    /// </summary>
    private async Task SetBaseItemIdAsync(string name, string input)
    {
        var trimmed = input.Trim();

        // 読み直すと行が作り直されるので、書けなかった知らせは今の行に、書けた知らせは読み直した後の行に出す
        Action<string> warn = text => ShowOnBase(name, row => row.ItemIdNote, text, warn: true);

        if (trimmed.Length == 0)
        {
            if (await WriteAsync(new UiCommand.SetBaseItemId(name, null), "紐付けを外せませんでした。", warn) is null)
            {
                return;
            }

            await LoadAsync();
            ShowOnBase(name, row => row.ItemIdNote, "配布商品との紐付けを外しました。", warn: false);
            return;
        }

        var itemId = Core.Services.BoothItemId.Parse(trimmed);
        if (itemId is null)
        {
            warn("商品IDが読み取れませんでした。数字か、BOOTHの商品ページのURLを入れてください。");
            return;
        }

        if (await WriteAsync(new UiCommand.SetBaseItemId(name, itemId), "紐付けできませんでした。", warn) is null)
        {
            return;
        }

        await LoadAsync();
        ShowOnBase(name, row => row.ItemIdNote, $"商品 {itemId} に紐付けました。", warn: false);
    }

    private async Task ToggleInferAsync(string name, bool infer)
    {
        if (await WriteAsync(new UiCommand.SetBaseInferClothing(name, infer), "切り替えを保存できませんでした。",
                text => ShowOnBase(name, row => row.ItemIdNote, text, warn: true)) is null)
        {
            // 切り替えの見た目は押した時点で動くので、書いた値に戻す
            await LoadAsync();
            return;
        }

        // 衣装の互換を広げるかは素体の索引が見る。検索の絞り込みに今の値を効かせる
        NoteRegistryChanged();
        await LoadAsync();
        ShowOnBase(name, row => row.ItemIdNote, infer
            ? "この素体の一致から衣装の互換を広げます。"
            : "この素体の一致では衣装の互換を広げません。", warn: false);
    }

    /// <summary>
    /// 共通素体の名前を、欄に打った名前へ変える（B10）。
    /// 欄が空・変わっていないときは何もしない（アバターの名前と同じ作法）。
    /// </summary>
    private void RenameBase(string oldName, string input)
    {
        var newName = Core.Services.NameText.Normalize(input);

        // 空のまま押したときに黙って終わらない（I1）
        if (newName.Length == 0)
        {
            ShowOnBase(oldName, row => row.NameNote, "新しい素体の名前を入れてから押してください。", warn: true);
            return;
        }

        if (Core.Services.NameText.IsTooLong(newName))
        {
            ShowOnBase(oldName, row => row.NameNote, Core.Services.NameText.TooLongMessage("共通素体の名前"), warn: true);
            return;
        }

        if (newName == oldName)
        {
            // 変えずに Enter を押したら、何も書かずに文字へ戻す
            Bases.FirstOrDefault(row => row.Name == oldName)?.CancelRenameCommand.Execute(null);
            return;
        }

        RenameBaseAsync(oldName, newName).Forget();
    }

    private async Task RenameBaseAsync(string oldName, string newName)
    {
        // **ほかの素体の名前を指したら統合になるので聞く**（ユーザ判断 2026-09-29）。前は確認なしで統合され、
        // 統合は所属と宣言がどちらの素体の物だったかを残さないので分け直せない。
        // 名前の変更だけなら同じ手順で戻せるので聞かない（タグの管理の統合と同じ作法・D5）
        var mergeInto = Core.Services.AvatarBaseRename.MergeTarget(Bases.Select(row => row.Name), oldName, newName);
        if (mergeInto is not null && !await ConfirmMergeBaseAsync(oldName, mergeInto))
        {
            return;
        }

        var result = await WriteAsync(
            new UiCommand.RenameBase(oldName, newName),
            mergeInto is null ? "素体の名前を変更できませんでした。" : "素体を統合できませんでした。",
            text => ShowOnBase(oldName, row => row.NameNote, text, warn: true));
        var resultText = string.Empty;
        if (result is not null)
        {
            var updated = result is CommandResult.Counted renamed ? renamed.Count : 0;
            resultText = mergeInto is null
                ? $"「{oldName}」を「{newName}」に変え、商品 {updated} 件を書き換えました。"
                : $"「{oldName}」を「{mergeInto}」に統合し、商品 {updated} 件を書き換えました。";
        }

        // 書けなかったときも読み直す。途中まで書き換えた商品があり得るので、今の状態を見せる
        await LoadAsync();
        await NoteItemsRewrittenAsync();

        if (resultText.Length > 0)
        {
            // 読み直すと選びが先頭の素体へ移る。結果を名前の欄の下に出すので、名前を変えた（統合先の）素体を選び直す
            if (BaseRow(mergeInto ?? newName) is { } target)
            {
                SelectedBase = target;
            }

            ShowOnBase(mergeInto ?? newName, row => row.NameNote, resultText, warn: false);
        }
    }

    /// <summary>
    /// 統合してよいかを聞く。所属しているアバターと宣言している商品の数を出す（消すときの確認と同じく、押す前に規模を見せる）。
    /// </summary>
    private async Task<bool> ConfirmMergeBaseAsync(string oldName, string into)
    {
        var members = Bases.FirstOrDefault(row => row.Name == oldName)?.Summary.MemberCount ?? 0;
        var items = await Task.Run(() => _services.Avatars.CountItemsUsingBaseAsync(oldName));

        var answer = Services.Notice.Show(
            $"共通素体「{oldName}」を「{into}」に統合します。\n\n"
            + $"アバター {members} 体の所属と、商品 {items} 件の素体の宣言を「{into}」に書き換えます。\n\n"
            + "この操作は元に戻せません。",
            "共通素体を統合する",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        return answer == System.Windows.MessageBoxResult.OK;
    }

    /// <summary>
    /// 素体グループを消す。アバターまわりで唯一、取り消せない操作なので、
    /// 押す前に何件書き換わるかを数えて出す。
    /// </summary>
    private async Task ConfirmDeleteBaseAsync(string name)
    {
        var members = Bases.FirstOrDefault(row => row.Name == name)?.Summary.MemberCount ?? 0;
        var items = await Task.Run(() => _services.Avatars.CountItemsUsingBaseAsync(name));

        var answer = Services.Notice.Show(
            $"共通素体「{name}」を削除します。\n\n"
            + $"アバター {members} 体が所属無しに戻り、商品 {items} 件から素体の宣言が消えます。\n"
            + "素体経由で出ていた対応も出なくなります。\n\n"
            + "この操作は元に戻せません。同じ名前で作り直しても、所属と宣言は戻りません。",
            "共通素体を削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            await DeleteBaseAsync(name);
        }
    }

    private async Task DeleteBaseAsync(string name)
    {
        // 素体の削除は、管理の欄ごと別の素体に切り替わる。消えた欄の下には出せないので、上の段に残す
        if (await WriteAsync(new UiCommand.DeleteBase(name), "素体を削除できませんでした。", ShowInHeader) is { } result)
        {
            var updated = result is CommandResult.Counted deleted ? deleted.Count : 0;
            Status = $"「{name}」を削除し、商品 {updated} 件を書き換えました。";
        }

        // 書けなかったときも読み直す（改名と同じ）
        await LoadAsync();
        await NoteItemsRewrittenAsync();
    }

    /// <summary>このアバターに人が名前を付けているか。「BOOTHの名前に戻す」はそのときだけ出す。</summary>
    public bool HasManualName => Selected is not null && AvatarNames.ManualName(Selected.Summary.Entry) is not null;

    /// <summary>戻したらどの名前になるかを、押す前に見せる。</summary>
    public string ResetNameTip => Selected is null
        ? string.Empty
        : $"「{AvatarNames.ShownName(Selected.Summary.Entry with { DisplayName = null })}」に戻します。あとから付け直せます。";

    private RelayCommand? _resetNameCommand;

    /// <summary>
    /// 付けた名前を消して自動の名前に戻す（ユーザ指示）。
    /// 名前の欄を空にして保存する、という戻し方は直感的でない（しかも「名前を保存」は空の入力を受け付けない）ので、
    /// 戻す操作を名乗るボタンにする。
    /// </summary>
    public RelayCommand ResetNameCommand => _resetNameCommand ??= new RelayCommand(
        () => ResetNameAsync().Forget(),
        () => HasManualName);

    private async Task ResetNameAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var resetFor = Selected.ItemId;
        if (await WriteAsync(new UiCommand.SetAvatarName(resetFor, string.Empty), "名前を戻せませんでした。", AvatarNameNote.Warn) is null)
        {
            return;
        }

        AfterWritten(resetFor, draft => draft with { Name = null }, () => IsEditingName = false);
        NoteRegistryChanged();
        await LoadAsync();
    }

    private async Task RenameAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var newName = Core.Services.NameText.Normalize(NameInput);
        if (newName.Length == 0)
        {
            AvatarNameNote.Warn("名前を入れてから押してください。");
            return;
        }

        if (Core.Services.NameText.IsTooLong(newName))
        {
            AvatarNameNote.Warn(Core.Services.NameText.TooLongMessage("アバターの名前"));
            return;
        }

        // 変えずに Enter を押したら、何も書かずに文字へ戻す（窓の「名前を変える」が押せないのと同じ扱い）
        if (newName == Selected.Name)
        {
            CancelRenameCommand.Execute(null);
            return;
        }

        // 書けなかったときは打った名前の欄を開いたまま残す（もう一度押せば書ける）
        var renameFor = Selected.ItemId;
        if (await WriteAsync(new UiCommand.SetAvatarName(renameFor, newName), "名前を保存できませんでした。", AvatarNameNote.Warn) is null)
        {
            return;
        }

        AfterWritten(renameFor, draft => draft with { Name = draft.Name == newName ? null : draft.Name }, () => IsEditingName = false);
        NoteRegistryChanged();
        await LoadAsync();
    }

    /// <summary>
    /// 書けた後の片付けを、**書いたアバターにだけ**当てる（外部の点検 2026-10-06）。
    /// 書き込みを待つ間に別のアバターを選べるので、待った後に Selected を見ると、選び直した先の欄と打ちかけを消していた。
    /// まだ同じアバターなら欄を片付けて控えを捨てる。離れていたら、控えのうち書いた値と同じ所だけを捨てる
    /// </summary>
    private void AfterWritten(string itemId, Func<AvatarDraft, AvatarDraft> forget, Action onSame)
    {
        if (Selected?.ItemId == itemId)
        {
            _drafts.Remove(itemId);
            onSame();
            return;
        }

        if (_drafts.TryGetValue(itemId, out var draft))
        {
            var left = forget(draft);
            if (left == default)
            {
                _drafts.Remove(itemId);
            }
            else
            {
                _drafts[itemId] = left;
            }
        }
    }

    /// <summary>待っているメモを今書く。別のアバターへ移る前に呼ぶ（移ってからだと、移った先の欄の文になる）</summary>
    private void FlushMemo()
    {
        if (_memoItemId is null)
        {
            return;
        }

        _saveMemo.Cancel();
        SaveMemoAsync().Forget();
    }

    private async Task SaveMemoAsync()
    {
        if (_memoItemId is not { } itemId)
        {
            return;
        }

        _memoItemId = null;
        var memo = _memoInput;
        _writtenMemos[itemId] = memo;

        // 保存できたときは何も出さない（自動で残す欄）。書けなかったときは、別のアバターへ移った後だと欄の下に出しても
        // 別のアバターのメモに見えるので、上の段へ出す
        await WriteAsync(new UiCommand.SetAvatarMemo(itemId, memo), "メモを保存できませんでした。",
            text =>
            {
                if (Selected?.ItemId == itemId)
                {
                    MemoNote.Warn(text);
                }
                else
                {
                    Status = text;
                }
            });
    }

    private async Task AddAliasAsync()
    {
        if (Selected is null)
        {
            return;
        }

        if (Core.Services.NameText.IsTooLong(AliasInput))
        {
            AliasNote.Warn(Core.Services.NameText.TooLongMessage("呼び方"));
            return;
        }

        // 1文字だと当たりが広すぎるので受けない。黙って終わらず、そう言う（I1）
        if (AliasInput.Trim().Length < 2)
        {
            AliasNote.Warn(AliasInput.Trim().Length == 0
                ? "呼び方を入れてから押してください。"
                : "呼び方は2文字以上で入れてください。");
            return;
        }

        var aliasFor = Selected.ItemId;
        var alias = AliasInput;
        if (await WriteAsync(new UiCommand.AddAvatarAlias(aliasFor, alias), "呼び方を追加できませんでした。", AliasNote.Warn) is null)
        {
            return;
        }

        AfterWritten(aliasFor, draft => draft with { Alias = draft.Alias == alias ? null : draft.Alias }, () =>
        {
            if (AliasInput == alias)
            {
                AliasInput = string.Empty;
            }
        });
        await LoadAsync();
    }

    private async Task RemoveAliasAsync(string? display)
    {
        if (Selected is null || display is null)
        {
            return;
        }

        // 表示は「くうた（3）」の形なので、括弧より前を名前として扱う
        var text = display.Split('（')[0];
        if (await WriteAsync(new UiCommand.RemoveAvatarAlias(Selected.ItemId, text), "呼び方を削除できませんでした。", AliasNote.Warn) is null)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task ToggleOwnedAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var next = !Selected.Summary.Entry.IsOwnedManually;
        if (await WriteAsync(new UiCommand.SetAvatarOwned(Selected.ItemId, next), "所有を保存できませんでした。", OwnedNote.Warn) is null)
        {
            // 切り替えの見た目は押した時点で動くので、書いた値に戻す
            await LoadAsync();
            return;
        }

        // 検索の対応アバターの候補は、持っているアバターを先に並べる
        NoteRegistryChanged();
        await LoadAsync();
    }

    private async Task SetOverrideAsync(string? mode)
    {
        if (Selected is null)
        {
            return;
        }

        bool? value = mode switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

        // 繋がったボタンは今の扱いの所も押せる（押せなくすると塗りが薄れて今の扱いが読みにくい）。同じなら書かない
        if (Selected.Summary.Entry.AvatarOverride == value)
        {
            return;
        }

        if (await WriteAsync(new UiCommand.SetAvatarOverride(Selected.ItemId, value), "扱いを保存できませんでした。", JudgementNote.Warn) is not null)
        {
            // アバターとして扱うかで、検索の対応アバターの候補に出るかが変わる
            NoteRegistryChanged();
        }

        await LoadAsync();
    }

    /// <summary>
    /// 書き込みの命令を送り、書けなかったら呼び手の渡した出し先（<paramref name="show"/>）に出す。書けたら結果を、書けなかったら null を返す。
    /// 出し先は押した欄・ボタンのすぐ下の知らせ（<see cref="AreaNotice.Warn"/>）か、画面全体の知らせなら <see cref="ShowInHeader"/>。
    /// 前は一律で上の段に出していたので、押した所から遠く、幅が狭いと切れた（2026-10-03 の方針）
    ///
    /// 命令は書けなかった例外（ファイルを掴まれた・ドライブが外れた）をそのまま投げ、入口は Forget() でログに残すだけなので、
    /// 前は押しても何も起きなかったように見えた（タグ・属性の管理と同じ直し・b94dd15）。素体の改名は多数の商品を書くので、途中で止まることもある
    /// </summary>
    private async Task<CommandResult?> WriteAsync(UiCommand command, string failedText, Action<string> show)
    {
        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                show(failed.Message);
                return null;
            }

            return result;
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("アバターの画面：書き込み", exception);
            show(failedText + Core.Services.FailureText.Cause(exception));
            return null;
        }
    }

    /// <summary>
    /// アバターの記録（名前・素体・所有・扱い）だけを書いた後に、検索の対応アバターの候補と素体の索引を作り直す。
    /// 前は全件の読み直しまで、素体の絞り込みと候補が古いままだった。
    /// 商品は書いていないので全件は読み直さない（2000件で数秒かかる）。タグ・属性のマスタだけを変えたときと同じ扱い
    /// </summary>
    private void NoteRegistryChanged() => _main.RefreshMasters();

    /// <summary>
    /// 素体の改名・削除の後。商品の素体の宣言も書き換えるので、検索の写しごと読み直す（タグ・属性の改名と同じ）
    /// </summary>
    private async Task NoteItemsRewrittenAsync()
    {
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private bool _isRechecking;

    private async Task RecheckAsync()
    {
        // 問い合わせは1.5秒の間隔を空けて並ぶので数秒かかる。待つ間の2度押しで同じアバターを2回取りに行かせない
        // （要確認の「商品情報を取り直す」と同じ・40b3863）
        if (Selected is null || _isRechecking)
        {
            return;
        }

        _isRechecking = true;
        IdNote.Show("BOOTHに問い合わせています…");
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.RecheckAvatar(Selected.ItemId));
            if (result is CommandResult.Failed failed)
            {
                IdNote.Warn(failed.Message);
            }
            else
            {
                IdNote.Show("確認し直しました。");

                // BOOTHの名前や非公開の印が変わると、検索の対応アバターの候補の名前も変わる
                NoteRegistryChanged();
            }

            await LoadAsync();
        }
        catch (Exception exception)
        {
            // 受けないと「問い合わせています…」のまま残り、止まったように見えた
            Core.Diagnostics.AppLog.Error("アバターの画面：確認し直す", exception);
            IdNote.Warn($"確認し直せませんでした。{Core.Services.FailureText.Cause(exception)}");
        }
        finally
        {
            _isRechecking = false;
        }
    }

    /// <summary>
    /// BOOTHの商品ページを開く。右の詳細のボタンからは選んでいるアバター、
    /// 一覧の行の右クリックからはその行（ユーザ指示 2026-09-20・M2）、カードの右クリックと中クリックからはそのカードの商品。
    /// </summary>
    private void OpenBoothPage(object? parameter = null)
    {
        var itemId = parameter switch
        {
            AvatarRowViewModel row => row.ItemId,
            ItemCardViewModel card => card.Item.Id,
            _ => Selected?.ItemId,
        };
        if (itemId is null)
        {
            return;
        }

        // ID は登録簿（手で直せる JSON）から来るので、番号のときだけ商品ページを作り、開くのも1か所の守りを通す
        Services.Shell.OpenUrl(Core.Booth.BoothLinks.ItemPage(itemId));
    }

    /// <summary>このアバター向けの商品を検索で見る。件数だけ見せても何も判断できない。</summary>
    private void ShowItems()
    {
        if (Selected is null)
        {
            return;
        }

        _main.Search.ShowOnlyAvatar(Selected.ItemId, Selected.Name);
        _main.ShowSearch();
    }

    // ---- カードの操作（IItemCardHost） ----
    // カード表示のカードは、枠の Tag にこの画面が入る。受け先が無いと、星・中クリック・Enter が黙って効かなかった
    // （タグ・属性の管理と同じ直し・01aaac3）。星は検索画面の物をそのまま借りる

    /// <summary>
    /// カードを押した（Enter・読み上げの「押す」も）。**商品ページへは移らず、そのアバターを選んで右に詳細を出す。**
    /// この画面のカードは一覧の行そのもので、リスト表示と同じく押したら選ぶ（M4：選ぶ・右に詳細を出すはどちらでも同じに効く）。
    /// マウスでは一覧が先に選んでいるので、ここはキーボードで押したときに効く。商品ページへは詳細の「商品ページを開く」から行ける
    /// </summary>
    public void OpenItem(ItemCardViewModel card)
    {
        if (Rows.FirstOrDefault(row => row.ItemId == card.Item.Id) is { } row)
        {
            Selected = row;
        }
    }

    public void OpenBooth(ItemCardViewModel? card) => OpenBoothPage(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    // ---- 一覧の行の右クリック（ユーザ指示 2026-09-20・M2）。中身は検索画面と同じ命令を借りる ----

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;

    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;

    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;


    /// <summary>右クリックの「改変に追加…」。選びはこの画面に無いので、押した1件だけ（検索の画面と同じ命令）。</summary>

    public RelayCommand CardAddToModificationCommand => _main.Search.CardAddToModificationCommand;

    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand => _main.Search.HideItemCommand;
}
