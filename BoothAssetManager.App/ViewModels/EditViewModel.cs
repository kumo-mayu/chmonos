using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 編集画面。取り込んだitemに、ユーザにしか決められない情報を入れていく。
///
/// 複数件を順に処理する形にしているのは、この作業が
/// 「1件ずつ判断して次へ送る」という性質のものだから（設計メモの通り）。
/// 位置は1件進むごとに edit-session.json へ書くので、途中で閉じても続きから再開できる。
///
/// userTagも属性も、マスタを全部並べるのではなく候補付きの入力欄から積む。
/// 並べる方式は分類が増えるほど画面が縦に伸び、使えなくなるため。
/// </summary>
public sealed partial class EditViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private PaneColumn? _rightPane;

    /// <summary>右の入力欄の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn RightPane => _rightPane ??= new PaneColumn(_services.PaneWidths, "edit.right");
    private readonly MainViewModel _main;

    /// <summary>
    /// 続く商品の帯の右クリック（カードと同じメニュー・M2）が使う命令の持ち主。
    /// この画面にも同じ名前の命令（BOOTHで開く・エクスプローラで開く）があるので、検索画面をそのまま渡す
    /// </summary>
    public SearchViewModel CardHost => _main.Search;
    private readonly ThumbnailLoader _thumbnails;
    private readonly DispatcherTimer _returnTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private UserTagMaster _tagMaster = new();
    private AttributeMaster _attributeMaster = new();

    /// <summary>
    /// 手元にあるショップ名。入力欄の候補に出して、二重に作る事故を防ぐ。
    /// 画面を開いて最初の1件で全件から作り、あとは保存した名前を足していく。
    /// </summary>
    private List<string> _shopNames = [];
    private bool _shopNamesLoaded;

    /// <summary>候補に出す数。並べすぎると読めない。</summary>
    private const int ShopSuggestionLimit = 6;

    /// <summary>分類の候補に出す数。3Dモデルの子が12件なので、それが収まる数。</summary>
    private const int CategorySuggestionLimit = 12;
    private List<string> _queue = [];
    private int _index;
    private int _remainingSeconds;
    private ItemRecord? _item;
    private string _memo = string.Empty;
    private string _displayName = string.Empty;
    private string _shopNameInput = string.Empty;
    private string _shopUrlInput = string.Empty;
    private string _categoryInput = string.Empty;
    private string _acquiredAt = string.Empty;
    private bool _notifyOnUpdate = true;
    private bool _isHidden;
    private string _statusText = string.Empty;
    private bool _isSaving;
    private bool _isEditingBasics;

    public EditViewModel(AppServiceContainer services, MainViewModel main, ThumbnailLoader thumbnails)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        SaveAndNextCommand = new RelayCommand(() => SaveAndAdvanceAsync().Forget(), () => HasItem && !IsSaving);
        SkipCommand = new RelayCommand(() => SkipAsync().Forget(), () => HasItem && !IsSaving);
        BackCommand = new RelayCommand(GoBack, () => _index > 0);
        FinishCommand = new RelayCommand(() => FinishAsync().Forget());
        // 仮IDの商品にはBOOTHページが無い。押せると404へ送ることになる
        OpenBoothCommand = new RelayCommand(OpenBooth, () => HasItem && !IsLocalOnly);
        AddTagCommand = new RelayCommand(parameter => AddTagAsync(parameter as string).Forget());
        AddAttributeCommand = new RelayCommand(parameter => AddAttributeAsync(parameter as string).Forget());
        UseCategoryCommand = new RelayCommand(
            parameter => { if (parameter is string name) { CategoryInput = name; } },
            parameter => parameter is string);
        UseShopCommand = new RelayCommand(
            parameter => { if (parameter is string name) { ShopNameInput = name; } },
            parameter => parameter is string);
        StayCommand = new RelayCommand(StopReturnTimer);
        EditBasicsCommand = new RelayCommand(() => IsEditingBasics = !IsEditingBasics, () => HasItem);

        _returnTimer.Tick += OnReturnTick;
    }

    public RelayCommand SaveAndNextCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand FinishCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand AddTagCommand { get; }

    public RelayCommand AddAttributeCommand { get; }

    public RelayCommand StayCommand { get; }

    /// <summary>商品名・ショップ・BOOTH分類名の欄を開く／閉じる。閉じても打った内容は残り、保存で書かれる。</summary>
    public RelayCommand EditBasicsCommand { get; }

    /// <summary>
    /// 商品名・ショップ・BOOTH分類名の欄を開いているか。
    /// **普段は変える必要が無い**ので閉じておき、右の入力はユーザータグから始める（ユーザ指示）。
    /// 常に開いていると、毎回の編集で使わない欄の分だけ下へ送られる。
    /// </summary>
    public bool IsEditingBasics
    {
        get => _isEditingBasics;
        set
        {
            if (SetField(ref _isEditingBasics, value))
            {
                OnPropertyChanged(nameof(EditBasicsLabel));
            }
        }
    }

    public string EditBasicsLabel => IsEditingBasics ? "閉じる" : "編集";

    /// <summary>付けたuserTag。マスタ全部ではなく、選んだものだけが並ぶ。</summary>
    public ObservableCollection<UserTagRow> Tags { get; } = [];

    /// <summary>評価した属性。評価していないものは行自体が無い。</summary>
    public ObservableCollection<AttributeRow> Attributes { get; } = [];

    public ObservableCollection<OrderedVariationInput> Variations { get; } = [];

    /// <summary>userTagトップの候補。既に付けたものは出さない。</summary>
    public ObservableCollection<string> TagSuggestions { get; } = [];

    /// <summary>属性の候補。既に評価したものは出さない。</summary>
    public ObservableCollection<string> AttributeSuggestions { get; } = [];

    private string _tagInput = string.Empty;
    private string _attributeInput = string.Empty;

    /// <summary>
    /// ユーザータグの欄の**打ちかけ**（ユーザ判断 2026-09-20・I4）。
    /// 編集画面は「打ちかけを消さない」約束を持つのに、候補付きの欄に打った途中の字だけが控えに入らず、
    /// 商品を移ると黙って消えていた。決める前の字も控えに入れる。
    /// </summary>
    public string TagInput
    {
        get => _tagInput;
        set => SetField(ref _tagInput, value ?? string.Empty);
    }

    /// <summary>属性の欄の打ちかけ（I4）。</summary>
    public string AttributeInput
    {
        get => _attributeInput;
        set => SetField(ref _attributeInput, value ?? string.Empty);
    }

    public ItemRecord? Item => _item;

    public bool HasItem => _item is not null;

    public bool IsFinished => !HasItem;

    public string StepText => _queue.Count == 0 ? string.Empty : $"{Math.Min(_index + 1, _queue.Count)} / {_queue.Count} 件";

    public double StepProgress => _queue.Count == 0 ? 0 : (double)_index / _queue.Count * 100;

    /// <summary>
    /// 左の下見に出す名前。**入力欄に打った内容をそのまま映す。**
    ///
    /// 保存済みの値を出していると、空欄にしても下見が変わらないので
    /// 「消せていない」ように見える。空欄にすればBOOTHの名前に戻ることが、
    /// 保存する前に目で分かる形にする。
    /// </summary>
    public string Name => _item is null
        ? string.Empty
        : DisplayText.ItemName(DisplayName, _item.Booth.Name, _item.Id);

    /// <summary>下見のショップ名。こちらも打った内容を映す。</summary>
    public string ShopName => DisplayText.ShopName(ShopNameInput, _item?.Booth.Shop?.Name);

    /// <summary>下見の分類。打った子の名前から、親は同梱の表で補う。</summary>
    public string CategoryText => _services.Categories.TextFor(CategoryInput, _item?.Booth.Category);

    private ItemViewModel? _itemPage;

    /// <summary>
    /// 今の商品の、商品ページと同じ操作の持ち主（ユーザ判断 2026-09-12：JSONに関わる編集は商品ページと同等にする）。
    /// 左の画像・対応アバター・手元のファイルの欄、名前の横の星・ID・「IDを変える」・「BOOTHから取り直す」はこれに繋ぐ。
    ///
    /// **書き込みはその場で保存する**（ユーザ判断：保存のタイミングは商品ページと同じ）。
    /// 右の入力とは持つ項目が重ならない（右は <see cref="LocalOwners.EditScreen"/>）ので、
    /// 「保存して次へ」がここで直したものを古い写しで戻すことは無い。
    /// 商品が変わるたびに作り直し、終えたら null。
    /// </summary>
    public ItemViewModel? ItemPage
    {
        get => _itemPage;
        private set => SetField(ref _itemPage, value);
    }

    /// <summary>
    /// 左の欄が商品を開き直したとき（取り直した・ファイルやフォルダを外した・IDを変えた）。
    /// 商品ページなら画面ごと作り直すが、ここでは編集の中で読み直す。
    /// 右の打ちかけの入力は、書きかけとして控えてから読み直した記録に重ね直す（消さない）。
    /// </summary>
    private void OnItemPageReplaced(ItemRecord? updated)
    {
        if (_item is null)
        {
            return;
        }

        var previousId = _item.Id;
        CaptureDraft();

        if (updated is null)
        {
            // 最後のファイルを外して商品ごと消えた。読み直すと、消えた商品を飛ばして次へ進む
            _main.Drafts.Remove(previousId);
        }
        else if (!string.Equals(updated.Id, previousId, StringComparison.Ordinal))
        {
            // IDを変えた。順番の中の商品も、書きかけも保存した印も、移した先に付け替える
            _queue[_index] = updated.Id;
            if (_main.Drafts.Get(previousId) is { } draft)
            {
                _main.Drafts.Remove(previousId);
                _main.Drafts.Put(updated.Id, draft);
            }

            if (_saved.Remove(previousId))
            {
                _saved.Add(updated.Id);
            }

            ReplaceInSessionAsync(previousId, updated.Id).Forget();
        }

        LoadCurrentAsync().Forget();
    }

    /// <summary>
    /// 未編集の順番（ファイルに残す方）の中のIDを付け替える。指定して入った順番は
    /// <see cref="_queue"/> そのものが履歴に預けた控えなので、上で付け替えた時点で済んでいる。
    /// </summary>
    private async Task ReplaceInSessionAsync(string fromId, string toId)
    {
        if (_run is null)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.ReplaceEditSessionItemId(fromId, toId));
        }
    }

    public string DescriptionPreview => _item?.Booth.Description ?? string.Empty;

    public IReadOnlyList<string> BoothTags => _item?.Booth.Tags ?? [];

    /// <summary>BOOTHのタグの見出しに添える件数。畳んでいても何件あるかは分かるように。</summary>
    public string BoothTagsCountText => $"（{BoothTags.Count}）";

    /// <summary>
    /// 並べるタグの札。畳んでいる間は作らず、多い商品は一部だけ。作った札は隠すだけで捨てない
    /// （商品ページと同じ・<see cref="ChipStrip{TSource}"/>）。商品を移ったときだけ作り直す
    /// </summary>
    private readonly ChipStrip<string> _tagStrip = ChipLists.TagStrip();

    public ObservableCollection<object> BoothTagTiles => _tagStrip.Tiles;

    /// <summary>BOOTHのタグを開いているか。商品ページと共通（<see cref="SectionFolds"/>・ユーザ指示 2026-09-12）。</summary>
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
                _tagStrip.SetExpanded(value);
            }
        }
    }

    /// <summary>
    /// 編集を終えると行く先の名前。**検索とは限らない**——商品を指定して入った編集は入る前の画面へ戻る（B1）。
    /// 名乗りと実際の行き先が違うと、押した後に迷子になる。
    /// </summary>
    public string FinishTargetName => _run is null ? "検索" : _main.LeaveEditLabel;

    /// <summary>終えるボタンの文言。行き先を名乗る。</summary>
    public string FinishButtonText => $"{FinishTargetName}に戻る";

    /// <summary>自動で戻るまでのカウントダウン。0なら出さない。</summary>
    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set
        {
            if (SetField(ref _remainingSeconds, value))
            {
                OnPropertyChanged(nameof(ReturnNoticeText));
                OnPropertyChanged(nameof(IsReturning));
            }
        }
    }

    public bool IsReturning => RemainingSeconds > 0;

    public string ReturnNoticeText => $"{RemainingSeconds} 秒後に{FinishTargetName}に戻ります。";

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetField(ref _isSaving, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Memo
    {
        get => _memo;
        set => SetField(ref _memo, value);
    }

    /// <summary>
    /// 自分で付ける商品名。BOOTHから取れない商品はこれしか名前が無い。
    /// 空欄ならBOOTHの名前に戻る（消せるようにしておかないと、付けた名前を取り消せない）。
    /// </summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                // 左の下見も一緒に動かす。動かないと「消せていない」ように見える
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>BOOTHから取れている名前。入力欄の下に出して、何に戻るのかを見せる。</summary>
    public string BoothName => _item?.Booth.Name ?? string.Empty;

    /// <summary>
    /// 自分で入れるショップ名。**商品が非公開でもショップは見られる場合がある。**
    /// 空欄ならBOOTHから取れているショップに戻る。
    /// </summary>
    public string ShopNameInput
    {
        get => _shopNameInput;
        set
        {
            if (SetField(ref _shopNameInput, value))
            {
                OnPropertyChanged(nameof(ShopKeyNote));
                OnPropertyChanged(nameof(ShopName));
                RefreshShopSuggestions();
            }
        }
    }

    /// <summary>
    /// ショップのURL。**貼れば本物のサブドメインが取れ、既にあるショップに正しく束ねられる。**
    /// 貼らなければ手元だけの鍵になる。
    /// </summary>
    public string ShopUrlInput
    {
        get => _shopUrlInput;
        set
        {
            if (SetField(ref _shopUrlInput, value))
            {
                OnPropertyChanged(nameof(ShopKeyNote));
            }
        }
    }

    /// <summary>
    /// 何が起きるかを押す前に出す。URLを貼ったかどうかで結果が変わるので、
    /// 黙って分岐させない。
    /// </summary>
    public string ShopKeyNote
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ShopNameInput))
            {
                return _item?.Booth.Shop is { } booth
                    ? $"空欄にするとBOOTHのショップ「{booth.Name}」に戻ります。"
                    : string.Empty;
            }

            if (LocalShopKey.SubdomainFromUrl(ShopUrlInput) is { } subdomain)
            {
                return $"BOOTHのショップ {subdomain} に束ねます。アイコンやバナーもそちらのものになります。";
            }

            return string.IsNullOrWhiteSpace(ShopUrlInput)
                ? "URLが無いので、手元だけのショップになります。同じ名前を入れれば同じショップに束ねます。"
                : "ショップのURLとして読めませんでした（https://〇〇.booth.pm/ の形）。このままだと手元だけのショップになります。";
        }
    }

    /// <summary>
    /// 手元にあるショップ名の候補。**二重に作る事故をほぼ防げる。**
    /// 鍵は名前から決まるので、候補から選べば同じショップに束ねられる。
    /// </summary>
    public ObservableCollection<string> ShopSuggestions { get; } = [];

    public bool HasShopSuggestions => ShopSuggestions.Count > 0;

    public RelayCommand UseShopCommand { get; }

    /// <summary>
    /// 自分で入れる分類。**子の名前1つだけ。**
    /// 空欄ならBOOTHから取れている分類に戻る。
    /// </summary>
    public string CategoryInput
    {
        get => _categoryInput;
        set
        {
            if (SetField(ref _categoryInput, value))
            {
                OnPropertyChanged(nameof(CategoryText));
                RefreshCategorySuggestions();
            }
        }
    }

    /// <summary>
    /// 分類の候補。**同梱したBOOTHのカテゴリ表から出す。**
    /// 3Dモデルの子を先に出すが、残りも全部並ぶ——
    /// 選べる範囲を勝手に狭めると、BOOTHにある分類が入れられなくなる。
    /// </summary>
    public ObservableCollection<string> CategorySuggestions { get; } = [];

    public bool HasCategorySuggestions => CategorySuggestions.Count > 0;

    /// <summary>BOOTHから取れている分類。空欄にすると何に戻るのかを見せる。</summary>
    public string BoothCategory => _item?.Booth.Category?.Name ?? string.Empty;

    public bool HasBoothCategory => BoothCategory.Length > 0;

    public RelayCommand UseCategoryCommand { get; }

    public bool HasBoothName => BoothName.Length > 0;

    // ---- 空欄のときに欄そのものが名乗る既定 ----
    //
    // **空欄は「入力を求めている」と読まれる。**既定が決まっているなら、
    // 空欄のままで何になるかを placeholder で見せる。
    // BOOTHから取れていない商品には出す既定が無いので、そのときは
    // 「入れてください」側の文になる（そこは実際に入力を求めている）。

    public string NamePlaceholder => HasBoothName
        ? BoothName
        : "商品名を入れてください（BOOTHから取れていません）";

    public string ShopPlaceholder => _item?.Booth.Shop?.Name is { Length: > 0 } shop
        ? shop
        : "ショップ名を入れると、同じショップの商品がまとまります";

    public string CategoryPlaceholder => HasBoothCategory
        ? BoothCategory
        : "BOOTHの分類名を入れると、統計と絞り込みに出てきます";

    /// <summary>BOOTHに無い商品として登録したもの。名前を空欄にすると仮IDが出てしまう</summary>
    public bool IsLocalOnly => _item?.IsLocalOnly ?? false;

    public string OpenBoothTip => IsLocalOnly
        ? "BOOTHに無い商品として登録したものなので、開く先がありません。"
        : "BOOTHの商品ページをブラウザで開きます（アプリの外へ出ます）。";

    /// <summary>入手日。空欄ならファイルの日付にフォールバックする（保存時にnullを書く）。</summary>
    public string AcquiredAt
    {
        get => _acquiredAt;
        set
        {
            if (SetField(ref _acquiredAt, value))
            {
                OnPropertyChanged(nameof(AcquiredHintText));
            }
        }
    }

    /// <summary>
    /// 空欄のときに実際に使われる日付。何が採用されるのかを伏せない。
    /// </summary>
    public string AcquiredHintText
    {
        get
        {
            if (_acquiredAt.Trim().Length > 0 || _item is null)
            {
                return "yyyy-MM-dd";
            }

            var acquired = AcquiredDateResolver.Resolve(_item);
            return acquired.Value is { } date
                ? $"空欄のまま → {date:yyyy-MM-dd}（ファイルの日付）"
                : "yyyy-MM-dd（ファイルが無いため空欄のまま）";
        }
    }

    public bool NotifyOnUpdate
    {
        get => _notifyOnUpdate;
        set => SetField(ref _notifyOnUpdate, value);
    }

    public bool IsHidden
    {
        get => _isHidden;
        set => SetField(ref _isHidden, value);
    }

    /// <summary>
    /// キューを積んで最初の1件を開く。
    /// <paramref name="itemIds"/> が空なら、userTag未設定のitemを対象にする（ナビのバッジと同じ定義）。
    /// </summary>
    public async Task StartAsync(IReadOnlyList<string>? itemIds = null)
    {
        if (itemIds is null)
        {
            _run = null;
            _queue = await BuildDefaultQueueAsync();
            _index = 0;
            _saved = new HashSet<string>(StringComparer.Ordinal);
            await _services.Commands.ExecuteAsync(new UiCommand.StartEditSession(_queue));
        }
        else
        {
            // 指定して入った編集は、未編集の順番の記録（edit-session.json）を上書きしない。画面の履歴に預ける（ユーザ判断）
            _run = new EditRun { ItemIds = itemIds.ToList() };
            _queue = _run.ItemIds;
            _index = 0;
            _saved = _run.Saved;
        }

        await LoadCurrentAsync();
    }

    /// <summary>前回の続きを開く。残っていなければ新しく積み直す。</summary>
    /// <param name="itemId">画面の履歴から戻るときに開いていた商品。null ならナビから入った（記録の位置から）。</param>
    public async Task ResumeAsync(string? itemId = null)
    {
        var session = _services.Store.EditSession.Load();
        if (session.ItemIds.Count == 0 || (itemId is null && session.IsFinished))
        {
            await StartAsync();
            return;
        }

        _run = null;

        if (itemId is not null)
        {
            // 画面の履歴から戻った。そのときに開いていた商品を開く。入り直したときに保存した商品を外していて
            // 順番に無ければ、その1件だけを開く（戻った先で別の商品が出るより分かりやすい）
            var at = session.ItemIds.ToList().IndexOf(itemId);
            if (at < 0)
            {
                await StartAsync([itemId]);
                return;
            }

            _queue = session.ItemIds.ToList();
            _index = at;
            _saved = new HashSet<string>(session.SavedItemIds, StringComparer.Ordinal);
            await SavePositionAsync();
            await LoadCurrentAsync();
            return;
        }

        // ナビから入り直した。**保存した商品はもう出さない**（ユーザ指示 2026-09-12）——残すのは未編集と書きかけだけ。
        // 同じ回の間は帯に緑の印で残して戻れるようにしてあるが、離れて入り直したら片付いたものは要らない。
        // 他の入り方でタグを付けた商品（写しでユーザータグがある）も外す。書きかけのある商品は保存していても残す
        // 消えた商品も外す（写しに無いだけの商品は取り込み中かもしれないので、ディスクで確かめる。
        // 残すと帯に絵の無い枠が並んだ）
        var saved = session.SavedItemIds.ToHashSet(StringComparer.Ordinal);
        bool Keep(string id) => _services.Store.Items.Exists(id)
            && (_main.Drafts.Contains(id)
                || (!saved.Contains(id) && _main.Search.FindItem(id) is not { Local.UserTags.Count: > 0 }));

        var kept = session.ItemIds.Where(Keep).ToList();
        if (kept.Count == 0)
        {
            await StartAsync();
            return;
        }

        // 位置は、外した分だけ前へ詰める。前回の位置より後ろに何も残っていなければ（残りが飛ばした物だけ）、先頭から
        var index = session.ItemIds.Take(session.Index).Count(Keep);
        if (index >= kept.Count)
        {
            index = 0;
        }

        if (kept.Count != session.ItemIds.Count || saved.Count > 0)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.StartEditSession(kept));
            await _services.Commands.ExecuteAsync(new UiCommand.AdvanceEditSession(index));
        }

        _queue = kept;
        _index = index;
        _saved = new HashSet<string>(StringComparer.Ordinal);
        await LoadCurrentAsync();
    }

    /// <summary>今の位置。画面の履歴に、どの商品を開いていたかを預けるため。</summary>
    public int Index => _index;

    /// <summary>今開いている商品。画面の履歴は位置ではなく商品で覚える（入り直すと順番が詰まるため）。</summary>
    public string? CurrentItemId => _item?.Id;

    /// <summary>
    /// 指定して入った編集の順番と位置。null なら未編集の順番（edit-session.json に持つ）。
    /// </summary>
    private EditRun? _run;

    /// <summary>画面の履歴に預けるため。指定して入った編集だけが持つ。</summary>
    public EditRun? Run => _run;

    /// <summary>画面の履歴から、指定して入った編集を続きから開く。</summary>
    public async Task ResumeRunAsync(EditRun run, string? itemId = null)
    {
        if (itemId is not null && run.ItemIds.IndexOf(itemId) is var at and >= 0)
        {
            run.Index = at;
        }

        _run = run;
        _queue = run.ItemIds;
        _index = Math.Clamp(run.Index, 0, _queue.Count);
        _saved = run.Saved;
        await LoadCurrentAsync();
    }

    /// <summary>位置を控える。未編集の順番はファイルへ、指定して入った順番は履歴に預けた控えへ。</summary>
    private Task SavePositionAsync()
    {
        if (_run is not null)
        {
            _run.Index = _index;
            return Task.CompletedTask;
        }

        return _services.Commands.ExecuteAsync(new UiCommand.AdvanceEditSession(_index));
    }

    /// <summary>
    /// 現在位置のitemを読み、入力欄を今の値で埋める。
    /// キューを積んだ後に消えているitemは飛ばす。
    /// </summary>
    private async Task LoadCurrentAsync()
    {
        while (_index < _queue.Count)
        {
            var record = await _services.Store.Items.LoadAsync(_queue[_index]);
            if (record is not null)
            {
                _item = record;

                // 「残り n 件を表示」で全部並べたのは、その商品を開いている間だけ
                _tagStrip.Reset(BoothTags, IsBoothTagsExpanded, showAll: false);
                _tagMaster = _services.Store.UserTags.Load();
                _attributeMaster = _services.Store.Attributes.Load();
                // 店名の候補は画面を開いて1回だけ作る。以前は1件進むたびに全件を読み直していて、
                // 2000件の保存先では「スキップ」30回で全件の読み込みが30回走り、500MBを超えた（#71）
                if (!_shopNamesLoaded)
                {
                    _shopNames = await LoadShopNamesAsync();
                    _shopNamesLoaded = true;
                }

                FillFromItem(record);
                _baseline = BuildLocal(record);

                // 前の商品の打ちかけを持ち越さない（この商品の控えがあれば、すぐ下で戻す・I4）
                TagInput = string.Empty;
                AttributeInput = string.Empty;

                // 書きかけがあれば、読み直した記録に変えた項目だけを重ねて埋め直す
                if (_main.Drafts.Get(record.Id) is { } draft)
                {
                    FillFromItem(record with { Local = LocalFields.Merge(record.Local, draft.Local, draft.Changed) });
                    foreach (var (hash, variation) in draft.FileVariations)
                    {
                        if (_fileVariations.ContainsKey(hash))
                        {
                            _fileVariations[hash] = variation;
                        }
                    }

                    RefreshFileLinks();

                    // 決める前の打ちかけも戻す（I4）
                    TagInput = draft.TagInput;
                    AttributeInput = draft.AttributeInput;
                }

                // BOOTHから名前が取れていない商品は、ここを埋めないと名前が無い。
                // 閉じたままだと入れる場所が見えないので、その商品だけ開いて出す
                IsEditingBasics = record.Booth.Name is not { Length: > 0 };
                ItemPage = new ItemViewModel(record, _services, _main, _thumbnails, forEditing: true)
                {
                    Replaced = OnItemPageReplaced,
                };
                RaiseItemChanged();
                return;
            }

            _index++;
        }

        _item = null;
        _baseline = null;
        _tagStrip.Reset([], expanded: false, showAll: false);
        ItemPage = null;
        RaiseItemChanged();
        StartReturnTimer();
    }

    private void FillFromItem(ItemRecord record)
    {
        Tags.Clear();
        foreach (var assignment in record.Local.UserTags)
        {
            var row = CreateTagRow(assignment.Top);
            foreach (var sub in assignment.Subs)
            {
                row.Subs.Add(sub);
            }

            row.Raise();
            RefreshSubCandidates(row);
            Tags.Add(row);
        }

        Attributes.Clear();
        foreach (var pair in record.Local.Attributes.OrderByDescending(pair => pair.Value))
        {
            Attributes.Add(CreateAttributeRow(pair.Key, pair.Value));
        }

        // 既定に指定した属性を、まだ付いていない分だけ並べておく（ユーザ指示）。
        // **並べるだけで保存はしない。**触られた行だけが書き出される
        foreach (var definition in _attributeMaster.Attributes.Where(entry => entry.IsDefault))
        {
            if (Attributes.Any(row => string.Equals(row.Name, definition.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            var row = CreateAttributeRow(definition.Name, 50);
            row.IsSuggested = true;
            Attributes.Add(row);
        }

        Memo = record.Local.Memo ?? string.Empty;
        DisplayName = record.Local.DisplayName ?? string.Empty;
        ShopNameInput = record.Local.Shop?.Name ?? string.Empty;
        ShopUrlInput = record.Local.Shop?.Url ?? string.Empty;
        CategoryInput = record.Local.Category ?? string.Empty;
        AcquiredAt = record.Local.AcquiredAt?.ToString("yyyy-MM-dd") ?? string.Empty;
        OnPropertyChanged(nameof(AcquiredHintText));
        NotifyOnUpdate = record.Local.NotifyOnUpdate;
        IsHidden = record.Local.IsHidden;

        BuildVariations(record);
        BuildFileLinks(record);
        RefreshSuggestions();
    }

    /// <summary>
    /// 入力を <c>local</c> の形に組み直す。
    ///
    /// この画面が持つのは <see cref="LocalOwners.EditScreen"/> の項目だけで、
    /// それ以外はここで何を入れても保存時に捨てられる（読み直したものが残る）。
    /// </summary>
    private LocalBlock BuildLocal(ItemRecord record)
    {
        var userTags = Tags
            .Select(row => new UserTagAssignment { Top = row.Top, Subs = row.Subs.ToList() })
            .ToList();

        // **並べてあるだけの行は書き出さない**（ユーザ指示）。全itemに同じ値の行が並ぶと、
        // 付けていないのかそう評価したのかが区別できなくなる。
        // 「未評価は行が無いことで表す」という決め方を崩さない
        var attributes = Attributes
            .Where(row => !row.IsSuggested)
            .ToDictionary(row => row.Name, row => row.Value);

        // 買った1回が1レコード。版の行が1件目、その下にぶら下げたものが2件目以降。
        // ExistsOnBooth は保存側で計算し直されるので、ここでの値は目安にすぎない
        var ordered = Variations
            .Where(variation => variation.IsPurchased)
            .SelectMany(variation => new[]
            {
                new Purchase
                {
                    VariationId = variation.VariationId,
                    NameSnapshot = variation.Name,
                    Price = Core.Services.MoneyText.Parse(variation.Price),
                    Kind = variation.Kind,
                    ExistsOnBooth = !variation.IsGone,
                },
            }.Concat(variation.Extras.Select(extra => new Purchase
            {
                VariationId = variation.VariationId,
                NameSnapshot = extra.NameSnapshot ?? variation.Name,
                Price = Core.Services.MoneyText.Parse(extra.Price),
                Kind = extra.Kind,
                Note = extra.Note,
                ExistsOnBooth = !variation.IsGone,
            })))
            .ToList();

        return record.Local with
        {
            UserTags = userTags,
            Attributes = attributes,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
            Shop = BuildShop(),
            Category = string.IsNullOrWhiteSpace(CategoryInput) ? null : CategoryInput.Trim(),
            Memo = string.IsNullOrWhiteSpace(Memo) ? null : Memo.Trim(),
            Purchases = ordered,
            // 人が打つ書き方を広く受ける（I3。`DateText` は検索の日付の欄と同じ読み取り）。
            // 前は `DateOnly.TryParse` だけで、読めない書き方は黙って空になり、支出の統計から静かに落ちていた
            AcquiredAt = Core.Services.DateText.Parse(AcquiredAt, isEnd: false, DateOnly.FromDateTime(DateTime.Today)),
            NotifyOnUpdate = NotifyOnUpdate,
            IsHidden = IsHidden,
        };
    }

    /// <summary>
    /// 打ってあるのに読めなかった欄を1行で言う（ユーザ判断 2026-09-20・I3）。
    /// **止めはしない**——入力を突き返すと、ほかの欄まで保存できなくなる。空欄は何も言わない（入れていないだけ）。
    /// </summary>
    private string UnreadableNotice()
    {
        var unreadable = new List<string>();

        if (AcquiredAt.Trim().Length > 0
            && Core.Services.DateText.Parse(AcquiredAt, isEnd: false, DateOnly.FromDateTime(DateTime.Today)) is null)
        {
            unreadable.Add($"入手日「{AcquiredAt.Trim()}」");
        }

        var prices = Variations
            .Where(variation => variation.IsPurchased)
            .SelectMany(variation => new[] { variation.Price }.Concat(variation.Extras.Select(extra => extra.Price)))
            .Where(Core.Services.MoneyText.IsUnreadable)
            .Select(price => $"金額「{price.Trim()}」")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        unreadable.AddRange(prices);

        return unreadable.Count == 0
            ? string.Empty
            : $"{string.Join("・", unreadable)}は読めなかったので、空のまま保存しました。"
                + "日付は「2026-09-20」「2026/9/20」、金額は「1200」「¥1,200」のように入れられます。";
    }

    private async Task SaveAndAdvanceAsync()
    {
        if (_item is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.SaveItemLocal(_item.Id, BuildLocal(_item), LocalOwners.EditScreen));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                return;
            }

            // ファイルの種類は LocalFiles の中の項目なので、上の保存とは別の命令で書く（理由は BuildFileLinks の上）
            var changedFiles = ChangedFileVariations();
            if (changedFiles.Count > 0
                && await _services.Commands.ExecuteAsync(new UiCommand.SetFileVariations(_item.Id, changedFiles))
                    is CommandResult.Failed fileFailed)
            {
                StatusText = fileFailed.Message;
                return;
            }

            RememberShopName(BuildShop()?.Name);

            // **読めなかった欄は止めずに言う**（ユーザ判断 2026-09-20・I3）。
            // 黙って空にすると、打った本人は保存できたと思ったまま、支出の統計から静かに落ちる
            StatusText = UnreadableNotice();

            // ナビの「未:」をその場で減らす（ユーザ指示 2026-09-12）。検索画面の写しの1件を差し替えると数え直しが走る。
            // 帯には緑の印で残し、編集画面を離れて入り直すまでは戻れる（入り直したら出さない・ResumeAsync）
            if (await _services.Store.Items.LoadAsync(_item.Id) is { } savedRecord)
            {
                _main.Search.NoteItemChanged(savedRecord);
            }

            // 上の帯で、保存した物と飛ばした物を見分けるための印。未編集の順番はファイルに残し、
            // 指定して入った順番は履歴に預けた控え（_saved がそのまま控えの集合）に残る
            _saved.Add(_item.Id);
            if (_run is null)
            {
                await _services.Commands.ExecuteAsync(new UiCommand.NoteEditSaved(_item.Id));
            }

            // 保存したので書きかけではない。次へ進むときに控え直さないよう、消してから進む
            _main.Drafts.Remove(_item.Id);
            _baseline = null;

            await AdvanceAsync();
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// 保存した店名を候補に足す。候補は画面を開いたときに1回だけ作るので、
    /// 足さないと、いま入れたばかりの店が次の商品で候補に出ない。
    /// </summary>
    private void RememberShopName(string? name)
    {
        if (name is not { Length: > 0 } || _shopNames.Contains(name, StringComparer.CurrentCultureIgnoreCase))
        {
            return;
        }

        _shopNames.Add(name);
        _shopNames.Sort(StringComparer.CurrentCulture);
    }

    private async Task AdvanceAsync()
    {
        RememberStep();
        _index++;
        await SavePositionAsync();
        await LoadCurrentAsync();
    }

    /// <summary>
    /// スキップ。保存はしないが、**書きかけは残す**（ユーザ判断）。「編集途中 n件」から戻れる。
    /// 以前は入力を捨てて次へ進んでいた。
    /// </summary>
    private async Task SkipAsync()
    {
        CaptureDraft();
        await AdvanceAsync();
    }

    private void GoBack()
    {
        if (_index == 0)
        {
            return;
        }

        RememberStep();
        StopReturnTimer();
        CaptureDraft();
        _index--;
        SavePositionAsync().Forget();
        LoadCurrentAsync().Forget();
    }

    /// <summary>
    /// キューを終えたら、少し置いてから検索へ戻す。
    /// 終わったことを読む間は要るので即座には動かさない。設定で切れる。
    /// </summary>
    private void StartReturnTimer()
    {
        if (!_services.Settings.ReturnToSearchWhenEditDone)
        {
            return;
        }

        RemainingSeconds = Math.Max(1, _services.Settings.ReturnToSearchDelaySeconds);
        _returnTimer.Start();
    }

    private void StopReturnTimer()
    {
        _returnTimer.Stop();
        RemainingSeconds = 0;
    }

    private void OnReturnTick(object? sender, EventArgs e)
    {
        RemainingSeconds--;
        if (RemainingSeconds > 0)
        {
            return;
        }

        StopReturnTimer();

        // 待っている間に他の画面へ移っていたら、そこから引きはがさない
        if (ReferenceEquals(_main.CurrentViewModel, this))
        {
            FinishAsync().Forget();
        }
    }

    /// <summary>
    /// 編集を終える。未編集の順番（ナビから入った）は検索へ戻る。
    /// 商品を指定して入った編集は、入る前の画面へ戻る（動線の点検 D4：商品ページから1件直しに入っても、検索へ飛ばされていた）
    /// </summary>
    private async Task FinishAsync()
    {
        StopReturnTimer();

        // 捨てるのは未編集の順番の記録だけ。指定して入った順番は履歴に預けてあり、ファイルには無い
        if (_run is null)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.ClearEditSession());
        }

        await _main.ReloadLibraryAsync();

        if (_run is null)
        {
            _main.ShowSearch();
        }
        else
        {
            _main.LeaveEdit();
        }
    }

    /// <summary>編集しながら実物のページを見たいことがあるので、ここからも飛べるようにする。</summary>
    private void OpenBooth()
    {
        if (_item is null)
        {
            return;
        }

        if (BoothClient.PageUrlFor(_item) is not { } url)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくても編集は続けられる
        }
    }

    private void RaiseItemChanged()
    {
        // 進む・戻る・飛ぶ・保存のたびにここを通るので、上の帯もここで作り直す
        RebuildQueueTiles();

        OnPropertyChanged(nameof(PurchasesCountText));
        OnPropertyChanged(nameof(IsPurchaseUnselected));
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ShopName));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(BoothName));
        OnPropertyChanged(nameof(HasBoothName));
        OnPropertyChanged(nameof(IsLocalOnly));
        OnPropertyChanged(nameof(OpenBoothTip));
        OnPropertyChanged(nameof(ShopKeyNote));
        OnPropertyChanged(nameof(BoothCategory));
        OnPropertyChanged(nameof(HasBoothCategory));

        // 空欄のときに欄が名乗る既定。商品が変わると中身も変わる
        OnPropertyChanged(nameof(NamePlaceholder));
        OnPropertyChanged(nameof(ShopPlaceholder));
        OnPropertyChanged(nameof(CategoryPlaceholder));
        OnPropertyChanged(nameof(DescriptionPreview));
        OnPropertyChanged(nameof(BoothTags));
        OnPropertyChanged(nameof(BoothTagsCountText));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
