using System.Collections.ObjectModel;
using System.Windows;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>属性1件。平均も出すのは、値の入り方が偏っていないか見えるようにするため。</summary>
public sealed class AttributeMasterRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    public string? Memo { get; init; }

    public required int ItemCount { get; init; }

    public double? Average { get; init; }

    public string ItemCountText => ItemCount == 0 ? "未評価" : $"{ItemCount}";

    public string AverageText => Average is null ? "評価なし" : $"平均 {Average.Value:0} %";

    public bool IsUsed => ItemCount > 0;

    /// <summary>
    /// 編集画面で最初から並べる属性か。
    ///
    /// **並べるだけで、値は保存しない。**触らなかった行は書き出されない。
    /// </summary>
    public required bool IsDefault { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>マスタに無いのにitemが参照している属性。要確認は知らせるだけで、直せるのはここ。</summary>
public sealed class OrphanAttributeRow : ViewModelBase
{
    public required string Name { get; init; }

    public required int ItemCount { get; init; }

    public string ItemCountText => $"{ItemCount} 件の商品が参照";

    public RelayCommand? AddToMasterCommand { get; set; }

    public RelayCommand? MergeCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// 属性の管理画面。タグの管理と同じ作りにしてある（一覧＋詳細、件数から検索へ、
/// マスタに無い参照の修復、ドラッグでの並べ替え）。
///
/// 違いはitem側が名前だけでなく 0〜100 の値を持つこと。統合すると
/// 「両方に値が入っているitemでどちらを残すか」が出るので、そこだけ聞く。
/// </summary>
public sealed class AttributeManageViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    private List<AttributeMasterRow> _all = [];
    private AttributeMasterRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private string _statusText = string.Empty;

    public AttributeManageViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        AddCommand = new RelayCommand(parameter => _ = AddAsync(parameter as string));
        RenameCommand = new RelayCommand(parameter => _ = RenameAsync(parameter as string), _ => Selected is not null);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync(), () => Selected is not null);
        SaveMemoCommand = new RelayCommand(() => _ = SaveMemoAsync(), () => Selected is not null && MemoChanged);
        ToggleDefaultCommand = new RelayCommand(() => _ = ToggleDefaultAsync(), () => Selected is not null);
        RefreshCommand = new RelayCommand(() => _ = ReloadAsync());
        ShowItemsCommand = new RelayCommand(
            () => _main.ShowItemsWithAttribute(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        _ = ReloadAsync();
    }

    public ObservableCollection<AttributeMasterRow> Rows { get; } = [];

    public ObservableCollection<OrphanAttributeRow> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherNames { get; } = [];

    /// <summary>マスタにある全属性。マスタに無い参照の寄せ先はこちらを候補にする。</summary>
    public ObservableCollection<string> AllNames { get; } = [];

    public RelayCommand AddCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand SaveMemoCommand { get; }

    /// <summary>編集画面で最初から並べる属性かを切り替える</summary>
    public RelayCommand ToggleDefaultCommand { get; }

    public string ToggleDefaultText => Selected?.IsDefault == true
        ? "最初から並べるのをやめる"
        : "編集画面に最初から並べる";

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ShowItemsCommand { get; }

    public AttributeMasterRow? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            _selected = value;

            if (_selected is not null)
            {
                _selected.IsSelected = true;
            }

            MemoDraft = _selected?.Memo ?? string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedUsageText));
            OnPropertyChanged(nameof(SelectedIsUsed));
            OnPropertyChanged(nameof(RenameImpactText));
            OnPropertyChanged(nameof(ToggleDefaultText));
            OnPropertyChanged(nameof(SelectedIsDefault));
            OnPropertyChanged(nameof(DefaultNote));
            RebuildOtherNames();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public bool SelectedIsDefault => Selected?.IsDefault == true;

    /// <summary>並べるだけで保存しないことを、切り替える前に書いておく</summary>
    public string DefaultNote => SelectedIsDefault
        ? "編集画面に最初から並びます。値を動かすまで保存されません。"
        : "編集画面に最初から並べておけます。値を動かすまで保存されません。";

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品も評価していません。編集画面で値を入れると、ここに件数が出ます。"
            : $"{Selected.ItemCount} 件の商品で評価済み（{Selected.AverageText}）";

    public string RenameImpactText => Selected is null || Selected.ItemCount == 0
        ? "既にある属性を指定することで統合できます。"
        : $"既にある属性を指定することで統合できます。{Selected.ItemCount} 件の商品を書き換えます。";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                Rebuild();
            }
        }
    }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value))
            {
                OnPropertyChanged(nameof(MemoChanged));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool MemoChanged => Selected is not null && MemoDraft != (Selected.Memo ?? string.Empty);

    public bool HasOrphans => Orphans.Count > 0;

    public string HeaderText => $"属性 {_all.Count} 件";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    public async Task ReloadAsync()
    {
        var master = _services.Store.Attributes.Load();
        var usage = await _services.Attributes.LoadUsageAsync();
        var orphans = await _services.Attributes.LoadOrphansAsync();

        RunOnUiThread(() =>
        {
            var counts = usage.ToDictionary(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
            var keep = Selected?.Name;

            _all = master.Attributes.Select(definition => new AttributeMasterRow
            {
                Name = definition.Name,
                Memo = definition.Memo,
                ItemCount = counts.TryGetValue(definition.Name, out var entry) ? entry.ItemCount : 0,
                Average = counts.TryGetValue(definition.Name, out var found) ? found.Average : null,
                IsDefault = definition.IsDefault,
            }).ToList();

            AllNames.Clear();
            foreach (var row in _all)
            {
                AllNames.Add(row.Name);
            }

            Orphans.Clear();
            foreach (var orphan in orphans)
            {
                Orphans.Add(CreateOrphanRow(orphan));
            }

            Rebuild();
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(HasOrphans));

            Selected = _all.FirstOrDefault(row => row.Name == keep) ?? Rows.FirstOrDefault();
        });
    }

    private void Rebuild()
    {
        var filter = _filterText.Trim();

        Rows.Clear();
        foreach (var row in _all.Where(row => filter.Length == 0
            || row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Rows.Add(row);
        }
    }

    private void RebuildOtherNames()
    {
        OtherNames.Clear();
        foreach (var name in _all
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherNames.Add(name);
        }
    }

    private OrphanAttributeRow CreateOrphanRow(OrphanAttribute orphan)
    {
        var row = new OrphanAttributeRow { Name = orphan.Name, ItemCount = orphan.ItemCount };

        row.AddToMasterCommand = new RelayCommand(() => _ = AddOrphanToMasterAsync(row));
        row.MergeCommand = new RelayCommand(parameter => _ = MergeOrphanAsync(row, parameter as string));
        row.RemoveCommand = new RelayCommand(() => _ = RemoveOrphanAsync(row));

        return row;
    }

    /// <summary>
    /// 並べ替える。並びは検索の候補にも編集の候補にもそのまま出るので、
    /// 「よく使う順」に置けること自体が機能になる。itemの値には触らない。
    /// </summary>
    public async Task MoveAsync(AttributeMasterRow moved, AttributeMasterRow target, bool after)
    {
        var order = _all.Select(row => row.Name).ToList();

        var from = order.IndexOf(moved.Name);
        if (from < 0 || moved.Name == target.Name)
        {
            return;
        }

        order.RemoveAt(from);

        var at = order.IndexOf(target.Name);
        var to = after ? at + 1 : at;
        if (at < 0 || to == from)
        {
            return;
        }

        order.Insert(to, moved.Name);

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderAttributes(order));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    private async Task AddAsync(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(trimmed));
        StatusText = $"「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _all.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
    }

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。
    /// 統合では両方に値が入っているitemが出るので、そのときだけどちらを残すか聞く。
    /// </summary>
    private async Task RenameAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var merging = _all.Any(row => string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));
        var keep = AttributeMergeValue.KeepTarget;

        if (merging)
        {
            var preview = await _services.Attributes.PreviewMergeAsync(Selected.Name, target);
            var dialog = new Views.MergeAttributeDialog(
                new MergeAttributeDialogViewModel(Selected.Name, target, preview));

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            keep = ((MergeAttributeDialogViewModel)dialog.DataContext).Keep;
        }
        else if (!Confirm(
            $"「{Selected.Name}」を「{target}」に変更します。\n\n{Selected.ItemCount} 件の商品を書き換えます。",
            "名前を変更する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.RenameAttribute(Selected.Name, target, keep));

        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = rewritten.Result.WasMerged
                ? $"「{target}」に統合しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。"
                : $"「{target}」に変更しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。";
        }

        var next = target;
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _all.FirstOrDefault(row =>
            string.Equals(row.Name, next, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、評価も一緒に外す。
    /// 属性はuserTagと違って「未設定」が既定なので、空になっても編集の対象には戻らない。
    /// </summary>
    private async Task DeleteAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどの商品も評価していないので、影響はありません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件の商品から、この属性の評価が消えます。\n"
                + $"\nこの操作は元に戻せません。同じ名前で作り直しても、{Selected.ItemCount} 件ぶんの評価は戻りません。\n"
                + "入れ直すには、もう一度1件ずつ評価する必要があります。";

        if (!Confirm(message, "属性を削除する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteAttribute(Selected.Name));
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"削除しました（{rewritten.Result.ItemsUpdated} 件の商品から評価を外しました）。";
        }

        Selected = null;
        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private async Task SaveMemoAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.SetAttributeMemo(Selected.Name, MemoDraft));
        StatusText = "メモを保存しました。";
        await ReloadAsync();
    }

    /// <summary>
    /// 編集画面で最初から並べる属性かを切り替える。
    ///
    /// **既に評価してある商品には何もしない。**並べるだけで、
    /// 値は人が動かしたときにしか保存されない。
    /// </summary>
    private async Task ToggleDefaultAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var next = !Selected.IsDefault;
        await _services.Commands.ExecuteAsync(new UiCommand.SetAttributeDefault(Selected.Name, next));
        StatusText = next
            ? $"「{Selected.Name}」を編集画面に最初から並べます。値は動かしたときだけ付きます。"
            : $"「{Selected.Name}」を最初から並べるのをやめました。付けた評価はそのまま残ります。";
        await ReloadAsync();
    }

    private async Task AddOrphanToMasterAsync(OrphanAttributeRow row)
    {
        await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(row.Name));
        StatusText = $"「{row.Name}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに出るようになります。";
        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private async Task MergeOrphanAsync(OrphanAttributeRow row, string? target)
    {
        var name = target?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var preview = await _services.Attributes.PreviewMergeAsync(row.Name, name);
        var dialog = new Views.MergeAttributeDialog(
            new MergeAttributeDialogViewModel(row.Name, name, preview));

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var keep = ((MergeAttributeDialogViewModel)dialog.DataContext).Keep;

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameAttribute(row.Name, name, keep));
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"「{name}」に寄せました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。";
        }

        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private async Task RemoveOrphanAsync(OrphanAttributeRow row)
    {
        if (!Confirm(
            $"「{row.Name}」の評価を {row.ItemCount} 件の商品から消します。\n\nこの操作は元に戻せません。",
            "参照を外す"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteAttribute(row.Name));
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"「{row.Name}」を外しました（{rewritten.Result.ItemsUpdated} 件の商品から消しました）。";
        }

        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>既定はキャンセル。Enterを押しただけで消えないようにする。</summary>
    private static bool Confirm(string message, string caption)
        => MessageBox.Show(
            message,
            caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
