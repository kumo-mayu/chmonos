using System.Collections.ObjectModel;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 改変の一覧の1行。
///
/// **同じ名前を許してあるので、日付が見分けの手掛かり**（`docs/history/modifications.md` Q27）。
/// </summary>
/// <summary>一覧の1行。</summary>
public sealed class AvatarRowViewModel : ViewModelBase, IHasItemCard
{
    public required AvatarSummary Summary { get; init; }

    /// <summary>この行の商品（アバターは商品でもある）。右クリックをカードと同じにするために持つ（M2）。手元に無ければ null。</summary>
    public ItemRecord? Item { get; init; }

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
    public BoothAssetManager.App.Services.ThumbnailLoader? Thumbnails { get; init; }

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

    /// <summary>頭の絵。持っているアバターは商品の1枚目、持っていないアバターは控えの1枚（U18）。</summary>
    /// <remarks>裏で読み、届いたら描き直す。その場で読むと、画面を開くのが遅れた（<see cref="BoothAssetManager.App.Services.ThumbnailLoader.PeekForTile"/>）。</remarks>
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
        => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || ItemId.Contains(query, StringComparison.Ordinal)
            || BoothName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Summary.Entry.Aliases.Any(alias => alias.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
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
            }
        }
    }

    /// <summary>欄を今の名前から変えたか。「名前を保存」はそのときだけ出す（アバターと同じ作法）。</summary>
    public bool HasNameChange => NameInput.Trim().Length > 0 && NameInput.Trim() != Name;

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
        set => SetField(ref _itemIdInput, value);
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
public sealed partial class AvatarsViewModel : ViewModelBase, IPendingWrites, ILeavingScreen
{
    /// <summary>名前の候補を出す数。並べすぎると選べない。</summary>
    private const int MaxNameSuggestions = 5;

    private readonly AppServiceContainer _services;

    /// <summary>
    /// 離れたら一覧の読み直しを取り消す（既知 P8）。この画面は開くたびに作り直すので、離れた後の一覧は誰も見ない。
    /// **取り消すのは読み直しだけ。**押した書き込み（名前・素体・メモ）は読み直しの前に済んでいるので止まらない
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    public void OnLeaving() => _leaving.Cancel();
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

    /// <summary>開いたときに選んでおくアバター。最初の読み込みで1回だけ使う。</summary>
    private string? _openWith;

    /// <param name="selectItemId">
    /// 開いたときに選んでおくアバター（商品ページの対応アバターの札から来たとき、U13）。
    /// </param>
    public AvatarsViewModel(AppServiceContainer services, MainViewModel main, string? selectItemId = null)
    {
        _services = services;
        _main = main;
        _openWith = selectItemId;

        DetectCommand = new RelayCommand(() => DetectAsync().Forget(), () => !IsDetecting);
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
        ClearBaseCommand = new RelayCommand(() => ClearBaseAsync().Forget());
        AddAliasCommand = new RelayCommand(() => AddAliasAsync().Forget());
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        RenameCommand = new RelayCommand(() => RenameAsync().Forget());
        UseNameSuggestionCommand = new RelayCommand(
            parameter => { if (parameter is string name) { NameInput = name; } },
            parameter => parameter is string);
        RemoveAliasCommand = new RelayCommand(parameter => RemoveAliasAsync(parameter as string).Forget());
        ToggleOwnedCommand = new RelayCommand(() => ToggleOwnedAsync().Forget());
        OpenItemCommand = new RelayCommand(() => OpenItemAsync().Forget());
        RecheckCommand = new RelayCommand(() => RecheckAsync().Forget());
        TreatAsAvatarCommand = new RelayCommand(parameter => SetOverrideAsync(parameter as string).Forget());
        OpenBoothCommand = new RelayCommand(parameter => OpenBooth(parameter));
        ShowItemsCommand = new RelayCommand(ShowItems);
        CreateModificationCommand = new RelayCommand(
            () => CreateModificationAsync().Forget(),
            () => Selected is not null && ModificationNameInput.Trim().Length > 0);
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

    private async Task CreateModificationAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

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

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.CreateModification(row.ItemId, name));

        if (result is CommandResult.Failed failed)
        {
            Status = failed.Message;
            return;
        }

        ModificationNameInput = string.Empty;
        Status = $"改変「{name}」を作りました。";
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
            }
        }
    }

    public string DetectButtonText => IsDetecting ? "検出しています…" : "対応アバターを検出する";

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

    /// <summary>素体を見ているときに選んでいる1つ。</summary>
    public AvatarBaseRowViewModel? SelectedBase
    {
        get => _selectedBase;
        set
        {
            if (SetField(ref _selectedBase, value))
            {
                OnPropertyChanged(nameof(SelectedBaseMembers));
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
    public IReadOnlyList<AvatarRowViewModel> SelectedBaseMembers => SelectedBase is null
        ? []
        : _all
            .Where(row => SelectedBase.Summary.MemberIds.Contains(row.ItemId))
            .OrderByDescending(row => row.IsOwned)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>素体の中のアバターを押すと、アバターの一覧へ切り替えてそれを選ぶ。</summary>
    public RelayCommand OpenMemberCommand => _openMemberCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is not AvatarRowViewModel row)
            {
                return;
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
            }
        }
    }

    public string AliasInput
    {
        get => _aliasInput;
        set => SetField(ref _aliasInput, value);
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
            }
        }
    }

    /// <summary>
    /// 名前の欄を今の名前から変えたか。「名前を保存」はそのときだけ出す（ユーザ指示 2026-09-17：メモは押さずに残るのに、
    /// 名前だけ保存のボタンが常に出ていて揃っていなかった）。名前は一覧の並びと見出しを変えるので、打っている途中では書かない
    /// </summary>
    public bool HasNameChange => Selected is not null && NameInput.Trim().Length > 0 && NameInput.Trim() != Selected.Name;

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
    private readonly record struct AvatarDraft(string Name, string Alias, string Base);

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

                // 離れる前に打ちかけを控える（I5）
                if (_selected is { } leaving)
                {
                    _drafts[leaving.ItemId] = new AvatarDraft(NameInput, AliasInput, BaseInput);
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
        : "所有していない";

    public string OwnedButtonText => Selected?.Summary.Entry.IsOwnedManually == true
        ? "所有の指定を外す"
        : "所有しているものとして扱う";

    /// <summary>どの文脈で候補に挙がったか。判定の根拠なので隠さない。</summary>
    public string SelectedSeenAsText
    {
        get
        {
            if (Selected is null || Selected.Summary.Entry.SeenAs.Count == 0)
            {
                return string.Empty;
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
                Item = _main.Search.FindItem(summary.Entry.ItemId),
                CardFactory = () => _main.Search.CardFor(summary.Entry.ItemId),
                Services = _services,
                IconPathFactory = () => AvatarImageSync.IconPath(
                    _services.Paths, summary.Entry.ItemId, _main.Search.FindItem(summary.Entry.ItemId)),
            }).ToList();

            // 素体の設定を変えると読み直すので、選んでいた素体を名前で戻す
            var selectedBaseName = SelectedBase?.Name;
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
        });
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
        IsDetecting = true;
        Status = "手元の説明文とタグを読んでいます…";

        // **どの画面からでも止められるようにする**（ユーザ判断 2026-09-21・C1）。
        // 止める手立てが一切無く、友人データの初回で約37分ぶら下がっていた
        using var stop = new CancellationTokenSource();
        _main.BeginLongJob("この間、アバターの編集と取り込みの検出は待たされます", stop);

        try
        {
            var progress = new Progress<AvatarDetectProgress>(report =>
            {
                Status = $"{report.Phase}　{report.Done} / {report.Total}";
                _main.ReportLongJob($"対応アバターを検出中　{report.Phase}　{report.Done} / {report.Total}");
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

            if (result.Unresolved > 0)
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
            _main.EndLongJob();
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
            Status = "共通素体の名前を入れてから押してください。";
            return;
        }

        if (Core.Services.NameText.IsTooLong(BaseInput))
        {
            Status = Core.Services.NameText.TooLongMessage("共通素体の名前");
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarBase(Selected.ItemId, BaseInput));
        _drafts.Remove(Selected.ItemId);
        await LoadAsync();
    }

    private async Task ClearBaseAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarBase(Selected.ItemId, null));
        BaseInput = string.Empty;
        await LoadAsync();
    }

    /// <summary>
    /// 素体に配布商品を結ぶ。空にすると外れる。
    /// 数字でもBOOTHの商品URLでも受ける（ブラウザから来るのは普通URLの方）。
    /// </summary>
    private async Task SetBaseItemIdAsync(string name, string input)
    {
        var trimmed = input.Trim();

        if (trimmed.Length == 0)
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetBaseItemId(name, null));
            Status = $"「{name}」の配布商品との紐付けを外しました。";
            await LoadAsync();
            return;
        }

        var itemId = Core.Services.BoothItemId.Parse(trimmed);
        if (itemId is null)
        {
            Status = "商品IDが読み取れませんでした。数字か、BOOTHの商品ページのURLを入れてください。";
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetBaseItemId(name, itemId));
        Status = $"「{name}」を商品 {itemId} に紐付けました。";
        await LoadAsync();
    }

    private async Task ToggleInferAsync(string name, bool infer)
    {
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetBaseInferClothing(name, infer));
        Status = infer
            ? $"「{name}」の一致から衣装の互換を広げます。"
            : $"「{name}」の一致では衣装の互換を広げません。";
        await LoadAsync();
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
            Status = "新しい素体の名前を入れてから押してください。";
            return;
        }

        if (Core.Services.NameText.IsTooLong(newName))
        {
            Status = Core.Services.NameText.TooLongMessage("共通素体の名前");
            return;
        }

        if (newName == oldName)
        {
            return;
        }

        RenameBaseAsync(oldName, newName).Forget();
    }

    private async Task RenameBaseAsync(string oldName, string newName)
    {
        var updated = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RenameBase(oldName, newName))
            is Core.Commands.CommandResult.Counted renamed ? renamed.Count : 0;
        Status = $"「{oldName}」を「{newName}」に変え、商品 {updated} 件を書き換えました。";
        await LoadAsync();
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
        var updated = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.DeleteBase(name))
            is Core.Commands.CommandResult.Counted deleted ? deleted.Count : 0;
        Status = $"「{name}」を削除し、商品 {updated} 件を書き換えました。";
        await LoadAsync();
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

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarName(Selected.ItemId, string.Empty));
        _drafts.Remove(Selected.ItemId);
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
            Status = "名前を入れてから押してください。";
            return;
        }

        if (Core.Services.NameText.IsTooLong(newName))
        {
            Status = Core.Services.NameText.TooLongMessage("アバターの名前");
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarName(Selected.ItemId, newName));
        _drafts.Remove(Selected.ItemId);
        await LoadAsync();
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
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarMemo(itemId, memo));
        Status = memo.Trim().Length == 0 ? "メモを消しました。" : "メモを保存しました。";
    }

    private async Task AddAliasAsync()
    {
        if (Selected is null)
        {
            return;
        }

        if (Core.Services.NameText.IsTooLong(AliasInput))
        {
            Status = Core.Services.NameText.TooLongMessage("呼び方");
            return;
        }

        // 1文字だと当たりが広すぎるので受けない。黙って終わらず、そう言う（I1）
        if (AliasInput.Trim().Length < 2)
        {
            Status = AliasInput.Trim().Length == 0
                ? "呼び方を入れてから押してください。"
                : "呼び方は2文字以上で入れてください。";
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddAvatarAlias(Selected.ItemId, AliasInput));
        AliasInput = string.Empty;
        _drafts.Remove(Selected.ItemId);
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
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RemoveAvatarAlias(Selected.ItemId, text));
        await LoadAsync();
    }

    private async Task ToggleOwnedAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var next = !Selected.Summary.Entry.IsOwnedManually;
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarOwned(Selected.ItemId, next));
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

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SetAvatarOverride(Selected.ItemId, value));
        await LoadAsync();
    }

    private async Task RecheckAsync()
    {
        if (Selected is null)
        {
            return;
        }

        Status = "BOOTHに問い合わせています…";
        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RecheckAvatar(Selected.ItemId));
        Status = result is Core.Commands.CommandResult.Failed failed ? failed.Message : "確認し直しました。";
        await LoadAsync();
    }

    /// <summary>
    /// BOOTHの商品ページを開く。右の詳細のボタンからは選んでいるアバター、
    /// 一覧の行の右クリックからはその行（ユーザ指示 2026-09-20・M2）。
    /// </summary>
    private void OpenBooth(object? parameter = null)
    {
        var target = parameter as AvatarRowViewModel ?? Selected;
        if (target is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://booth.pm/ja/items/{target.ItemId}",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 開けなくてもアプリは動き続ける
        }
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

    // ---- 一覧の行の右クリック（ユーザ指示 2026-09-20・M2）。中身は検索画面と同じ命令を借りる ----

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;

    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;

    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;

    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand => _main.Search.HideItemCommand;
}
