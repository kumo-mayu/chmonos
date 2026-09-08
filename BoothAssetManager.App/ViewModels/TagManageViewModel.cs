using System.Collections.ObjectModel;
using System.Windows;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 並べ替え中に、どこへ落ちるかを示す線。行の上か下かだけを持つ。
/// Adornerを使わないのは、行のテンプレートに1本足すだけで済むため。
/// </summary>
public abstract class ReorderableRow : ViewModelBase
{
    private bool _dropBefore;
    private bool _dropAfter;

    public bool DropBefore
    {
        get => _dropBefore;
        set => SetField(ref _dropBefore, value);
    }

    public bool DropAfter
    {
        get => _dropAfter;
        set => SetField(ref _dropAfter, value);
    }

    public void ClearDropIndicator()
    {
        DropBefore = false;
        DropAfter = false;
    }
}

/// <summary>トップレベル1件。件数を出すのは、消す前に影響が見えるようにするため。</summary>
public sealed class TagTopRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    public string? Memo { get; init; }

    public required int SubCount { get; init; }

    public required int ItemCount { get; init; }

    public string SubCountText => SubCount == 0 ? "サブなし" : $"サブ {SubCount} 件";

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>サブレベル1件。</summary>
public sealed class TagSubRow : ReorderableRow
{
    public required string Name { get; init; }

    public required string Top { get; init; }

    public string? Memo { get; init; }

    public required int ItemCount { get; init; }

    public string MemoText => string.IsNullOrEmpty(Memo) ? "メモなし" : Memo;

    public bool HasMemo => !string.IsNullOrEmpty(Memo);

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsUsed => ItemCount > 0;

    /// <summary>寄せ先の候補。自分自身は外す（自分に改名しても何も起きない）。</summary>
    public IReadOnlyList<string> OtherNames { get; set; } = [];

    /// <summary>移動先のトップ候補。自分が属するトップは外す。</summary>
    public IReadOnlyList<string> MoveTargets { get; set; } = [];

    public RelayCommand? ShowItemsCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? MoveCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }
}

/// <summary>マスタに無いのにitemが参照している名前。要確認は知らせるだけで、直せるのはここ。</summary>
public sealed class OrphanTagRow : ViewModelBase
{
    public required string Top { get; init; }

    /// <summary>null ならトップレベル、入っていればその配下のサブ。</summary>
    public string? Sub { get; init; }

    public required int ItemCount { get; init; }

    public bool IsSub => Sub is not null;

    public string Name => Sub ?? Top;

    /// <summary>サブは、どのトップの配下なのかが分からないと直しようがない。</summary>
    public string DisplayName => IsSub ? $"{Top}／{Sub}" : Top;

    public string KindText => IsSub ? "サブレベル" : "トップレベル";

    public string ItemCountText => $"{ItemCount} 件のitemが参照";

    public string MergePlaceholder => IsSub
        ? $"「{Top}」の既存サブへ寄せる"
        : "既存の分類へ寄せる";

    /// <summary>寄せ先の候補。トップならトップ一覧、サブなら同じトップの既存サブ。</summary>
    public IReadOnlyList<string> MergeCandidates { get; set; } = [];

    public RelayCommand? AddToMasterCommand { get; set; }

    public RelayCommand? MergeCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// タグの管理画面。appTagマスタ（トップ／サブの2階層）を編集する。
///
/// item側は名前で参照しているので、改名も削除も全itemの書き換えを伴う。
/// 戻せない操作なので、実行前に「何件が書き換わるか」を必ず数えて見せる。
/// マスタに無い名前をitemが参照したままの状態もここに出す。
/// 要確認はそれを知らせるだけで、直せる場所はこの画面しかないため。
/// </summary>
public sealed class TagManageViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    private List<TagTopRow> _allTops = [];
    private TagTopRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private string _statusText = string.Empty;
    private bool _isBusy;

    public TagManageViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        AddTopCommand = new RelayCommand(parameter => _ = AddTopAsync(parameter as string));
        AddSubCommand = new RelayCommand(parameter => _ = AddSubAsync(parameter as string), _ => Selected is not null);
        RenameTopCommand = new RelayCommand(parameter => _ = RenameTopAsync(parameter as string), _ => Selected is not null);
        DeleteTopCommand = new RelayCommand(() => _ = DeleteTopAsync(), () => Selected is not null);
        SaveMemoCommand = new RelayCommand(() => _ = SaveMemoAsync(), () => Selected is not null && MemoChanged);
        RefreshCommand = new RelayCommand(() => _ = ReloadAsync());
        ShowItemsCommand = new RelayCommand(
            () => _main.ShowItemsWithTag(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        _ = ReloadAsync();
    }

    public ObservableCollection<TagTopRow> Tops { get; } = [];

    public ObservableCollection<TagSubRow> Subs { get; } = [];

    public ObservableCollection<OrphanTagRow> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherTopNames { get; } = [];

    /// <summary>
    /// マスタにある全トップレベル。マスタに無い分類の寄せ先はこちらを候補にする。
    /// 右側で何を選んでいるかとは無関係なので、<see cref="OtherTopNames"/> は使えない。
    /// </summary>
    public ObservableCollection<string> AllTopNames { get; } = [];

    /// <summary>サブレベルの改名の寄せ先候補。同じトップの中だけを候補にする。</summary>
    public ObservableCollection<string> SubNames { get; } = [];

    public RelayCommand AddTopCommand { get; }

    public RelayCommand AddSubCommand { get; }

    public RelayCommand RenameTopCommand { get; }

    public RelayCommand DeleteTopCommand { get; }

    public RelayCommand SaveMemoCommand { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>この分類が付いているitemを検索で見せる。消す・統合するの判断は中身を見ないとできない。</summary>
    public RelayCommand ShowItemsCommand { get; }

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public TagTopRow? Selected
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
            RebuildSubs();
            RebuildOtherNames();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどのitemにも付いていません"
            : $"{Selected.ItemCount} 件のitemに付いています";

    /// <summary>改名すると何件が書き換わるか。押す前に見えていないと判断できない。</summary>
    public string RenameImpactText => Selected is null || Selected.ItemCount == 0
        ? "既にある名前を選ぶと統合します。"
        : $"既にある名前を選ぶと統合します。{Selected.ItemCount} 件のitemを書き換えます。";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                RebuildTops();
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

    public int TopCount => _allTops.Count;

    public string HeaderText => $"トップレベル {TopCount} 件";

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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            var master = _services.Store.AppTags.Load();
            var usage = await _services.AppTags.LoadUsageAsync();
            var orphans = await _services.AppTags.LoadOrphansAsync();

            RunOnUiThread(() =>
            {
                var counts = usage.ToDictionary(entry => entry.Top, StringComparer.CurrentCultureIgnoreCase);
                var keep = Selected?.Name;

                _allTops = master.Tops.Select(top => new TagTopRow
                {
                    Name = top.Name,
                    Memo = top.Memo,
                    SubCount = top.Subs.Count,
                    ItemCount = counts.TryGetValue(top.Name, out var entry) ? entry.ItemCount : 0,
                }).ToList();

                _subCounts = counts;

                AllTopNames.Clear();
                foreach (var top in _allTops)
                {
                    AllTopNames.Add(top.Name);
                }

                Orphans.Clear();
                foreach (var orphan in orphans)
                {
                    Orphans.Add(CreateOrphanRow(orphan));
                }

                RebuildTops();
                OnPropertyChanged(nameof(TopCount));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(HasOrphans));

                // 選び直す。改名した直後は名前が変わっているので、無ければ先頭に落とす
                Selected = _allTops.FirstOrDefault(row => row.Name == keep) ?? Tops.FirstOrDefault();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyDictionary<string, AppTagUsage> _subCounts =
        new Dictionary<string, AppTagUsage>(StringComparer.CurrentCultureIgnoreCase);

    private void RebuildTops()
    {
        var filter = _filterText.Trim();

        Tops.Clear();
        foreach (var row in _allTops.Where(row => filter.Length == 0
            || row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Tops.Add(row);
        }
    }

    public bool HasSubs => Subs.Count > 0;

    private void RebuildSubs()
    {
        Subs.Clear();
        SubNames.Clear();
        if (Selected is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        var master = _services.Store.AppTags.Load();
        var top = master.Tops.FirstOrDefault(entry =>
            string.Equals(entry.Name, Selected.Name, StringComparison.CurrentCultureIgnoreCase));

        if (top is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        _subCounts.TryGetValue(Selected.Name, out var usage);

        foreach (var sub in top.Subs)
        {
            var row = new TagSubRow
            {
                Name = sub.Name,
                Top = top.Name,
                Memo = sub.Memo,
                ItemCount = usage?.SubCounts.GetValueOrDefault(sub.Name) ?? 0,
            };

            row.RenameCommand = new RelayCommand(parameter => _ = RenameSubAsync(row, parameter as string));
            row.DeleteCommand = new RelayCommand(() => _ = DeleteSubAsync(row));
            row.ShowItemsCommand = new RelayCommand(
                () => _main.ShowItemsWithTag(row.Top, row.Name),
                () => row.IsUsed);
            row.MoveCommand = new RelayCommand(() => _ = MoveSubToTopAsync(row), () => _allTops.Count > 1);
            row.MoveTargets = _allTops
                .Where(entry => !string.Equals(entry.Name, top.Name, StringComparison.CurrentCultureIgnoreCase))
                .Select(entry => entry.Name)
                .ToList();
            Subs.Add(row);
            SubNames.Add(row.Name);
        }

        // 自分以外を寄せ先の候補にする。自分に改名しても何も起きないので出さない
        foreach (var row in Subs)
        {
            row.OtherNames = SubNames.Where(name => name != row.Name).ToList();
        }

        OnPropertyChanged(nameof(HasSubs));
    }

    /// <summary>マスタに載っているサブレベル名。寄せ先の候補に使う。</summary>
    private IReadOnlyList<string> SubNamesOf(string top)
        => _services.Store.AppTags.Load().Tops
            .FirstOrDefault(entry => string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase))
            ?.Subs.Select(sub => sub.Name).ToList()
            ?? [];

    private void RebuildOtherNames()
    {
        OtherTopNames.Clear();
        foreach (var name in _allTops
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherTopNames.Add(name);
        }
    }

    private OrphanTagRow CreateOrphanRow(OrphanAppTag orphan)
    {
        var row = new OrphanTagRow
        {
            Top = orphan.Top,
            Sub = orphan.Sub,
            ItemCount = orphan.ItemCount,

            // サブの寄せ先は同じトップの中だけ。別のトップのサブへは寄せられない
            MergeCandidates = orphan.Sub is null
                ? _allTops.Select(top => top.Name).ToList()
                : SubNamesOf(orphan.Top),
        };

        row.AddToMasterCommand = new RelayCommand(() => _ = AddOrphanToMasterAsync(row));
        row.MergeCommand = new RelayCommand(parameter => _ = MergeOrphanAsync(row, parameter as string));
        row.RemoveCommand = new RelayCommand(() => _ = RemoveOrphanAsync(row));

        return row;
    }

    private async Task AddTopAsync(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddAppTag(trimmed));
        StatusText = $"「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
    }

    private async Task AddSubAsync(string? name)
    {
        var trimmed = name?.Trim();
        if (Selected is null || string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddAppTag(Selected.Name, trimmed));
        StatusText = $"「{Selected.Name}」に「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。どちらも戻せないので、
    /// 何件のitemが書き換わるかを出してから確認を取る。
    /// </summary>
    private async Task RenameTopAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var merging = _allTops.Any(row =>
            string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));

        var message = merging
            ? $"「{Selected.Name}」を「{target}」に統合します。\n\n"
                + $"{Selected.ItemCount} 件のitemを書き換えます。サブレベルは「{target}」側へまとめます。\n"
                + MemoNotice(Selected.Memo, target)
                + "この操作は元に戻せません。"
            : $"「{Selected.Name}」を「{target}」に変更します。\n\n"
                + $"{Selected.ItemCount} 件のitemを書き換えます。";

        if (!Confirm(message, merging ? "分類を統合する" : "名前を変更する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameAppTag(Selected.Name, null, target));
        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.WasMerged
                ? $"「{target}」に統合しました（{rewritten.Result.ItemsUpdated} 件のitemを書き換え）。"
                : $"「{target}」に変更しました（{rewritten.Result.ItemsUpdated} 件のitemを書き換え）。";
        }

        var keep = target;
        await ReloadAsync();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, keep, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、付けていたitemからも外す。
    /// appTagが空になるitemは編集の対象に戻るので、その件数も先に出す。
    /// </summary>
    private async Task DeleteTopAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどのitemにも付いていないので、影響はありません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件のitemからこの分類が外れます（サブレベルも一緒に外れます）。\n"
                + "この操作は元に戻せません。";

        if (!Confirm(message, "分類を削除する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteAppTag(Selected.Name));
        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.ItemsLeftUntagged > 0
                ? $"削除しました（{rewritten.Result.ItemsUpdated} 件のitemから外し、"
                    + $"うち {rewritten.Result.ItemsLeftUntagged} 件はappTagが空になったので編集の対象に戻ります）。"
                : $"削除しました（{rewritten.Result.ItemsUpdated} 件のitemから外しました）。";
        }

        Selected = null;
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private async Task RenameSubAsync(TagSubRow row, string? newName)
    {
        var target = newName?.Trim();
        if (string.IsNullOrEmpty(target)
            || string.Equals(target, row.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var merging = Subs.Any(entry => string.Equals(entry.Name, target, StringComparison.CurrentCultureIgnoreCase));
        var message = merging
            ? $"「{row.Top}」の「{row.Name}」を「{target}」に統合します。\n\n"
                + $"{row.ItemCount} 件のitemを書き換えます。\n"
                + MemoNotice(row.Memo, target)
            : $"「{row.Top}」の「{row.Name}」を「{target}」に変更します。\n\n"
                + $"{row.ItemCount} 件のitemを書き換えます。";

        if (!Confirm(message, merging ? "サブレベルを統合する" : "サブレベルの名前を変更する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameAppTag(row.Top, row.Name, target));
        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = $"「{target}」に変更しました（{rewritten.Result.ItemsUpdated} 件のitemを書き換え）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private async Task DeleteSubAsync(TagSubRow row)
    {
        var message = row.ItemCount == 0
            ? $"「{row.Top}」から「{row.Name}」を削除します。\n\nどのitemにも付いていないので、影響はありません。"
            : $"「{row.Top}」から「{row.Name}」を削除します。\n\n"
                + $"{row.ItemCount} 件のitemからこのサブレベルが外れます。「{row.Top}」自体は付いたままです。";

        if (!Confirm(message, "サブレベルを削除する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteAppTag(row.Top, row.Name));
        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = $"「{row.Name}」を削除しました（{rewritten.Result.ItemsUpdated} 件のitemから外しました）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// トップレベルを並べ替える。並びは検索の絞り込みにも編集の候補にもそのまま出るので、
    /// 「よく使う順」に置けること自体が機能になる。itemは名前で参照しているので触らない。
    ///
    /// 絞り込み中は見えている分しか動かせないため、隠れている行の位置は保つ。
    /// </summary>
    public async Task MoveTopAsync(TagTopRow moved, TagTopRow target, bool after)
    {
        var order = _allTops.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderAppTags(order));
        await ReloadAsync();

        // 並びは検索の絞り込みにもそのまま出るので、そちらも作り直す
        _main.RefreshMasters();
    }

    public async Task MoveSubAsync(TagSubRow moved, TagSubRow target, bool after)
    {
        var order = Subs.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderAppTags(order, moved.Top));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>抜いてから差し込む。落とす先の index は抜いた後で数え直す。</summary>
    private static bool Reorder(List<string> order, string moved, string target, bool after)
    {
        var from = order.IndexOf(moved);
        if (from < 0 || moved == target)
        {
            return false;
        }

        order.RemoveAt(from);

        var at = order.IndexOf(target);
        if (at < 0)
        {
            order.Insert(from, moved);
            return false;
        }

        var to = after ? at + 1 : at;
        if (to == from)
        {
            order.Insert(from, moved);
            return false;
        }

        order.Insert(to, moved);
        return true;
    }

    /// <summary>
    /// サブレベルを別のトップへ移す。削除して付け直すとitemの割当てが失われるので、
    /// 専用の操作にしてある。
    ///
    /// 滅多に使わない操作なので入力欄は常設せず、ここでダイアログを開いて
    /// 移動先と「サブが無くなった元のトップをどうするか」をまとめて決めてもらう。
    /// </summary>
    private async Task MoveSubToTopAsync(TagSubRow row)
    {
        var dialog = new Views.MoveSubDialog(
            new MoveSubDialogViewModel(_services.AppTags, row.Top, row.Name, row.MoveTargets));

        if (dialog.ShowDialog() != true
            || dialog.DataContext is not MoveSubDialogViewModel { Target: { } to })
        {
            return;
        }

        var drop = ((MoveSubDialogViewModel)dialog.DataContext).DropEmptySourceTop;

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.MoveAppTagSub(row.Top, row.Name, to, drop));

        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            var parts = new List<string> { $"{rewritten.Result.ItemsUpdated} 件のitemを書き換え" };

            if (rewritten.Result.ItemsGainedTop > 0)
            {
                parts.Add($"うち {rewritten.Result.ItemsGainedTop} 件に「{to}」が新しく付きました");
            }

            if (rewritten.Result.ItemsSourceTopRemoved > 0)
            {
                parts.Add($"{rewritten.Result.ItemsSourceTopRemoved} 件から「{row.Top}」を外しました");
            }

            StatusText = $"「{to}」の下へ移しました（{string.Join("、", parts)}）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private async Task SaveMemoAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.SetAppTagMemo(Selected.Name, null, MemoDraft));
        StatusText = "メモを保存しました。";
        await ReloadAsync();
    }

    /// <summary>参照だけ残っている名前を、そのままマスタへ作る。名前が正しかった場合の直し方。</summary>
    private async Task AddOrphanToMasterAsync(OrphanTagRow row)
    {
        await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.AddAppTag(row.Top, row.Sub)
            : new UiCommand.AddAppTag(row.Top));

        StatusText = $"「{row.DisplayName}」をマスタに追加しました。{row.ItemCount} 件のitemが絞り込みに出るようになります。";
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>名前が変わっていた場合の直し方。既存の分類へ寄せる。</summary>
    private async Task MergeOrphanAsync(OrphanTagRow row, string? target)
    {
        var name = target?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!Confirm(
            $"「{row.DisplayName}」を「{name}」に寄せます。\n\n{row.ItemCount} 件のitemを書き換えます。",
            "分類を寄せる"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.RenameAppTag(row.Top, row.Sub, name)
            : new UiCommand.RenameAppTag(row.Top, null, name));

        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = $"「{name}」に寄せました（{rewritten.Result.ItemsUpdated} 件のitemを書き換え）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>もう使わない名前だった場合の直し方。itemから外す。</summary>
    private async Task RemoveOrphanAsync(OrphanTagRow row)
    {
        var notice = row.IsSub
            ? $"「{row.Top}」自体は付いたままです。"
            : string.Empty;

        if (!Confirm(
            $"「{row.DisplayName}」を {row.ItemCount} 件のitemから外します。\n\n{notice}この操作は元に戻せません。",
            "参照を外す"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.DeleteAppTag(row.Top, row.Sub)
            : new UiCommand.DeleteAppTag(row.Top));
        if (result is CommandResult.AppTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.ItemsLeftUntagged > 0
                ? $"「{row.DisplayName}」を外しました（{rewritten.Result.ItemsUpdated} 件のitemから外し、"
                    + $"うち {rewritten.Result.ItemsLeftUntagged} 件はappTagが空になったので編集の対象に戻ります）。"
                : $"「{row.DisplayName}」を外しました（{rewritten.Result.ItemsUpdated} 件のitemから外しました）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 統合でメモがどうなるかを一行で伝える。黙って寄せ先に書き足すと、
    /// 後から読んだときに出所が分からないメモが増えることになる。
    /// </summary>
    private static string MemoNotice(string? memo, string target)
        => string.IsNullOrWhiteSpace(memo)
            ? string.Empty
            : $"メモは「{target}」側に「「元の名前」から統合：…」として書き足します。\n";

    /// <summary>既定はキャンセル。Enterを押しただけで消えないようにする。</summary>
    private static bool Confirm(string message, string caption)
        => MessageBox.Show(
            message,
            caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
