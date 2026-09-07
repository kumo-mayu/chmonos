using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>appTagのトップ1つと、その配下のサブ。トップを外すとサブも一緒に外れる。</summary>
public sealed class AppTagChoice : ViewModelBase
{
    private bool _isSelected;
    private string _pendingSub = string.Empty;

    public required string Name { get; init; }

    public ObservableCollection<AppTagSubChoice> Subs { get; } = [];

    /// <summary>このトップの下に足すサブの入力欄。編集の途中で分類を増やせるようにする。</summary>
    public string PendingSub
    {
        get => _pendingSub;
        set => SetField(ref _pendingSub, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value) && !value)
            {
                // トップを外したらサブも落とす。従属関係をUI側でも守る
                foreach (var sub in Subs)
                {
                    sub.IsSelected = false;
                }
            }
        }
    }

    public bool HasSubs => Subs.Count > 0;
}

public sealed class AppTagSubChoice : ViewModelBase
{
    private bool _isSelected;

    public required string Name { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>
/// 属性1つ。未評価（値なし）と0は別物なので、有効フラグと値を分けて持つ。
/// </summary>
public sealed class AttributeChoice : ViewModelBase
{
    private bool _isRated;
    private int _value = 50;

    public required string Name { get; init; }

    /// <summary>評価しているか。オフなら item 側にキーを書かない（＝未評価）。</summary>
    public bool IsRated
    {
        get => _isRated;
        set
        {
            if (SetField(ref _isRated, value))
            {
                OnPropertyChanged(nameof(ValueText));
            }
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                // つまみを動かしたら評価したものとして扱う。チェックを別に押させない
                IsRated = true;
                OnPropertyChanged(nameof(ValueText));
            }
        }
    }

    public string ValueText => IsRated ? $"{Value}%" : "未評価";
}

/// <summary>購入記録の入力行。</summary>
public sealed class OrderedVariationInput : ViewModelBase
{
    private bool _isPurchased;
    private string _price = string.Empty;
    private bool _isGifted;

    public required long VariationId { get; init; }

    public required string Name { get; init; }

    /// <summary>BOOTHの現在価格。未入力のときの目安として出す。</summary>
    public required string ListPriceText { get; init; }

    public int? ListPrice { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }

    public bool IsPurchased
    {
        get => _isPurchased;
        set => SetField(ref _isPurchased, value);
    }

    /// <summary>購入価格。空欄は未入力、0は無料配布。</summary>
    public string Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    public bool IsGifted
    {
        get => _isGifted;
        set => SetField(ref _isGifted, value);
    }
}

/// <summary>
/// 編集画面。取り込んだitemに、ユーザにしか決められない情報を入れていく。
///
/// 複数件を順に処理する形にしているのは、この作業が
/// 「1件ずつ判断して次へ送る」という性質のものだから（設計メモの通り）。
/// 位置は1件進むごとに edit-session.json へ書くので、途中で閉じても続きから再開できる。
/// </summary>
public sealed class EditViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    private List<string> _queue = [];
    private int _index;
    private ItemRecord? _item;
    private string _memo = string.Empty;
    private string _acquiredAt = string.Empty;
    private bool _notifyOnUpdate = true;
    private bool _isHidden;
    private string _newTopTag = string.Empty;
    private string _newAttribute = string.Empty;
    private string _statusText = string.Empty;
    private bool _isSaving;

    public EditViewModel(AppServiceContainer services, MainViewModel main, ThumbnailLoader thumbnails)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        SaveAndNextCommand = new RelayCommand(() => _ = SaveAndAdvanceAsync(), () => HasItem && !IsSaving);
        SkipCommand = new RelayCommand(() => _ = AdvanceAsync(), () => HasItem && !IsSaving);
        BackCommand = new RelayCommand(GoBack, () => _index > 0);
        FinishCommand = new RelayCommand(() => _ = FinishAsync());
        AddTopTagCommand = new RelayCommand(() => _ = AddTopTagAsync(), () => NewTopTag.Trim().Length > 0);
        AddAttributeCommand = new RelayCommand(() => _ = AddAttributeAsync(), () => NewAttribute.Trim().Length > 0);
        AddSubTagCommand = new RelayCommand(parameter => _ = AddSubTagAsync(parameter), parameter => parameter is AppTagChoice);
    }

    public RelayCommand SaveAndNextCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand FinishCommand { get; }

    public RelayCommand AddTopTagCommand { get; }

    public RelayCommand AddSubTagCommand { get; }

    public RelayCommand AddAttributeCommand { get; }

    public ObservableCollection<AppTagChoice> AppTags { get; } = [];

    public ObservableCollection<AttributeChoice> Attributes { get; } = [];

    public ObservableCollection<OrderedVariationInput> Variations { get; } = [];

    public ObservableCollection<GalleryImage> Images { get; } = [];

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

    public BitmapSource? MainImage => Images.Count == 0 ? null : Images[0].Image;

    public string DescriptionPreview => _item?.Booth.Description ?? string.Empty;

    public IReadOnlyList<string> BoothTags => _item?.Booth.Tags ?? [];

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
        set => SetField(ref _acquiredAt, value);
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

    public string NewTopTag
    {
        get => _newTopTag;
        set
        {
            if (SetField(ref _newTopTag, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewAttribute
    {
        get => _newAttribute;
        set
        {
            if (SetField(ref _newAttribute, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// キューを積んで最初の1件を開く。
    /// <paramref name="itemIds"/> が空なら、appTag未設定のitemを対象にする（ナビのバッジと同じ定義）。
    /// </summary>
    public async Task StartAsync(IReadOnlyList<string>? itemIds = null)
    {
        var queue = itemIds?.ToList() ?? await BuildDefaultQueueAsync();

        _queue = queue;
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
            .Where(item => item.Local.AppTags.Count == 0)
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
                LoadMasters();
                FillFromItem(record);
                RaiseItemChanged();
                return;
            }

            _index++;
        }

        _item = null;
        RaiseItemChanged();
    }

    private void LoadMasters()
    {
        var tagMaster = _services.Store.AppTags.Load();
        AppTags.Clear();
        foreach (var top in tagMaster.Tops)
        {
            var choice = new AppTagChoice { Name = top.Name };
            foreach (var sub in top.Subs)
            {
                choice.Subs.Add(new AppTagSubChoice { Name = sub.Name });
            }

            AppTags.Add(choice);
        }

        var attributeMaster = _services.Store.Attributes.Load();
        Attributes.Clear();
        foreach (var definition in attributeMaster.Attributes)
        {
            Attributes.Add(new AttributeChoice { Name = definition.Name });
        }
    }

    private void FillFromItem(ItemRecord record)
    {
        foreach (var assignment in record.Local.AppTags)
        {
            var top = AppTags.FirstOrDefault(choice =>
                string.Equals(choice.Name, assignment.Top, StringComparison.CurrentCultureIgnoreCase));
            if (top is null)
            {
                continue;
            }

            top.IsSelected = true;
            foreach (var subName in assignment.Subs)
            {
                var sub = top.Subs.FirstOrDefault(choice =>
                    string.Equals(choice.Name, subName, StringComparison.CurrentCultureIgnoreCase));
                if (sub is not null)
                {
                    sub.IsSelected = true;
                }
            }
        }

        foreach (var attribute in Attributes)
        {
            if (record.Local.Attributes.TryGetValue(attribute.Name, out var value))
            {
                attribute.Value = value;
                attribute.IsRated = true;
            }
            else
            {
                attribute.IsRated = false;
            }
        }

        Memo = record.Local.Memo ?? string.Empty;
        AcquiredAt = record.Local.AcquiredAt?.ToString("yyyy-MM-dd") ?? string.Empty;
        NotifyOnUpdate = record.Local.NotifyOnUpdate;
        IsHidden = record.Local.IsHidden;

        BuildVariations(record);
        BuildImages(record);
    }

    private void BuildVariations(ItemRecord record)
    {
        Variations.Clear();
        var ordered = record.Local.OrderedVariations.ToDictionary(entry => entry.VariationId);

        foreach (var variation in record.Booth.Variations)
        {
            var purchased = ordered.TryGetValue(variation.Id, out var existing);
            Variations.Add(new OrderedVariationInput
            {
                VariationId = variation.Id,
                Name = variation.Name ?? "（バリエーションなし）",
                ListPrice = variation.Price,
                ListPriceText = $"¥{variation.Price:N0}",
                IsPurchased = purchased,
                Price = existing?.Price?.ToString() ?? string.Empty,
                IsGifted = existing?.IsGifted ?? false,
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す
        var currentIds = record.Booth.Variations.Select(variation => variation.Id).ToHashSet();
        foreach (var entry in record.Local.OrderedVariations.Where(entry => !currentIds.Contains(entry.VariationId)))
        {
            Variations.Add(new OrderedVariationInput
            {
                VariationId = entry.VariationId,
                Name = entry.NameSnapshot ?? $"variation {entry.VariationId}",
                ListPriceText = "-",
                IsGone = true,
                IsPurchased = true,
                Price = entry.Price?.ToString() ?? string.Empty,
                IsGifted = entry.IsGifted,
            });
        }
    }

    private void BuildImages(ItemRecord record)
    {
        Images.Clear();
        foreach (var path in _thumbnails.ListFiles(_services.Paths.ItemImagesDir(record.Id)))
        {
            Images.Add(new GalleryImage { Path = path, Image = _thumbnails.Load(path) });
        }
    }

    /// <summary>入力を <c>local</c> ブロックに組み直す。触っていない項目は元の値のまま残す。</summary>
    private LocalBlock BuildLocal(ItemRecord record)
    {
        var appTags = AppTags
            .Where(top => top.IsSelected)
            .Select(top => new AppTagAssignment
            {
                Top = top.Name,
                Subs = top.Subs.Where(sub => sub.IsSelected).Select(sub => sub.Name).ToList(),
            })
            .ToList();

        var attributes = Attributes
            .Where(attribute => attribute.IsRated)
            .ToDictionary(attribute => attribute.Name, attribute => attribute.Value);

        var ordered = Variations
            .Where(variation => variation.IsPurchased)
            .Select(variation => new OrderedVariation
            {
                VariationId = variation.VariationId,
                NameSnapshot = variation.Name,
                Price = int.TryParse(variation.Price.Trim(), out var price) ? price : null,
                IsGifted = variation.IsGifted,
                ExistsOnBooth = !variation.IsGone,
            })
            .ToList();

        return record.Local with
        {
            AppTags = appTags,
            Attributes = attributes,
            Memo = string.IsNullOrWhiteSpace(Memo) ? null : Memo.Trim(),
            OrderedVariations = ordered,
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

        _index--;
        _ = _services.Edit.AdvanceSessionAsync(_index);
        _ = LoadCurrentAsync();
    }

    /// <summary>編集を終える。キューを捨てて検索へ戻る。</summary>
    private async Task FinishAsync()
    {
        await _services.Edit.ClearSessionAsync();
        await _main.ReloadLibraryAsync();
        _main.ShowSearch();
    }

    private async Task AddTopTagAsync()
    {
        var name = NewTopTag.Trim();
        await _services.Commands.ExecuteAsync(new UiCommand.AddAppTag(name));
        NewTopTag = string.Empty;
        ReloadTagsKeepingSelection();

        // 作ったものは、そのまま付けたいことがほとんど
        var added = AppTags.FirstOrDefault(choice =>
            string.Equals(choice.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (added is not null)
        {
            added.IsSelected = true;
        }
    }

    private async Task AddSubTagAsync(object? parameter)
    {
        if (parameter is not AppTagChoice top || string.IsNullOrWhiteSpace(top.PendingSub))
        {
            return;
        }

        var subName = top.PendingSub.Trim();
        await _services.Commands.ExecuteAsync(new UiCommand.AddAppTag(top.Name, subName));
        top.PendingSub = string.Empty;
        ReloadTagsKeepingSelection();

        var reloaded = AppTags.FirstOrDefault(choice =>
            string.Equals(choice.Name, top.Name, StringComparison.CurrentCultureIgnoreCase));
        var sub = reloaded?.Subs.FirstOrDefault(choice =>
            string.Equals(choice.Name, subName, StringComparison.CurrentCultureIgnoreCase));
        if (sub is not null)
        {
            sub.IsSelected = true;
        }
    }

    private async Task AddAttributeAsync()
    {
        var name = NewAttribute.Trim();
        await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(name));
        NewAttribute = string.Empty;

        if (!Attributes.Any(choice => string.Equals(choice.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            Attributes.Add(new AttributeChoice { Name = name });
        }
    }

    /// <summary>マスタを読み直しつつ、今画面で選んでいる状態は保つ。</summary>
    private void ReloadTagsKeepingSelection()
    {
        var selected = AppTags
            .Where(top => top.IsSelected)
            .ToDictionary(
                top => top.Name,
                top => top.Subs.Where(sub => sub.IsSelected).Select(sub => sub.Name).ToList(),
                StringComparer.CurrentCultureIgnoreCase);

        LoadMasters();

        foreach (var top in AppTags)
        {
            if (!selected.TryGetValue(top.Name, out var subs))
            {
                continue;
            }

            top.IsSelected = true;
            foreach (var sub in top.Subs.Where(sub => subs.Contains(sub.Name, StringComparer.CurrentCultureIgnoreCase)))
            {
                sub.IsSelected = true;
            }
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
