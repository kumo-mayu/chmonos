using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>購入記録の入力行。</summary>
public sealed class OrderedVariationInput : ViewModelBase
{
    private bool _isPurchased;
    private string _price = string.Empty;
    private PurchaseKind _kind = PurchaseKind.ForSelf;

    /// <summary>
    /// どのバリエーションの行か。**null は「どのバリエーションも指していない」購入の行。**
    /// BOOTHから取れない商品にはバリエーションが1件も無く、
    /// バリエーション単位の販売終了でも後から記録を入れる行が無くなる。
    /// </summary>
    public long? VariationId { get; init; }

    public required string Name { get; init; }

    /// <summary>BOOTHの現在価格。未入力のときの目安として出す。</summary>
    public required string ListPriceText { get; init; }

    public int? ListPrice { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }

    /// <summary>
    /// 保存済みの値を流し込み終えたか。
    /// これが立つまで価格の自動入力はしない。読み込んだだけで
    /// 「未入力」だった過去の記録に勝手な金額が入るのを避けるため。
    /// </summary>
    public bool IsInitialized { get; set; }

    public bool IsPurchased
    {
        get => _isPurchased;
        set
        {
            if (!SetField(ref _isPurchased, value))
            {
                return;
            }

            // ユーザが印を付けた時点で、BOOTHの現在価格を初期値として入れておく。
            // 空欄のままだと「未入力」で保存され、統計の支出に乗らない。
            // セール等で実際の支払額が違うことはあるので、値は書き換えられるようにしておく。
            if (IsInitialized && value && _price.Length == 0 && ListPrice is { } listPrice)
            {
                Price = listPrice.ToString();
            }

            NotePurchasedChanged();
        }
    }

    /// <summary>購入価格。空欄は未入力（支出に数えない）、0は無料配布。</summary>
    public string Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    /// <summary>
    /// この購入が誰のためのものだったか。
    /// 貰い物は支出に数えず、贈答は支出には入るが所持には入らない。
    /// </summary>
    public PurchaseKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindLabel));
            }
        }
    }

    public string KindLabel => DisplayText.PurchaseKindLabel(Kind);

    /// <summary>
    /// 同じ版の2件目以降の購入記録。
    ///
    /// **買った1回が1レコード**なので、同じ版を2回買った記録も持てる。
    /// 「自分用に1つ、ギフトに1つ」がこれにあたり、
    /// <see cref="PurchaseKind"/> を3種に割ったのはこの用途のため。
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ExtraPurchaseInput> Extras { get; } = [];

    public bool HasExtras => Extras.Count > 0;

    /// <summary>この版をもう1回買った記録を足す。</summary>
    public RelayCommand? AddPurchaseCommand { get; set; }

    /// <summary>足せるのは購入に印を付けた版だけ。1件目が無いのに2件目は作れない。</summary>
    public bool CanAddPurchase => IsPurchased;

    internal void NotePurchasedChanged()
    {
        OnPropertyChanged(nameof(CanAddPurchase));
        RelayCommand.RaiseCanExecuteChanged();
    }

    internal void NoteExtrasChanged() => OnPropertyChanged(nameof(HasExtras));

    // ---- この種類のファイル（#40） ----

    private bool _canLinkFiles;
    private FileLinkInput? _selectedFileChoice;

    /// <summary>
    /// ファイルを紐付けられる行か。種類が2つ以上ある商品で、手元にファイルがあるときだけ。
    /// 1種類しかない商品では、どのファイルもその種類なので選ぶ意味が無い。
    /// </summary>
    public bool CanLinkFiles
    {
        get => _canLinkFiles;
        set => SetField(ref _canLinkFiles, value);
    }

    /// <summary>この種類に紐付けたファイル。同じ種類に複数付けられる（別zipでも同じ種類由来のことがある）。</summary>
    public ObservableCollection<FileLinkInput> LinkedFiles { get; } = [];

    public bool HasLinkedFiles => LinkedFiles.Count > 0;

    /// <summary>プルダウンに出す、まだこの種類に付いていないファイル。名前が似ているものを先に並べる。</summary>
    public ObservableCollection<FileLinkInput> FileChoices { get; } = [];

    /// <summary>プルダウンで選ばれたら紐付ける。選んだファイルは一覧から抜けるので、選択は自然に空へ戻る。</summary>
    public Action<FileLinkInput>? LinkRequested { get; set; }

    public FileLinkInput? SelectedFileChoice
    {
        get => _selectedFileChoice;
        set
        {
            _selectedFileChoice = value;
            if (value is not null)
            {
                LinkRequested?.Invoke(value);
            }

            OnPropertyChanged();
        }
    }

    internal void NoteLinkedFilesChanged() => OnPropertyChanged(nameof(HasLinkedFiles));
}

/// <summary>種類に紐付ける／紐付いたファイル1件。</summary>
public sealed class FileLinkInput
{
    public required string Hash { get; init; }

    public required string Name { get; init; }

    /// <summary>「『〇〇』に付いています」「名前が似ています」。無ければ空。</summary>
    public string Note { get; init; } = string.Empty;

    public string Display => Note.Length == 0 ? Name : $"{Name}（{Note}）";

    public RelayCommand? UnlinkCommand { get; set; }
}

/// <summary>
/// 同じ版の2件目以降の購入記録1件。
///
/// 1件目（版の行そのもの）と同じ項目を持つが、印を外す代わりに行ごと消す。
/// 「買った回数」を減らす操作なので、外すより消す方が意味に合う。
/// </summary>
public sealed class ExtraPurchaseInput : ViewModelBase
{
    private string _price = string.Empty;
    private PurchaseKind _kind = PurchaseKind.Given;

    /// <summary>BOOTH側に現存しない版の記録か。1件目から引き継ぐ。</summary>
    public bool IsGone { get; init; }

    public string? NameSnapshot { get; init; }

    public string? Note { get; init; }

    public string Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    /// <summary>
    /// 既定を「贈った」にしている。2件目を作る理由のほとんどが贈答だから
    /// （自分用を2つ買う場面はまれ）。違えばその場で変えられる。
    /// </summary>
    public PurchaseKind Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    /// <summary>この記録を消す。</summary>
    public RelayCommand? RemoveCommand { get; set; }
}

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
public sealed class EditViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
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
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);

        SaveAndNextCommand = new RelayCommand(() => _ = SaveAndAdvanceAsync(), () => HasItem && !IsSaving);
        SkipCommand = new RelayCommand(() => _ = AdvanceAsync(), () => HasItem && !IsSaving);
        BackCommand = new RelayCommand(GoBack, () => _index > 0);
        FinishCommand = new RelayCommand(() => _ = FinishAsync());
        // 仮IDの商品にはBOOTHページが無い。押せると404へ送ることになる
        OpenBoothCommand = new RelayCommand(OpenBooth, () => HasItem && !IsLocalOnly);
        AddTagCommand = new RelayCommand(parameter => _ = AddTagAsync(parameter as string));
        AddAttributeCommand = new RelayCommand(parameter => _ = AddAttributeAsync(parameter as string));
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

    public ObservableCollection<GalleryImage> Images { get; } = [];

    /// <summary>userTagトップの候補。既に付けたものは出さない。</summary>
    public ObservableCollection<string> TagSuggestions { get; } = [];

    /// <summary>属性の候補。既に評価したものは出さない。</summary>
    public ObservableCollection<string> AttributeSuggestions { get; } = [];

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

    private int _selectedImageIndex;

    /// <summary>
    /// 今メインに出している画像。属性を付けるには複数枚見たいので切り替えられる。
    /// 一覧の方は小さく縮めたものなので、メインは保存された大きさで読み直す（キャッシュに乗る）。
    /// </summary>
    public BitmapSource? MainImage => Images.Count == 0
        ? null
        : _thumbnails.Load(Images[Math.Clamp(_selectedImageIndex, 0, Images.Count - 1)].Path);

    public RelayCommand SelectImageCommand { get; }

    private void SelectImage(object? parameter)
    {
        if (parameter is not GalleryImage image)
        {
            return;
        }

        var index = Images.IndexOf(image);
        if (index < 0)
        {
            return;
        }

        _selectedImageIndex = index;

        foreach (var entry in Images)
        {
            entry.IsSelected = ReferenceEquals(entry, image);
        }

        OnPropertyChanged(nameof(MainImage));
    }

    public string DescriptionPreview => _item?.Booth.Description ?? string.Empty;

    public IReadOnlyList<string> BoothTags => _item?.Booth.Tags ?? [];

    /// <summary>自動で検索へ戻るまでのカウントダウン。0なら出さない。</summary>
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

    public string ReturnNoticeText => $"{RemainingSeconds} 秒後に検索へ戻ります。";

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
        : "BOOTHの商品ページをブラウザで開きます。";

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
        _queue = itemIds?.ToList() ?? await BuildDefaultQueueAsync();
        _index = 0;
        _saved = new HashSet<string>(StringComparer.Ordinal);
        await _services.Edit.StartSessionAsync(_queue);
        await LoadCurrentAsync();
    }

    /// <summary>前回の続きを開く。残っていなければ新しく積み直す。</summary>
    public async Task ResumeAsync()
    {
        var session = _services.Store.EditSession.Load();
        if (session.ItemIds.Count == 0 || session.IsFinished)
        {
            await StartAsync();
            return;
        }

        _queue = session.ItemIds.ToList();
        _index = Math.Clamp(session.Index, 0, _queue.Count);
        _saved = new HashSet<string>(session.SavedItemIds, StringComparer.Ordinal);
        await LoadCurrentAsync();
    }

    // ---- 上の帯：どんな商品が続くか（ユーザ指示 2026-09-12） ----
    //
    // 「n / N 件」とバーだけでは何が続くのか分からないので、右の空きに続く商品を小さな絵で並べる。
    // 済んだ物は5件まで、これからの物は幅に収まるだけ。保存した物には印を付け、押すとその商品へ飛ぶ

    /// <summary>帯に出す済んだ物の数（ユーザ指示）。</summary>
    private const int PastTileCount = 5;

    /// <summary>
    /// 帯に作るこれからの物の数。見えるのは幅に収まる分だけで、はみ出た分は折り返して枠の外に隠れる。
    /// 広い画面（2560px）でも埋まる数にしてある。絵は描いた分しか読まない。
    /// </summary>
    private const int UpcomingTileCount = 60;

    /// <summary>この回で保存した商品。飛ばした物と見分けるため。</summary>
    private HashSet<string> _saved = new(StringComparer.Ordinal);

    public ObservableCollection<EditQueueTile> QueueTiles { get; } = [];

    private RelayCommand? _jumpCommand;

    /// <summary>帯の絵を押すと、その商品へ飛ぶ。いま開いている商品の入力は保存しない（スキップと同じ）。</summary>
    public RelayCommand JumpCommand => _jumpCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is EditQueueTile tile)
            {
                _ = JumpAsync(tile.Index);
            }
        },
        parameter => parameter is EditQueueTile && !IsSaving);

    private async Task JumpAsync(int index)
    {
        if (index == _index || index < 0 || index >= _queue.Count)
        {
            return;
        }

        StopReturnTimer();
        _index = index;
        await _services.Edit.AdvanceSessionAsync(_index);
        await LoadCurrentAsync();
    }

    private void RebuildQueueTiles()
    {
        QueueTiles.Clear();
        if (_queue.Count == 0)
        {
            return;
        }

        var from = Math.Max(0, _index - PastTileCount);
        var to = Math.Min(_queue.Count, _index + 1 + UpcomingTileCount);

        for (var index = from; index < to; index++)
        {
            var itemId = _queue[index];

            // 検索が読んである写しから引く。1件進むたびに数十件のJSONを読み直さない（#71 と同じ理由）
            var record = _main.Search.FindItem(itemId);

            QueueTiles.Add(new EditQueueTile
            {
                Index = index,
                Name = record?.DisplayName ?? itemId,
                IsCurrent = index == _index,
                IsPast = index < _index,
                IsSaved = _saved.Contains(itemId),
                ImageFactory = record is null ? null : onLoaded => TileImage(record, onLoaded),
            });
        }
    }

    /// <summary>帯の絵。検索のカードと同じ1枚（指名・役割の設定を見る）にそろえる。</summary>
    private BitmapSource? TileImage(ItemRecord record, Action onLoaded)
    {
        var directory = _services.Paths.ItemImagesDir(record.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, record.Booth.Images, _thumbnails.ListFiles(directory), record.Local.UserImages);
        var path = Core.Images.ItemImageOrder.Thumbnail(
            ordered, record.Local.ThumbnailImage, _services.Settings.ThumbnailRole, record.Local.ImageRoles);

        return path is null ? null : _thumbnails.PeekForTile(path, onLoaded);
    }

    private async Task<List<string>> BuildDefaultQueueAsync()
    {
        var loaded = await _services.Store.Items.LoadAllAsync();
        return loaded.Items
            .Where(item => item.Local.UserTags.Count == 0)
            // 取り込みの③がまだの商品は積まない（U8・U10）。③が済めば次に開いたときに入る
            .Where(item => !_main.IsAwaitingDetection(item.Id))
            .OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
            .Select(item => item.Id)
            .ToList();
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

                // BOOTHから名前が取れていない商品は、ここを埋めないと名前が無い。
                // 閉じたままだと入れる場所が見えないので、その商品だけ開いて出す
                IsEditingBasics = record.Booth.Name is not { Length: > 0 };
                RaiseItemChanged();
                return;
            }

            _index++;
        }

        _item = null;
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
        BuildImages(record);
        RefreshSuggestions();
    }

    private UserTagRow CreateTagRow(string top)
    {
        var row = new UserTagRow { Top = top };

        row.RemoveCommand = new RelayCommand(() =>
        {
            Tags.Remove(row);
            RefreshSuggestions();
        });

        row.AddSubCommand = new RelayCommand(parameter => _ = AddSubAsync(row, parameter as string));

        row.RemoveSubCommand = new RelayCommand(parameter =>
        {
            if (parameter is string sub)
            {
                row.Subs.Remove(sub);
                row.Raise();
                RefreshSubCandidates(row);
            }
        });

        return row;
    }

    private AttributeRow CreateAttributeRow(string name, int value)
    {
        var row = new AttributeRow { Name = name, Value = value };
        row.RemoveCommand = new RelayCommand(() =>
        {
            Attributes.Remove(row);
            RefreshSuggestions();
        });

        return row;
    }

    /// <summary>まだ使っていない候補だけを出す。既に付けたものを候補に残すと選び間違える。</summary>
    /// <summary>
    /// 入力からショップを組み立てる。
    ///
    /// **URLを貼れば本物のサブドメイン、貼らなければ手元だけの鍵。**
    /// ローマ字化はしない——「ほとぎ屋」→ hotogiya は実在するので、
    /// 手で作った鍵が本物と衝突すると本物のアイコンとバナーが出てしまう。
    /// </summary>
    /// <summary>手元の商品が持っているショップ名を集める。</summary>
    /// <summary>
    /// 打った分で絞り込む。139件あるので、打つほど絞れる形にしないと選べない。
    /// </summary>
    private void RefreshCategorySuggestions()
    {
        var typed = CategoryInput.Trim();

        var matched = _services.Categories.Suggestions()
            .Where(name => typed.Length == 0
                || (name.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                    && !string.Equals(name, typed, StringComparison.CurrentCultureIgnoreCase)))
            .Take(CategorySuggestionLimit)
            .ToList();

        CategorySuggestions.Clear();
        foreach (var name in matched)
        {
            CategorySuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasCategorySuggestions));
    }

    /// <summary>
    /// 打った分で絞り込む。14店あると全部並べても読めないので、
    /// 打つほど絞れる形にする。空欄のときは頭から数件だけ出す。
    /// </summary>
    private void RefreshShopSuggestions()
    {
        var typed = ShopNameInput.Trim();

        var matched = _shopNames
            .Where(name => typed.Length == 0
                || (name.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                    && !string.Equals(name, typed, StringComparison.CurrentCultureIgnoreCase)))
            .Take(ShopSuggestionLimit)
            .ToList();

        ShopSuggestions.Clear();
        foreach (var name in matched)
        {
            ShopSuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasShopSuggestions));
    }

    private async Task<List<string>> LoadShopNamesAsync()
        => (await _services.Store.Items.LoadAllAsync()).Items
            .Select(item => item.ShopName)
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

    private LocalShop? BuildShop()
    {
        var name = ShopNameInput.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var url = ShopUrlInput.Trim();
        var subdomain = LocalShopKey.SubdomainFromUrl(url);

        return new LocalShop
        {
            Name = name,
            Subdomain = subdomain ?? LocalShopKey.For(name),
            Url = subdomain is null ? null : url,
        };
    }

    private void RefreshSuggestions()
    {
        TagSuggestions.Clear();
        foreach (var top in _tagMaster.Tops
            .Select(top => top.Name)
            .Where(name => !Tags.Any(row => string.Equals(row.Top, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            TagSuggestions.Add(top);
        }

        RefreshShopSuggestions();

        RefreshCategorySuggestions();

        AttributeSuggestions.Clear();
        foreach (var name in _attributeMaster.Attributes
            .Select(definition => definition.Name)
            .Where(name => !Attributes.Any(row => string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            AttributeSuggestions.Add(name);
        }
    }

    private void RefreshSubCandidates(UserTagRow row)
    {
        var master = _tagMaster.Tops.FirstOrDefault(top =>
            string.Equals(top.Name, row.Top, StringComparison.CurrentCultureIgnoreCase));

        row.SubCandidates.Clear();
        foreach (var sub in (master?.Subs ?? [])
            .Select(sub => sub.Name)
            .Where(name => !row.Subs.Contains(name, StringComparer.CurrentCultureIgnoreCase)))
        {
            row.SubCandidates.Add(sub);
        }
    }

    private async Task AddTagAsync(string? name)
    {
        var top = name?.Trim();
        if (string.IsNullOrEmpty(top)
            || Tags.Any(row => string.Equals(row.Top, top, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        // 候補に無い語はマスタへの新規追加を兼ねる
        if (!_tagMaster.Tops.Any(entry => string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(top)) is CommandResult.UserTagsChanged changed)
            {
                _tagMaster = changed.Master;
            }
        }

        var row = CreateTagRow(top);
        RefreshSubCandidates(row);
        Tags.Add(row);
        RefreshSuggestions();
    }

    private async Task AddSubAsync(UserTagRow row, string? name)
    {
        var sub = name?.Trim();
        if (string.IsNullOrEmpty(sub) || row.Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase))
        {
            return;
        }

        var master = _tagMaster.Tops.FirstOrDefault(top =>
            string.Equals(top.Name, row.Top, StringComparison.CurrentCultureIgnoreCase));

        if (master is null
            || !master.Subs.Any(entry => string.Equals(entry.Name, sub, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(row.Top, sub)) is CommandResult.UserTagsChanged changed)
            {
                _tagMaster = changed.Master;
            }
        }

        row.Subs.Add(sub);
        row.Raise();
        RefreshSubCandidates(row);
    }

    private async Task AddAttributeAsync(string? name)
    {
        var attribute = name?.Trim();
        if (string.IsNullOrEmpty(attribute)
            || Attributes.Any(row => string.Equals(row.Name, attribute, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        if (!_attributeMaster.Attributes.Any(entry =>
            string.Equals(entry.Name, attribute, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(attribute)) is CommandResult.AttributesChanged changed)
            {
                _attributeMaster = changed.Master;
            }
        }

        Attributes.Add(CreateAttributeRow(attribute, 50));
        RefreshSuggestions();
    }

    private void BuildVariations(ItemRecord record)
    {
        Variations.Clear();

        // 同じ版を複数回買った記録がありうるので、版ごとにまとめる。
        // 1件目は版の行そのもの、2件目以降は行の下にぶら下げる
        // ToLookup は null の鍵を持てる（ToDictionary は持てない）。
        // 「どのバリエーションも指していない」記録がここに入る
        var ordered = record.Local.Purchases.ToLookup(purchase => purchase.VariationId);

        foreach (var variation in record.Booth.Variations)
        {
            var group = ordered[variation.Id].ToList();
            var first = group.FirstOrDefault();

            var row = new OrderedVariationInput
            {
                VariationId = variation.Id,
                Name = variation.Name ?? "（1種類のみ）",
                ListPrice = variation.Price,
                ListPriceText = $"¥{variation.Price:N0}",
                IsPurchased = first is not null,
                Price = first?.Price?.ToString() ?? string.Empty,
                Kind = first?.Kind ?? PurchaseKind.ForSelf,
            };

            AttachExtras(row, group.Skip(1));
            Variations.Add(row);
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す。
        // バリエーションを指していない記録（null）もここへ落ちる——
        // 指す先が無いので「現存する」側には入らない
        var currentIds = record.Booth.Variations.Select(variation => (long?)variation.Id).ToHashSet();
        foreach (var group in ordered.Where(entry => !currentIds.Contains(entry.Key)))
        {
            var purchases = group.ToList();
            var first = purchases[0];
            var row = new OrderedVariationInput
            {
                VariationId = group.Key,
                Name = first.NameSnapshot ?? DisplayText.VariationLabel(group.Key),
                ListPriceText = "-",

                // 指していない記録は「消えた」わけではない。
                // 指す先が無いだけなので、現存しない印は付けない
                IsGone = group.Key is not null,
                IsPurchased = true,
                Price = first.Price?.ToString() ?? string.Empty,
                Kind = first.Kind,
            };

            AttachExtras(row, purchases.Skip(1));
            Variations.Add(row);
        }

        // どのバリエーションも指さない購入を、いつでも足せるようにする。
        //
        // BOOTHから取れない商品にはバリエーションが1件も無いので、これが無いと
        // **買った金額を記録する場所が存在しない**（統計の支出から丸ごと落ちる）。
        // 普通の商品にも出すのは、**バリエーション単位の販売終了があるため**——
        // 買ったあとにその版が消えると、後から記録を入れる行が無くなる。
        if (Variations.All(row => row.VariationId is not null))
        {
            Variations.Add(new OrderedVariationInput
            {
                VariationId = null,
                Name = DisplayText.VariationLabel(null),
                ListPriceText = "-",
                IsPurchased = false,
            });
        }

        // ここから先の変更はユーザ操作。価格の自動入力を許可する
        foreach (var input in Variations)
        {
            input.IsInitialized = true;
        }
    }

    // ---- ファイルに種類を付ける（#40） ----
    //
    // 付ける操作がどこにも無く、友人のデータで327件中0件だった。
    // 他の入力と同じく「保存して次へ」で書く（スキップすれば捨てる）。
    // 書くのは編集画面の保存とは別の命令——種類は LocalFiles の中の項目で、
    // 編集画面が LocalFiles を丸ごと持つと、開いている間に取り込みが足したファイルを消してしまう。

    /// <summary>手元のファイル（ハッシュ→表示名）。場所の分からないものは出さない。</summary>
    private List<(string Hash, string Name)> _files = [];

    /// <summary>今の画面上の紐付け。</summary>
    private Dictionary<string, long?> _fileVariations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>開いた時点の紐付け。保存のときに差だけを書く。</summary>
    private Dictionary<string, long?> _savedFileVariations = new(StringComparer.OrdinalIgnoreCase);

    private void BuildFileLinks(ItemRecord record)
    {
        _files = record.Local.LocalFiles
            .Where(file => file.Paths.Count > 0)
            .Select(file => (file.Hash, System.IO.Path.GetFileName(file.Paths[0])))
            .ToList();

        _fileVariations = record.Local.LocalFiles.ToDictionary(
            file => file.Hash, file => file.VariationId, StringComparer.OrdinalIgnoreCase);
        _savedFileVariations = new Dictionary<string, long?>(_fileVariations, StringComparer.OrdinalIgnoreCase);

        var canLink = record.Booth.Variations.Count >= 2 && _files.Count > 0;

        foreach (var row in Variations)
        {
            // 「種類を選ばない購入」の行には付けない。付け先の種類が無い
            row.CanLinkFiles = canLink && row.VariationId is not null;
            row.LinkRequested = choice =>
            {
                _fileVariations[choice.Hash] = row.VariationId;
                RefreshFileLinks();
            };
        }

        RefreshFileLinks();
    }

    private void RefreshFileLinks()
    {
        var names = Variations
            .Where(row => row.VariationId is not null)
            .ToDictionary(row => row.VariationId!.Value, row => row.Name);

        foreach (var row in Variations.Where(row => row.CanLinkFiles))
        {
            row.LinkedFiles.Clear();
            row.FileChoices.Clear();

            foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
            {
                var current = _fileVariations.GetValueOrDefault(hash);
                if (current == row.VariationId)
                {
                    row.LinkedFiles.Add(new FileLinkInput
                    {
                        Hash = hash,
                        Name = name,
                        UnlinkCommand = new RelayCommand(() =>
                        {
                            _fileVariations[hash] = null;
                            RefreshFileLinks();
                        }),
                    });
                }
            }

            // 名前に種類名がそのまま入っているものを先に出す。友人のデータで当たるのは14%だけなので、
            // 自動では付けずに候補の順番にだけ使う
            var choices = _files
                .Where(file => _fileVariations.GetValueOrDefault(file.Hash) != row.VariationId)
                .Select(file =>
                {
                    var other = _fileVariations.GetValueOrDefault(file.Hash);
                    var looksLike = NameLooksLike(file.Name, row.Name);
                    return (File: file, LooksLike: looksLike, Note: other is { } otherId && names.TryGetValue(otherId, out var otherName)
                        ? $"「{otherName}」に付いています"
                        : looksLike ? "名前が似ています" : string.Empty);
                })
                .OrderByDescending(entry => entry.LooksLike)
                .ThenBy(entry => entry.File.Name, StringComparer.CurrentCulture);

            foreach (var entry in choices)
            {
                row.FileChoices.Add(new FileLinkInput { Hash = entry.File.Hash, Name = entry.File.Name, Note = entry.Note });
            }

            row.NoteLinkedFilesChanged();
        }
    }

    /// <summary>ファイル名に種類名が丸ごと入っているか。空白・括弧・区切りは無視する。</summary>
    private static bool NameLooksLike(string fileName, string variationName)
    {
        static string Fold(string text) => new(text
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && !"_-.()[]【】（）「」『』・/\\".Contains(character))
            .ToArray());

        var file = Fold(System.IO.Path.GetFileNameWithoutExtension(fileName));
        var variation = Fold(variationName);
        return variation.Length >= 2 && file.Contains(variation, StringComparison.Ordinal);
    }

    /// <summary>開いた時点から変わった紐付けだけ。</summary>
    private Dictionary<string, long?> ChangedFileVariations()
        => _fileVariations
            .Where(pair => _savedFileVariations.GetValueOrDefault(pair.Key) != pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 2件目以降の購入記録を行にぶら下げ、足す／消すを配線する。
    /// 版の行と同じ形にしておくと、1件目と2件目で操作が変わらない。
    /// </summary>
    private void AttachExtras(OrderedVariationInput row, IEnumerable<Purchase>? existing)
    {
        foreach (var purchase in existing ?? [])
        {
            AddExtra(row, purchase.Price?.ToString(), purchase.Kind, purchase.NameSnapshot, purchase.Note);
        }

        row.AddPurchaseCommand = new RelayCommand(
            // 版の名前は引き継ぐ。BOOTH側から消えたときに何の版だったか分からなくなる
            () => AddExtra(row, row.Price, PurchaseKind.Given, row.Name, null),
            () => row.CanAddPurchase);

        row.NoteExtrasChanged();
    }


    private void AddExtra(
        OrderedVariationInput row,
        string? price,
        PurchaseKind kind,
        string? nameSnapshot,
        string? note)
    {
        var extra = new ExtraPurchaseInput
        {
            IsGone = row.IsGone,
            NameSnapshot = nameSnapshot,
            Note = note,
            Price = price ?? string.Empty,
            Kind = kind,
        };

        extra.RemoveCommand = new RelayCommand(() =>
        {
            row.Extras.Remove(extra);
            row.NoteExtrasChanged();
        });

        row.Extras.Add(extra);
        row.NoteExtrasChanged();
    }

    private void BuildImages(ItemRecord record)
    {
        Images.Clear();
        _selectedImageIndex = 0;
        var directory = _services.Paths.ItemImagesDir(record.Id);

        foreach (var entry in Core.Images.ItemImageOrder.Arrange(
            directory, record.Booth.Images, _thumbnails.ListFiles(directory), record.Local.UserImages))
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
                    fileName, record.Local.ThumbnailImage, StringComparison.OrdinalIgnoreCase),
            });
        }
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
                    Price = int.TryParse(variation.Price.Trim(), out var price) ? price : null,
                    Kind = variation.Kind,
                    ExistsOnBooth = !variation.IsGone,
                },
            }.Concat(variation.Extras.Select(extra => new Purchase
            {
                VariationId = variation.VariationId,
                NameSnapshot = extra.NameSnapshot ?? variation.Name,
                Price = int.TryParse(extra.Price.Trim(), out var extraPrice) ? extraPrice : null,
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
            AcquiredAt = DateOnly.TryParse(AcquiredAt.Trim(), out var date) ? date : null,
            NotifyOnUpdate = NotifyOnUpdate,
            IsHidden = IsHidden,
        };
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
            StatusText = string.Empty;

            // 上の帯で、保存した物と飛ばした物を見分けるための印。開き直しても残す
            _saved.Add(_item.Id);
            await _services.Edit.NoteSavedAsync(_item.Id);

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
        _index++;
        await _services.Edit.AdvanceSessionAsync(_index);
        await LoadCurrentAsync();
    }

    private void GoBack()
    {
        if (_index == 0)
        {
            return;
        }

        StopReturnTimer();
        _index--;
        _ = _services.Edit.AdvanceSessionAsync(_index);
        _ = LoadCurrentAsync();
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
            _ = FinishAsync();
        }
    }

    /// <summary>編集を終える。キューを捨てて検索へ戻る。</summary>
    private async Task FinishAsync()
    {
        StopReturnTimer();
        await _services.Edit.ClearSessionAsync();
        await _main.ReloadLibraryAsync();
        _main.ShowSearch();
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
        OnPropertyChanged(nameof(MainImage));
        OnPropertyChanged(nameof(DescriptionPreview));
        OnPropertyChanged(nameof(BoothTags));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
