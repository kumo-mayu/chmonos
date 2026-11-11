using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>サブ画面の窓を前に出す・閉じる口。アプリでは窓（<see cref="Views.FilePoolWindow"/>）、試験では控えるだけの作り物。</summary>
internal interface ISubWindowHandle
{
    void BringToFront();

    void Close();
}

/// <summary>
/// 選んだ商品の間でファイルを割り振るサブ画面（plan 30・ユーザ案と判断 2026-11-11「最初からサブ画面で作って良いと思う」）。
///
/// 左は選んだ商品、右は選んだ商品に付いているファイルの溜まり（<see cref="FilePool"/>）。左で付ける先を1つ選び、
/// 右でチェックを入れて「付ける」。付けても元の商品からは外さない（<see cref="OtherHolders.Share"/>・shared-files.md）。
/// 外すのは左の商品の下のファイルの行から（<see cref="UiCommand.DetachFile"/>）。
///
/// **主の窓を塞がない別の窓**（決め事は docs/spec/ui-subwindows.md）。開いている間にほかの画面・取り込み・API が
/// 選んだ商品を書き換えたら、置き場の知らせ（<c>ItemRepository.Written</c>）で読み直す。
/// 自分の付け外しは主の窓へ知らせる（検索の写し・開いている商品ページ・ナビの数）。
/// </summary>
public sealed class FilePoolViewModel : ViewModelBase
{
    /// <summary>場所の記録が無いファイルの名前の代わり。</summary>
    internal const string UnnamedFileText = "場所の記録が無いファイル";

    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    /// <summary>選んだ商品の ID（選んだ順）。消えた商品は外す。知らせは裏のスレッドから来るので、差し替える形で持つ。</summary>
    private volatile IReadOnlyList<string> _ids;

    private IReadOnlyList<ItemRecord> _items = [];

    /// <summary>
    /// 窓を開いてから一度でも溜まりに出たファイル（ハッシュ → 最後に見た姿と、最後に持っていた商品）。
    /// 選んだ商品の最後の1件から外すと溜まりから消えるので、ここに残して重複回数 0 で出し、別の商品へ付け直せるようにする
    /// （開く前から誰にも付いていない物は出さない：前に外した物まで並ぶと、割り振る物が埋もれる）
    /// </summary>
    private readonly Dictionary<string, (PooledFile File, string Source)> _seen = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanded = new(StringComparer.CurrentCultureIgnoreCase);

    private FilePoolItemRow? _selected;
    private string _itemFilterText = string.Empty;
    private string _fileFilterText = string.Empty;
    private int _threshold = 1;
    private HolderCountFilter _filter = HolderCountFilter.AtMost;
    private string _resultText = string.Empty;
    private string _removedText = string.Empty;
    private bool _isBusy;
    private int _loadVersion;
    private int _reloadQueued;
    private bool _closed;

    internal FilePoolViewModel(AppServiceContainer services, MainViewModel main, IReadOnlyList<string> itemIds)
    {
        _services = services;
        _main = main;
        _ids = itemIds.Distinct(StringComparer.Ordinal).ToList();
        _services.Store.Items.Written += OnItemWritten;

        AttachCommand = new RelayCommand(() => AttachAsync().Forget(), () => CanAttach);
        DetachCommand = new RelayCommand(parameter => DetachAsync(parameter as FilePoolHeldFile).Forget());
        OpenItemCommand = new RelayCommand(parameter => OpenItemAsync(parameter as FilePoolItemRow).Forget());
        ExpandAllItemsCommand = new RelayCommand(() => SetAllItemsExpanded(true));
        CollapseAllItemsCommand = new RelayCommand(() => SetAllItemsExpanded(false));
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        ClearChecksCommand = new RelayCommand(ClearChecks);
        ClearItemFilterCommand = new RelayCommand(() => ItemFilterText = string.Empty);
        ClearFileFilterCommand = new RelayCommand(() => FileFilterText = string.Empty);
    }

    /// <summary>窓が閉じた（主の窓が1つだけ持つ控えを外すため）。</summary>
    internal event Action? Closed;

    /// <summary>窓を作る口。試験は窓を作らずに作り物を返す（窓を出すと、画面の無い試験では止まる）。</summary>
    internal static Func<FilePoolViewModel, ISubWindowHandle>? OpenIntercept { get; set; }

    internal ISubWindowHandle? Window { get; private set; }

    public string Title => "ファイルの割り振り";

    public ObservableCollection<FilePoolItemRow> Items { get; } = [];

    /// <summary>
    /// 左に出す商品（<see cref="Items"/> を探す欄で絞った物）。<see cref="Items"/> は選んだ商品の全部のまま：
    /// 見出しの数・n の選択肢・右の溜まりは、左の絞りで変えない（隠れた商品のファイルまで溜まりから消えると、割り振る物を見落とす）
    /// </summary>
    public ObservableCollection<FilePoolItemRow> ShownItems { get; } = [];

    public ObservableCollection<FilePoolGroupRow> Groups { get; } = [];

    public string ItemsHeading => $"選んだ商品（{Items.Count}）";

    /// <summary>左の探す欄。商品名で絞る（照らし方はほかの探す欄と同じ：大文字小文字・かなの種類・全角半角を区別しない。空白で区切った語の全部）。</summary>
    public string ItemFilterText
    {
        get => _itemFilterText;
        set
        {
            if (SetField(ref _itemFilterText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasItemFilterText));
                ApplyItemFilter();
            }
        }
    }

    public bool HasItemFilterText => ItemFilterText.Length > 0;

    /// <summary>左の探す欄で1件も当たらないときの1行。</summary>
    public string ItemsEmptyText { get; private set; } = string.Empty;

    public bool HasItemsEmptyText => ItemsEmptyText.Length > 0;

    /// <summary>
    /// 右の探す欄。ファイル名と組の名前で絞る（語ごとに、どちらかに入っていればよい）。付いている商品の数の絞りと重ねて効く。
    /// 組は数で絞った後に作り、組の中の行を文字で絞る（組の名前で当たれば組の全部が残る。ファイル名で1つだけ残った組は、見出しの無い1行になる）
    /// </summary>
    public string FileFilterText
    {
        get => _fileFilterText;
        set
        {
            if (SetField(ref _fileFilterText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasFileFilterText));
                RebuildPool();
            }
        }
    }

    public bool HasFileFilterText => FileFilterText.Length > 0;

    public RelayCommand ClearItemFilterCommand { get; }

    public RelayCommand ClearFileFilterCommand { get; }

    /// <summary>付ける先。左で押した商品。</summary>
    public FilePoolItemRow? SelectedItem
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            _selected = value;
            if (value is not null)
            {
                value.IsSelected = true;
            }

            OnPropertyChanged();
            RebuildPool();
        }
    }

    /// <summary>重複回数の n（既定 1。ユーザ判断 2026-11-11「基本は n 以下を表示」）。</summary>
    public int Threshold
    {
        get => _threshold;
        set
        {
            if (SetField(ref _threshold, Math.Max(0, value)))
            {
                RebuildPool();
            }
        }
    }

    /// <summary>n の選択肢（0 から選んだ商品の数まで。0 はどの商品にも付いていない物）。</summary>
    /// <remarks>
    /// 商品の数が変わったときだけ作り直す。読み直しのたびに新しい一覧を渡すと、選ぶ欄が選んでいた値を外して空に見えることがある
    /// </remarks>
    public IReadOnlyList<int> ThresholdChoices { get; private set; } = [0, 1];

    public HolderCountFilter Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                RebuildPool();
            }
        }
    }

    public IReadOnlyList<FilterChoice> FilterChoices { get; } =
    [
        new(HolderCountFilter.AtMost, "以下"),
        new(HolderCountFilter.AtLeast, "以上"),
        new(HolderCountFilter.Exactly, "のみ"),
    ];

    public sealed record FilterChoice(HolderCountFilter Value, string Text);

    /// <summary>溜まりの数（絞る前）と出している数。</summary>
    public string PoolCountText { get; private set; } = string.Empty;

    /// <summary>右が空のときの1行（溜まりが空・絞りで全部隠れた）。</summary>
    public string EmptyText { get; private set; } = string.Empty;

    public bool HasEmptyText => EmptyText.Length > 0;

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (SetField(ref _resultText, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => ResultText.Length > 0;

    /// <summary>選んだ商品が消えたときの1行（左の一覧の上）。</summary>
    public string RemovedText
    {
        get => _removedText;
        private set
        {
            if (SetField(ref _removedText, value))
            {
                OnPropertyChanged(nameof(HasRemoved));
            }
        }
    }

    public bool HasRemoved => RemovedText.Length > 0;

    public int CheckedCount => Groups.SelectMany(group => group.Files).Count(file => file.IsChecked);

    public bool CanAttach => !_isBusy && SelectedItem is not null && CheckedCount > 0;

    public string AttachText => CheckedCount > 0 ? $"{CheckedCount} 個を付ける" : "付ける";

    /// <summary>付けるが押せない理由・付ける先（ボタンの左の1行）。</summary>
    public string AttachHint => SelectedItem is null
        ? "左の一覧で、付ける先の商品を選んでください。"
        : CheckedCount == 0
            ? "付けるファイルにチェックを入れてください。"
            : $"付ける先：{SelectedItem.Name}";

    public RelayCommand AttachCommand { get; }

    public RelayCommand DetachCommand { get; }

    public RelayCommand OpenItemCommand { get; }

    /// <summary>左の商品の下のファイルをすべて広げる（右の組の「すべて広げる」とは別の物）。</summary>
    public RelayCommand ExpandAllItemsCommand { get; }

    public RelayCommand CollapseAllItemsCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand ClearChecksCommand { get; }

    /// <summary>窓を開く（主の窓から1回だけ呼ぶ）。</summary>
    internal void Open()
    {
        Window = OpenIntercept is { } intercept ? intercept(this) : Views.FilePoolWindow.Open(this);
        ReloadAsync().Forget();
    }

    /// <summary>
    /// 2回目に開こうとした：選んだ商品を入れ替えて前に出す（窓は1つだけ）。同じ選び方なら入れ替えず、
    /// 付ける先・チェック・開いた組をそのまま残す（帯をもう一度押しただけで手元の作業を消さない）。
    /// </summary>
    internal void Reopen(IReadOnlyList<string> itemIds)
    {
        // 並びは選んだ順なので、同じ商品を選び直して順だけ違っても同じ選び方とみなす（チェックを消さない）
        var ids = itemIds.Distinct(StringComparer.Ordinal).ToList();
        if (!new HashSet<string>(ids, StringComparer.Ordinal).SetEquals(_ids))
        {
            _ids = ids;
            _seen.Clear();
            _checked.Clear();
            RemovedText = string.Empty;
            ResultText = string.Empty;
            ReloadAsync().Forget();
        }

        Window?.BringToFront();
    }

    /// <summary>窓が閉じた（×・Esc・主の窓が閉じた）。置き場の知らせを外す（置き場はアプリの間生きている）。</summary>
    internal void NoteWindowClosed()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _services.Store.Items.Written -= OnItemWritten;
        Closed?.Invoke();
    }

    /// <summary>
    /// 商品が書かれた・消された（錠の中・裏のスレッドから来る）。選んだ商品なら、画面のスレッドで手が空いたときに1回だけ読み直す
    /// （取り込みは数百回鳴らすので、ここでは数えて積むだけ）。
    /// </summary>
    private void OnItemWritten(string itemId)
    {
        if (_closed || !_ids.Contains(itemId) || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        if (Interlocked.Exchange(ref _reloadQueued, 1) == 1)
        {
            return;
        }

        dispatcher.InvokeAsync(
            () =>
            {
                Interlocked.Exchange(ref _reloadQueued, 0);
                ReloadAsync().Forget();
            },
            DispatcherPriority.Background);
    }

    /// <summary>選んだ商品を記録から読み直して、左右を組み直す。消えた商品は一覧から外し、そう言う。</summary>
    internal async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var ids = _ids;
        var items = new List<ItemRecord>(ids.Count);
        var gone = new List<string>();
        foreach (var id in ids)
        {
            if (await _services.Store.Items.LoadAsync(id) is { } item)
            {
                items.Add(item);
            }
            else
            {
                gone.Add(id);
            }
        }

        // 読む間に次の読み直しが始まっていたら、こちらは古い
        if (version != _loadVersion || _closed)
        {
            return;
        }

        if (gone.Count > 0)
        {
            _ids = ids.Except(gone, StringComparer.Ordinal).ToList();
            RemovedText = $"削除された商品 {gone.Count} 件を一覧から外しました。";
        }

        _items = items;
        RebuildItems();
        RebuildPool();
    }

    private void RebuildItems()
    {
        var byId = Items.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var rows = new List<FilePoolItemRow>(_items.Count);
        foreach (var item in _items)
        {
            if (!byId.TryGetValue(item.Id, out var row))
            {
                row = new FilePoolItemRow(item.Id, item.DisplayName);
            }

            row.Update(item);
            rows.Add(row);
        }

        if (!rows.Select(row => row.Id).SequenceEqual(Items.Select(row => row.Id)))
        {
            Items.Clear();
            foreach (var row in rows)
            {
                Items.Add(row);
            }
        }

        if (_selected is { } selected && !rows.Contains(selected))
        {
            _selected = null;
            OnPropertyChanged(nameof(SelectedItem));
        }

        var top = Math.Max(Items.Count, 1);
        if (ThresholdChoices.Count != top + 1)
        {
            ThresholdChoices = Enumerable.Range(0, top + 1).ToList();
            OnPropertyChanged(nameof(ThresholdChoices));
        }

        if (_threshold > top)
        {
            _threshold = top;
            OnPropertyChanged(nameof(Threshold));
        }

        OnPropertyChanged(nameof(ItemsHeading));
        ApplyItemFilter();
    }

    /// <summary>
    /// 左の探す欄で <see cref="ShownItems"/> を寄せる（差分で：作り直すと、広げた行の部品と読んだ絵を作り直す）。
    /// 付ける先は隠れても外さない（探す語を変えただけで付ける先が消えると、付けるボタンが黙って押せなくなる）
    /// </summary>
    private void ApplyItemFilter()
    {
        var words = ModificationSearchText.Words(_itemFilterText);
        var shown = Items.Where(row => words.All(word => ModificationSearchText.Contains(row.Name, word))).ToList();
        CollectionSync.Apply(ShownItems, shown);
        ItemsEmptyText = Items.Count > 0 && shown.Count == 0 ? "当たる商品はありません。探す文字を変えてください。" : string.Empty;
        OnPropertyChanged(nameof(ItemsEmptyText));
        OnPropertyChanged(nameof(HasItemsEmptyText));
    }

    /// <summary>
    /// 溜まり・絞り・組を作り直す。チェックはハッシュ（行の知らせで控えを保つ）、開いた組は名前で覚えて戻す
    /// （読み直しのたびに閉じたり外れたりすると、ほかの画面で付け外ししただけで手元の作業が消える）。
    /// </summary>
    private void RebuildPool()
    {
        foreach (var group in Groups)
        {
            if (group.IsExpanded)
            {
                _expanded.Add(group.Name);
            }
            else
            {
                _expanded.Remove(group.Name);
            }
        }

        var selectedIds = new HashSet<string>(_items.Select(item => item.Id), StringComparer.Ordinal);
        var pool = FilePool.Of(_items).ToList();
        var present = new HashSet<string>(pool.Select(file => file.Hash), StringComparer.OrdinalIgnoreCase);
        foreach (var file in pool)
        {
            _seen[file.Hash] = (file, file.HolderIds[0]);
        }

        // 選んだ商品の最後の1件から外した物は、重複回数 0 で残す。写す元の商品が一覧から消えていれば落とす
        foreach (var (hash, (file, source)) in _seen.ToList())
        {
            if (present.Contains(hash))
            {
                continue;
            }

            if (selectedIds.Contains(source))
            {
                pool.Add(file with { HolderIds = [] });
            }
            else
            {
                _seen.Remove(hash);
            }
        }

        var names = _items.ToDictionary(item => item.Id, item => item.DisplayName, StringComparer.Ordinal);
        // 溜まりの並び（名前の順・名前の無い物は後ろ）に、0 で残した物も混ぜて並べ直す
        var shown = pool
            .Where(file => FilePoolGroups.Matches(file.HolderCount, _threshold, _filter))
            .OrderBy(file => file.FileName is null)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(file => file.Hash, StringComparer.Ordinal)
            .ToList();
        var target = SelectedItem?.Id;
        var words = ModificationSearchText.Words(_fileFilterText);
        var matched = 0;

        Groups.Clear();
        foreach (var group in FilePoolGroups.Of(shown))
        {
            var groupName = group.Name.Length > 0 ? group.Name : UnnamedFileText;
            var rows = group.Files
                .Where(file => words.All(word => ModificationSearchText.Contains(file.FileName ?? UnnamedFileText, word)
                    || ModificationSearchText.Contains(groupName, word)))
                .Select(file =>
                {
                    var row = new FilePoolFileRow(
                        file,
                        file.HolderIds.Count > 0 ? file.HolderIds[0] : _seen.GetValueOrDefault(file.Hash).Source,
                        file.FileName ?? UnnamedFileText,
                        target is not null && file.HolderIds.Contains(target, StringComparer.Ordinal),
                        string.Join("\n", file.HolderIds.Select(id => names.GetValueOrDefault(id, id))));
                    row.IsChecked = _checked.Contains(file.Hash);
                    row.PropertyChanged += OnFileRowChanged;
                    return row;
                })
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            matched += rows.Count;
            Groups.Add(new FilePoolGroupRow(group, rows, _expanded.Contains(groupName)));
        }

        PoolCountText = matched == pool.Count ? $"{pool.Count} 個" : $"{pool.Count} 個のうち {matched} 個を表示";
        EmptyText = pool.Count == 0 ? "選んだ商品に付いているファイルはありません。"
            : shown.Count == 0 ? "条件に合うファイルはありません。付いている商品の数を変えてください。"
            : matched == 0 ? "当たるファイルはありません。探す文字を変えてください。"
            : string.Empty;
        OnPropertyChanged(nameof(PoolCountText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasEmptyText));
        OnCheckedChanged();
    }

    private void OnFileRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FilePoolFileRow.IsChecked) && sender is FilePoolFileRow row)
        {
            if (row.IsChecked)
            {
                _checked.Add(row.Hash);
            }
            else
            {
                _checked.Remove(row.Hash);
            }

            OnCheckedChanged();
        }
    }

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(AttachText));
        OnPropertyChanged(nameof(AttachHint));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void SetAllItemsExpanded(bool expanded)
    {
        foreach (var row in Items)
        {
            row.IsExpanded = expanded;
        }
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var group in Groups.Where(group => !group.IsSingle))
        {
            group.IsExpanded = expanded;
        }
    }

    private void ClearChecks()
    {
        foreach (var file in Groups.SelectMany(group => group.Files))
        {
            file.IsChecked = false;
        }

        _checked.Clear();
    }

    /// <summary>
    /// サブ画面の商品を押した：付ける先に選び、メインの画面でその商品ページを開く
    /// （ユーザ指示 2026-11-11「人間がやったのと同じ経路（ページ遷移の履歴に登録する形）で開く」）。
    /// 開くのは今の記録（検索の写しではなく置き場から読む。付けた直後でも新しい姿で開く）。
    /// フォーカスはサブ画面に残す（続けて割り振る。メインの窓を前に出すと、サブ画面が後ろに隠れることがある）
    /// </summary>
    internal async Task OpenItemAsync(FilePoolItemRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedItem = row;
        if (await _services.Store.Items.LoadAsync(row.Id) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    /// <summary>
    /// チェックしたファイルを、付ける先の商品に付ける。窓は出さない：付けても元の商品から外さない（<see cref="OtherHolders.Share"/>）。
    /// 除外している物は解かずに付けない（除外の解除は人が商品ページで決める）。結果は付けるボタンの下の1行（ui-dialogs.md「どこに言うか」）。
    /// </summary>
    internal async Task AttachAsync()
    {
        if (!CanAttach || SelectedItem is not { } target)
        {
            return;
        }

        var files = Groups.SelectMany(group => group.Files).Where(file => file.IsChecked && file.SourceItemId is not null).ToList();
        _isBusy = true;
        OnCheckedChanged();
        var attached = 0;
        var failures = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var result = await _services.Commands.ExecuteAsync(
                    new UiCommand.AttachRecordedFile(target.Id, file.Hash, file.SourceItemId!, OtherHolders: OtherHolders.Share));
                if (result is CommandResult.FileAttached { Outcome: { } outcome })
                {
                    if (outcome.Result is FileAttachResult.Attached or FileAttachResult.AlreadyAttached)
                    {
                        attached++;
                        _checked.Remove(file.Hash);
                    }
                    else
                    {
                        failures.Add(FailureOf(outcome, file.FileName));
                    }
                }
                else
                {
                    failures.Add(result is CommandResult.Failed failed ? failed.Message : $"「{file.FileName}」を付けられませんでした。");
                }
            }
        }
        finally
        {
            _isBusy = false;
        }

        var lines = new List<string>();
        if (attached > 0)
        {
            lines.Add($"{attached} 個のファイルを「{target.Name}」に付けました。");
        }

        lines.AddRange(failures);
        ResultText = string.Join("\n", lines);
        _main.NoteItemsChangedBySubWindow([target.Id]);
        await ReloadAsync();
    }

    private static string FailureOf(FileAttachOutcome outcome, string fileName) => outcome.Result switch
    {
        FileAttachResult.Excluded => $"「{fileName}」は管理対象から除外しているので、付けませんでした。",
        FileAttachResult.RecordMissing => $"「{fileName}」の記録が見つからないので、付けませんでした。",
        FileAttachResult.ItemMissing => "付ける先の商品が見つかりませんでした。",
        _ => $"「{fileName}」を付けられませんでした。",
    };

    /// <summary>
    /// 左の商品の下のファイルを、その商品から外す（商品は消さない）。ほかの商品にも付いていれば聞かずに外す
    /// （この画面でまた付けられる）。どの商品にも付かなくなるときだけ、未確定に表示されることを窓で言う。
    /// </summary>
    internal async Task DetachAsync(FilePoolHeldFile? file)
    {
        if (file is null || _isBusy)
        {
            return;
        }

        // ほかに持つ商品は、選んでいない商品も含めて見る（未確定に出るかは全体で決まる）。検索の写しで見る（命令が錠の中でもう一度見る）
        var heldElsewhere = _main.Search.SnapshotItems().Any(other => other.Id != file.ItemId
            && other.Local.AttachedFiles.Any(held => string.Equals(held.Hash, file.Hash, StringComparison.OrdinalIgnoreCase)));
        if (!heldElsewhere)
        {
            var answer = Services.Notice.Show(
                // 未確定に出すのはファイルが手元に在るときだけ（DetachFileAsync）。画面のスレッドでディスクを見ないので言い分けない
                $"「{file.FileName}」を「{file.ItemName}」から外しますか？\n"
                + "どの商品にも付いていない状態になります。ファイルが手元にあれば未確定に表示されます。この画面でほかの商品に付け直せます。",
                "この商品から外す",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        _isBusy = true;
        OnCheckedChanged();
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.DetachFile(file.ItemId, file.Hash, DeleteItemWhenEmpty: false));
            ResultText = result is CommandResult.FileDetached
                ? $"「{file.FileName}」を「{file.ItemName}」から外しました。"
                : result is CommandResult.Failed failed ? failed.Message : $"「{file.FileName}」を外せませんでした。";
        }
        finally
        {
            _isBusy = false;
        }

        _main.NoteItemsChangedBySubWindow([file.ItemId]);
        await ReloadAsync();
    }
}
