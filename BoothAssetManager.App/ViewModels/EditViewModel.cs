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

    /// <summary>買った印が変わった。ファイルの種類分け（買った種類が1つなら全部その種類として見せる）を作り直させる。</summary>
    public Action? PurchasedChanged { get; set; }

    internal void NotePurchasedChanged()
    {
        OnPropertyChanged(nameof(CanAddPurchase));
        RelayCommand.RaiseCanExecuteChanged();
        PurchasedChanged?.Invoke();
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

    /// <summary>
    /// 種類の行の右端に出すファイルの数（案A・ユーザ判断）。結び付けそのものは「ファイルの種類分け」だけで行い、
    /// 種類の行には数だけ出す（行に選び欄や札を並べると、印を付けるたびに形が変わって分かりにくかった）。
    /// </summary>
    /// 「ファイル n」では何の数か分からなかった（ユーザ指摘）ので、紐付けた数だと読める言い方にする。
    public string FileCountText => LinkedFiles.Count > 0 ? $"{LinkedFiles.Count} ファイル紐付け済" : string.Empty;

    internal void NoteLinkedFilesChanged()
    {
        OnPropertyChanged(nameof(HasLinkedFiles));
        OnPropertyChanged(nameof(FileCountText));
    }
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

    /// <summary>✕を出すか。買った種類が1つで全部その種類として見せている札は、外す意味が無いので出さない。</summary>
    public bool CanUnlink => UnlinkCommand is not null;
}

/// <summary>ファイルの種類分けの選択肢1つ。null は「指定しない」。</summary>
public sealed record VariationChoice(long? VariationId, string Name);

/// <summary>
/// 「ファイルの種類分け」の1行（ユーザ指示 2026-09-12）。ファイルを主にして、どの種類のファイルかを選ぶ。
/// 「購入した種類」の各行の選び欄（種類を主にして、付けるファイルを選ぶ）と同じ中身を直すので、どちらで選んでも揃う。
/// </summary>
public sealed class FileSortRow : ViewModelBase
{
    private VariationChoice _selected;

    public FileSortRow(VariationChoice selected) => _selected = selected;

    public required string Hash { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<VariationChoice> Choices { get; init; }

    /// <summary>買った種類が1つなので、その種類として見せているだけ（書かない）。選び欄は触れない。</summary>
    public bool IsAuto { get; init; }

    /// <summary>まだどの種類にも付いていない。印を出して、残っているファイルが一目で分かるようにする。</summary>
    public bool IsUnassigned => !IsAuto && _selected.VariationId is null;

    /// <summary>選び直された。引数は新しい種類（null は指定しない）。</summary>
    public Action<long?>? Changed { get; set; }

    public VariationChoice Selected
    {
        get => _selected;
        set
        {
            if (value is null || value == _selected)
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnassigned));
            Changed?.Invoke(value.VariationId);
        }
    }
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

        SaveAndNextCommand = new RelayCommand(() => _ = SaveAndAdvanceAsync(), () => HasItem && !IsSaving);
        SkipCommand = new RelayCommand(() => _ = SkipAsync(), () => HasItem && !IsSaving);
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

            _ = ReplaceInSessionAsync(previousId, updated.Id);
        }

        _ = LoadCurrentAsync();
    }

    /// <summary>
    /// 未編集の順番（ファイルに残す方）の中のIDを付け替える。指定して入った順番は
    /// <see cref="_queue"/> そのものが履歴に預けた控えなので、上で付け替えた時点で済んでいる。
    /// </summary>
    private async Task ReplaceInSessionAsync(string fromId, string toId)
    {
        if (_run is null)
        {
            await _services.Edit.ReplaceItemIdAsync(fromId, toId);
        }
    }

    public string DescriptionPreview => _item?.Booth.Description ?? string.Empty;

    public IReadOnlyList<string> BoothTags => _item?.Booth.Tags ?? [];

    /// <summary>BOOTHのタグの見出しに添える件数。畳んでいても何件あるかは分かるように。</summary>
    public string BoothTagsCountText => $"（{BoothTags.Count}）";

    private bool _showAllTags;

    /// <summary>並べるタグの札。畳んでいる間は作らず、多い商品は一部だけ（商品ページと同じ・<see cref="ChipLists"/>）。</summary>
    public IReadOnlyList<TagTile> BoothTagTiles => ChipLists.Tags(BoothTags, IsBoothTagsExpanded, _showAllTags);

    private RelayCommand? _showAllTagsCommand;

    /// <summary>「残り n 件を表示」。この商品を開いている間だけ全部並べる。</summary>
    public RelayCommand ShowAllTagsCommand => _showAllTagsCommand ??= new RelayCommand(() =>
    {
        _showAllTags = true;
        OnPropertyChanged(nameof(BoothTagTiles));
    });

    private RelayCommand? _showFewerTagsCommand;

    /// <summary>「最初の 40 件だけにする」。全部並べたのを元に戻す（ユーザ指示：可逆にする）。</summary>
    public RelayCommand ShowFewerTagsCommand => _showFewerTagsCommand ??= new RelayCommand(() =>
    {
        _showAllTags = false;
        OnPropertyChanged(nameof(BoothTagTiles));
    });

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

                // 畳んでいる間は札を作らない。開いたときに作る
                OnPropertyChanged(nameof(BoothTagTiles));
            }
        }
    }

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
        if (itemIds is null)
        {
            _run = null;
            _queue = await BuildDefaultQueueAsync();
            _index = 0;
            _saved = new HashSet<string>(StringComparer.Ordinal);
            await _services.Edit.StartSessionAsync(_queue);
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
            await _services.Edit.StartSessionAsync(kept);
            await _services.Edit.AdvanceSessionAsync(index);
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

        return _services.Edit.AdvanceSessionAsync(_index);
    }

    // ---- 書きかけ（ユーザ判断 2026-09-12） ----
    //
    // 保存せずに商品を離れたとき（スキップ・前へ・帯で飛ぶ・別の画面へ移る・閉じる）の入力を、
    // 変えた項目だけ控える。控えはアプリに1つの置き場（EditDraftStore）にあり、保存したら消える

    /// <summary>
    /// 開いた時点の入力から組んだ local。変えた項目はこれと比べて見分ける。
    /// 記録そのものと比べると、開いただけで形が整う項目（購入の名前の控え・現存の印など）まで
    /// 変えたことになってしまう。
    /// </summary>
    private LocalBlock? _baseline;

    /// <summary>今の商品の入力を書きかけとして控える。変えた項目が無ければ控えを消す（元に戻した＝書きかけではない）。</summary>
    public void CaptureDraft()
    {
        if (_item is null || _baseline is null)
        {
            return;
        }

        var current = BuildLocal(_item);
        var changed = LocalOwners.EditScreen.Where(field => !SameField(_baseline, current, field)).ToList();
        var files = ChangedFileVariations();

        if (changed.Count == 0 && files.Count == 0)
        {
            _main.Drafts.Remove(_item.Id);
            return;
        }

        _main.Drafts.Put(_item.Id, new EditDraft { Local = current, Changed = changed, FileVariations = files });
    }

    /// <summary>その項目だけを比べる。項目の中身（一覧や入れ子）ごと比べたいので、書き出した形で比べる。</summary>
    private static bool SameField(LocalBlock before, LocalBlock after, LocalField field)
        => System.Text.Json.JsonSerializer.Serialize(LocalFields.Merge(new LocalBlock(), before, [field]))
            == System.Text.Json.JsonSerializer.Serialize(LocalFields.Merge(new LocalBlock(), after, [field]));

    /// <summary>「編集途中 n件」のボタン。書きかけの置き場そのもの。</summary>
    public EditDraftStore Drafts => _main.Drafts;

    private RelayCommand? _openDraftsCommand;

    /// <summary>書きかけのある商品だけを並べて開く（ユーザ指示）。今の商品の入力も先に控える。</summary>
    public RelayCommand OpenDraftsCommand => _openDraftsCommand ??= new RelayCommand(
        () =>
        {
            CaptureDraft();
            _ = _main.ShowEditAsync(_main.Drafts.ItemIds);
        },
        () => _main.Drafts.HasAny);

    // ---- 上の帯：どんな商品が続くか（ユーザ指示 2026-09-12） ----
    //
    // 「n / N 件」とバーだけでは何が続くのか分からないので、右の空きに続く商品を小さな絵で並べる。
    // 済んだ物は5件まで、これからの物は幅に収まるだけ。保存した物には印を付け、押すとその商品へ飛ぶ

    /// <summary>開いたときに見せる済んだ物の数（ユーザ指示）。帯には全部並び、ホイールで遡れる。</summary>
    private const int PastTileCount = 5;

    /// <summary>この回で保存した商品。飛ばした物と見分けるため。</summary>
    private HashSet<string> _saved = new(StringComparer.Ordinal);

    /// <summary>
    /// 帯の絵。1件進むたびに差し替える（1枚ずつ足し引きすると、2000件の順番で2000回の知らせが飛ぶ）。
    /// 帯は見えている分しか作らないので、名前と絵も作られた分しか引かない。
    /// </summary>
    public IReadOnlyList<EditQueueTile> QueueTiles { get; private set; } = [];

    /// <summary>
    /// 「購入した種類」の欄を開いているか。種類の多い商品では膨大になるので畳める（ユーザ指示）。
    /// 編集画面は開くたびに作り直されるので、アプリを閉じるまでここに持つ（次の商品へ進んでも畳んだまま）。
    /// </summary>
    private static bool s_isPurchasesExpanded = true;

    public bool IsPurchasesExpanded
    {
        get => s_isPurchasesExpanded;
        set
        {
            if (s_isPurchasesExpanded != value)
            {
                s_isPurchasesExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>見出しに添える種類の数。畳んでいても何件あるかは分かるように。</summary>
    public string PurchasesCountText => Variations.Count > 0 ? $"（{Variations.Count} 種類）" : string.Empty;

    /// <summary>上の帯を出すか。要らない人もいるので設定で消せる（ユーザ指示）。</summary>
    public bool ShowsQueueStrip => _services.Settings.ShowEditQueueStrip;

    private RelayCommand? _goFirstCommand;

    /// <summary>1件目へ戻る（ユーザ指示）。いま開いている商品の入力は保存しない（スキップと同じ）。</summary>
    public RelayCommand GoFirstCommand => _goFirstCommand ??= new RelayCommand(
        () => _ = JumpAsync(0),
        () => _queue.Count > 0 && _index != 0 && !IsSaving);

    private RelayCommand? _goFirstUnsavedCommand;

    /// <summary>
    /// まだ保存していない物のうち、いちばん前へ飛ぶ（ユーザ指示）。
    /// 飛ばしながら進んだ後で、残した物を頭から片付けるため。
    /// </summary>
    public RelayCommand GoFirstUnsavedCommand => _goFirstUnsavedCommand ??= new RelayCommand(
        () =>
        {
            if (FirstUnsavedIndex() is { } index)
            {
                _ = JumpAsync(index);
            }
        },
        () => !IsSaving && FirstUnsavedIndex() is { } index && index != _index);

    private int? FirstUnsavedIndex()
    {
        for (var index = 0; index < _queue.Count; index++)
        {
            if (!_saved.Contains(_queue[index]))
            {
                return index;
            }
        }

        return null;
    }

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

        RememberStep();
        await MoveToAsync(index);
    }

    /// <summary>画面の履歴から戻ってきたとき。履歴には積まない（戻るで積むと、戻った先から戻れなくなる）。</summary>
    /// <param name="itemId">そのとき開いていた商品。順番の中に無ければ（IDを変えた等）位置で開く。</param>
    public Task ShowStepAsync(string? itemId, int fallbackIndex)
    {
        var at = itemId is null ? -1 : _queue.IndexOf(itemId);
        var target = at >= 0 ? at : Math.Clamp(fallbackIndex, 0, _queue.Count);
        return target == _index ? Task.CompletedTask : MoveToAsync(target);
    }

    private async Task MoveToAsync(int index)
    {
        StopReturnTimer();
        CaptureDraft();
        _index = index;
        await SavePositionAsync();
        await LoadCurrentAsync();
    }

    /// <summary>
    /// 別の商品へ移る前に、今の商品を画面の履歴に積む（ユーザ指示 2026-09-12）。
    /// 保存して次へ・スキップ・前へ・帯で飛ぶのどれでも、Alt＋← で直前に開いていた商品へ戻れる
    /// </summary>
    private void RememberStep()
    {
        if (_item is not null)
        {
            _main.RememberEditStep(this);
        }
    }

    private void RebuildQueueTiles()
    {
        // 1件目から最後まで全部並べる（ユーザ判断）。以前は済んだ物を5件で切っていたので、
        // 最後の方の件を開くと帯が数枚になり、送る分が無くてホイールが効かないように見えた。
        // 開いたときに見せる所（済んだ5件＋今の商品が左端）は、画面の側が送って合わせる
        var tiles = new List<EditQueueTile>(_queue.Count);

        for (var index = 0; index < _queue.Count; index++)
        {
            var itemId = _queue[index];

            // 名前も絵も、帯に作られたときに初めて引く。検索が読んである写しから引き、
            // 1件進むたびにJSONを読み直さない（#71 と同じ理由）
            tiles.Add(new EditQueueTile
            {
                Index = index,
                NameFactory = () => _main.Search.FindItem(itemId)?.DisplayName ?? itemId,
                IsCurrent = index == _index,
                IsPast = index < _index,
                IsSaved = _saved.Contains(itemId),
                IsDraft = _main.Drafts.Contains(itemId),
                ImageFactory = onLoaded => TileImage(itemId, onLoaded, preview: false),
                PreviewFactory = onLoaded => TileImage(itemId, onLoaded, preview: true),
            });
        }

        QueueTiles = tiles;
        OnPropertyChanged(nameof(QueueTiles));
        OnPropertyChanged(nameof(QueueFirstVisibleIndex));
    }

    /// <summary>
    /// 開いたときに帯の左端へ来る絵の位置。済んだ物を5件見せ、その次が今の商品になる（ユーザ指示）。
    /// 最後の方では帯の右端で止まるので、実際にはもっと前から見える。
    /// </summary>
    public int QueueFirstVisibleIndex => Math.Max(0, _index - PastTileCount);

    /// <summary>
    /// 帯の絵。検索のカードと同じ1枚（指名・役割の設定を見る）にそろえる。
    /// 乗せたときに大きく出す方（<paramref name="preview"/>）はカードの大きさで読む。
    /// </summary>
    private BitmapSource? TileImage(string itemId, Action onLoaded, bool preview)
    {
        if (_main.Search.FindItem(itemId) is not { } record)
        {
            return null;
        }

        var directory = _services.Paths.ItemImagesDir(record.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, record.Booth.Images, _thumbnails.ListFiles(directory), record.Local.UserImages);
        var path = Core.Images.ItemImageOrder.Thumbnail(
            ordered, record.Local.ThumbnailImage, _services.Settings.ThumbnailRole, record.Local.ImageRoles);

        if (path is null)
        {
            return null;
        }

        return preview ? _thumbnails.PeekForCard(path, onLoaded) : _thumbnails.PeekForTile(path, onLoaded);
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

                // 「残り n 件を表示」で全部並べたのは、その商品を開いている間だけ
                _showAllTags = false;
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
        _files = record.Local.OwnedFiles
            .Where(file => file.Paths.Count > 0)
            .Select(file => (file.Hash, System.IO.Path.GetFileName(file.Paths[0])))
            .ToList();

        _fileVariations = record.Local.OwnedFiles.ToDictionary(
            file => file.Hash, file => file.VariationId, StringComparer.OrdinalIgnoreCase);
        _savedFileVariations = new Dictionary<string, long?>(_fileVariations, StringComparer.OrdinalIgnoreCase);

        _canLinkAny = record.Booth.Variations.Count >= 2 && _files.Count > 0;

        // 商品ごとに畳んだ状態から始める（既定は畳む・ユーザ指示）
        _isFileSortExpanded = false;

        foreach (var row in Variations)
        {
            row.LinkRequested = choice =>
            {
                _fileVariations[choice.Hash] = row.VariationId;
                RefreshFileLinks();
            };
            row.PurchasedChanged = OnPurchasedChanged;
        }

        _purchasedCount = PurchasedVariationCount();
        RefreshFileLinks();
    }

    // ---- ファイルの種類分け（ユーザ指示 2026-09-12） ----
    //
    // ファイルを主にした一覧を「購入した種類」とメモの間に置く。種類を主にした各行の選び欄は便利なので残し、
    // 同じ中身（_fileVariations）を直すので揃う。
    // 買った種類が1つなら、全部のファイルをその種類として**見せるだけ**で書かない（計算で出せる値なので・ユーザ判断）。
    // 2つ目の種類に印を付けると自動の見せ方が消え（全部解除）、ファイルごとに選べるようになる

    /// <summary>この商品で種類を選べるか（BOOTHの種類が2つ以上で、手元にファイルがある）。</summary>
    private bool _canLinkAny;

    /// <summary>買った種類の数（「種類を選ばない購入」は数えない。付け先の種類が無い）。</summary>
    private int _purchasedCount;

    private bool _isFileSortExpanded;

    private int PurchasedVariationCount() => Variations.Count(row => row.IsPurchased && row.VariationId is not null);

    /// <summary>買った種類が1つだけならその種類。全部のファイルをこれとして見せる。</summary>
    private long? AutoVariation()
    {
        if (_files.Count == 0)
        {
            return null;
        }

        var purchased = Variations.Where(row => row.IsPurchased && row.VariationId is not null).ToList();
        return purchased.Count == 1 ? purchased[0].VariationId : null;
    }

    private void OnPurchasedChanged()
    {
        _purchasedCount = PurchasedVariationCount();

        // 勝手には開かない（案A・ユーザ判断）。印を付けるたびに下の欄が開いたり閉じたりすると、画面が動いて分かりにくい。
        // 残りがあることは見出しの「未指定 n」で知らせる
        RefreshFileLinks();
    }

    /// <summary>ファイルを主にした一覧の行。</summary>
    public ObservableCollection<FileSortRow> FileSortRows { get; } = [];

    /// <summary>
    /// 欄を開けるか。種類を2つ以上買ったとき、またはこの商品にファイルが2つ以上付いているとき（ユーザ指示）。
    /// 開けないときも欄は消さず、灰色にして理由を書く（何も無い所へいきなり現れるのは変なので・ユーザ指示）。
    /// </summary>
    public bool CanSortFiles => _purchasedCount >= 2 || _files.Count >= 2;

    public bool IsFileSortExpanded
    {
        get => _isFileSortExpanded && CanSortFiles;
        set
        {
            if (_isFileSortExpanded != value)
            {
                _isFileSortExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 見出しの右の一言。開けないときは理由、開けるときはファイルの数と、まだ種類を付けていない数。
    /// 欄は勝手に開かないので、残りがあることはここで知らせる（案A）。
    /// </summary>
    public string FileSortHeaderNote => CanSortFiles
        ? $"（{_files.Count} ファイル・未指定 {FileSortRows.Count(row => row.IsUnassigned)}）"
        : "　複数の種類を購入するか、複数のファイルがこの商品に付いている場合にだけ開けます";

    public bool HasNoFilesToSort => _files.Count == 0;

    /// <summary>
    /// 欄の中の表示。ファイルごと（ファイルを主に種類を選ぶ）と種類ごと（種類を主にファイルを足す）。
    /// 種類ごとは、以前「購入した種類」の各行にあった選び方をこの欄へ移したもの（案A：入口を1か所にする）。
    /// 選んだ表示は次の商品へ進んでも保つ（アプリを閉じるまで）。
    /// </summary>
    private static bool s_isByVariationView;

    public bool IsByVariationView
    {
        get => s_isByVariationView;
        set
        {
            if (s_isByVariationView != value)
            {
                s_isByVariationView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsByFileView));
            }
        }
    }

    public bool IsByFileView
    {
        get => !s_isByVariationView;
        set => IsByVariationView = !value;
    }

    /// <summary>種類ごとの表示に並べる、買った種類の行。</summary>
    public ObservableCollection<OrderedVariationInput> PurchasedVariationRows { get; } = [];

    private void RebuildFileSortRows(long? auto)
    {
        FileSortRows.Clear();

        // 買った種類が1つなら、その種類を「（自動）」として1つだけ見せる（書かない）。案内の枠は出さない（案A）
        var choices = auto is { } autoId
            ? [new VariationChoice(autoId, $"{Variations.First(row => row.VariationId == autoId).Name}（自動）")]
            : new List<VariationChoice> { new(null, "指定しない") };
        if (auto is null)
        {
            choices.AddRange(Variations
                .Where(row => row.VariationId is not null && row.IsPurchased)
                .Select(row => new VariationChoice(row.VariationId, row.Name)));
        }

        foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
        {
            // 選べるのは買った種類と「指定しない」だけ（ユーザ指示）
            var current = auto ?? EffectiveVariation(hash);
            var rowChoices = choices.ToList();

            var row = new FileSortRow(rowChoices.First(choice => choice.VariationId == current))
            {
                Hash = hash,
                Name = name,
                Choices = rowChoices,
                IsAuto = auto is not null,
            };

            // 選び欄の選択の最中に一覧を作り直すと選び欄が迷うので、選び終えてから作り直す
            row.Changed = variationId =>
            {
                _fileVariations[hash] = variationId;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(RefreshFileLinks));
            };

            FileSortRows.Add(row);
        }
    }

    private void RefreshFileLinks()
    {
        var names = Variations
            .Where(row => row.VariationId is not null)
            .ToDictionary(row => row.VariationId!.Value, row => row.Name);

        var auto = AutoVariation();

        foreach (var row in Variations)
        {
            row.LinkedFiles.Clear();
            row.FileChoices.Clear();

            // 「種類を選ばない購入」の行には付けない（付け先の種類が無い）。
            // 買った種類が1つのときは全部その種類として見せるので、選ぶ欄は出さない
            // 結び付けられるのは買った種類だけ（ユーザ指示）。買っていない種類のファイルは手元に無いはず
            row.CanLinkFiles = auto is null && _canLinkAny && row.VariationId is not null && row.IsPurchased;

            if (auto is { } autoId)
            {
                if (row.VariationId == autoId)
                {
                    foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
                    {
                        row.LinkedFiles.Add(new FileLinkInput { Hash = hash, Name = name });
                    }
                }

                row.NoteLinkedFilesChanged();
                continue;
            }

            if (!row.CanLinkFiles)
            {
                row.NoteLinkedFilesChanged();
                continue;
            }

            foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
            {
                var current = EffectiveVariation(hash);
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
                .Where(file => EffectiveVariation(file.Hash) != row.VariationId)
                .Select(file =>
                {
                    var other = EffectiveVariation(file.Hash);
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

        RebuildFileSortRows(auto);

        PurchasedVariationRows.Clear();
        foreach (var row in Variations.Where(row => row.VariationId is not null && row.IsPurchased))
        {
            PurchasedVariationRows.Add(row);
        }

        foreach (var name in new[]
        {
            nameof(CanSortFiles), nameof(IsFileSortExpanded), nameof(FileSortHeaderNote), nameof(HasNoFilesToSort),
        })
        {
            OnPropertyChanged(name);
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

    /// <summary>
    /// 画面と保存に使う結び付け。**買っていない種類に付いていれば「指定しない」として扱う**（ユーザ指示）。
    /// 画面の中の控え（_fileVariations）は消さないので、印を外して付け直せば元の結び付けに戻る。
    /// </summary>
    private long? EffectiveVariation(string hash)
        => _fileVariations.GetValueOrDefault(hash) is { } id
            && Variations.Any(row => row.VariationId == id && row.IsPurchased)
                ? id
                : null;

    /// <summary>開いた時点から変わった紐付けだけ。買っていない種類への結び付けは外れた扱いで書く。</summary>
    private Dictionary<string, long?> ChangedFileVariations()
        => _fileVariations.Keys
            .Select(hash => (Hash: hash, Value: EffectiveVariation(hash)))
            .Where(pair => _savedFileVariations.GetValueOrDefault(pair.Hash) != pair.Value)
            .ToDictionary(pair => pair.Hash, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

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
                await _services.Edit.NoteSavedAsync(_item.Id);
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
        _ = SavePositionAsync();
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

        // 捨てるのは未編集の順番の記録だけ。指定して入った順番は履歴に預けてあり、ファイルには無い
        if (_run is null)
        {
            await _services.Edit.ClearSessionAsync();
        }

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

        OnPropertyChanged(nameof(PurchasesCountText));
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
        OnPropertyChanged(nameof(BoothTagTiles));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
