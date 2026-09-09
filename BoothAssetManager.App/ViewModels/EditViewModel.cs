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

    public required long VariationId { get; init; }

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

    public string KindLabel => Kind switch
    {
        PurchaseKind.Received => "貰った",
        PurchaseKind.Given => "贈った",
        _ => "自分用",
    };

    /// <summary>
    /// 同じ版にこれ以外の購入記録が何件あるか。
    ///
    /// 買った1回が1レコードなので、同じ版を2回買った記録も持てる。
    /// この画面は1版につき1行しか出せないので、残りは触らずに持ち回す。
    /// 黙って消すと、贈答や買い直しの記録が編集するたびに減っていく。
    /// </summary>
    public IReadOnlyList<Purchase> Extras { get; set; } = [];

    public bool HasExtras => Extras.Count > 0;

    public string ExtrasText => $"この版にはほかに {Extras.Count} 件の記録があります（ここでは最初の1件だけ編集できます）";
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
    private List<string> _queue = [];
    private int _index;
    private int _remainingSeconds;
    private ItemRecord? _item;
    private string _memo = string.Empty;
    private string _acquiredAt = string.Empty;
    private bool _notifyOnUpdate = true;
    private bool _isHidden;
    private string _statusText = string.Empty;
    private bool _isSaving;

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
        OpenBoothCommand = new RelayCommand(OpenBooth, () => HasItem);
        AddTagCommand = new RelayCommand(parameter => _ = AddTagAsync(parameter as string));
        AddAttributeCommand = new RelayCommand(parameter => _ = AddAttributeAsync(parameter as string));
        StayCommand = new RelayCommand(StopReturnTimer);

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

    public string Name => _item?.Booth.Name ?? string.Empty;

    public string ShopName => _item?.Booth.Shop?.Name ?? string.Empty;

    public string CategoryText => _item?.Booth.Category is null
        ? string.Empty
        : _item.Booth.Category.ParentName is null
            ? _item.Booth.Category.Name
            : $"{_item.Booth.Category.ParentName} / {_item.Booth.Category.Name}";

    private int _selectedImageIndex;

    /// <summary>今メインに出している画像。属性を付けるには複数枚見たいので切り替えられる。</summary>
    public BitmapSource? MainImage => Images.Count == 0
        ? null
        : Images[Math.Clamp(_selectedImageIndex, 0, Images.Count - 1)].Image;

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
        await LoadCurrentAsync();
    }

    private async Task<List<string>> BuildDefaultQueueAsync()
    {
        var loaded = await _services.Store.Items.LoadAllAsync();
        return loaded.Items
            .Where(item => item.Local.UserTags.Count == 0)
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
                FillFromItem(record);
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

        Memo = record.Local.Memo ?? string.Empty;
        AcquiredAt = record.Local.AcquiredAt?.ToString("yyyy-MM-dd") ?? string.Empty;
        OnPropertyChanged(nameof(AcquiredHintText));
        NotifyOnUpdate = record.Local.NotifyOnUpdate;
        IsHidden = record.Local.IsHidden;

        BuildVariations(record);
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
    private void RefreshSuggestions()
    {
        TagSuggestions.Clear();
        foreach (var top in _tagMaster.Tops
            .Select(top => top.Name)
            .Where(name => !Tags.Any(row => string.Equals(row.Top, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            TagSuggestions.Add(top);
        }

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
        // 画面は1版1行なので、2件目以降は触らずに持ち回す（下の Extras）
        var ordered = record.Local.Purchases
            .GroupBy(purchase => purchase.VariationId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var variation in record.Booth.Variations)
        {
            var purchased = ordered.TryGetValue(variation.Id, out var group);
            var first = group?.FirstOrDefault();

            Variations.Add(new OrderedVariationInput
            {
                VariationId = variation.Id,
                Name = variation.Name ?? "（バリエーションなし）",
                ListPrice = variation.Price,
                ListPriceText = $"¥{variation.Price:N0}",
                IsPurchased = purchased,
                Price = first?.Price?.ToString() ?? string.Empty,
                Kind = first?.Kind ?? PurchaseKind.ForSelf,
                Extras = group?.Skip(1).ToList() ?? [],
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す
        var currentIds = record.Booth.Variations.Select(variation => variation.Id).ToHashSet();
        foreach (var group in ordered.Where(pair => !currentIds.Contains(pair.Key)))
        {
            var first = group.Value[0];
            Variations.Add(new OrderedVariationInput
            {
                VariationId = group.Key,
                Name = first.NameSnapshot ?? $"variation {group.Key}",
                ListPriceText = "-",
                IsGone = true,
                IsPurchased = true,
                Price = first.Price?.ToString() ?? string.Empty,
                Kind = first.Kind,
                Extras = group.Value.Skip(1).ToList(),
            });
        }

        // ここから先の変更はユーザ操作。価格の自動入力を許可する
        foreach (var input in Variations)
        {
            input.IsInitialized = true;
        }
    }

    private void BuildImages(ItemRecord record)
    {
        Images.Clear();
        _selectedImageIndex = 0;
        var directory = _services.Paths.ItemImagesDir(record.Id);

        foreach (var entry in Core.Images.ItemImageOrder.Arrange(
            directory, record.Booth.Images, _thumbnails.ListFiles(directory)))
        {
            Images.Add(new GalleryImage
            {
                Path = entry.Path,
                Image = _thumbnails.Load(entry.Path),
                IsOrphaned = entry.IsOrphaned,
            });
        }
    }

    /// <summary>入力を <c>local</c> ブロックに組み直す。触っていない項目は元の値のまま残す。</summary>
    private LocalBlock BuildLocal(ItemRecord record)
    {
        var userTags = Tags
            .Select(row => new UserTagAssignment { Top = row.Top, Subs = row.Subs.ToList() })
            .ToList();

        var attributes = Attributes.ToDictionary(row => row.Name, row => row.Value);

        // 編集できるのは版ごとの1件目だけ。2件目以降はそのまま書き戻す
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
            }.Concat(variation.Extras))
            .ToList();

        return record.Local with
        {
            UserTags = userTags,
            Attributes = attributes,
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
                new UiCommand.SaveItemLocal(_item.Id, BuildLocal(_item)));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                return;
            }

            StatusText = string.Empty;
            await AdvanceAsync();
        }
        finally
        {
            IsSaving = false;
        }
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

        var url = _item.Booth.Url ?? BoothClient.ItemPageUrl(_item.Id);
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
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ShopName));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(MainImage));
        OnPropertyChanged(nameof(DescriptionPreview));
        OnPropertyChanged(nameof(BoothTags));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
