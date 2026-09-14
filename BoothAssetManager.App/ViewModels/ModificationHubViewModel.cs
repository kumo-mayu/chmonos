using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 改変の画面。左で「Unityプロジェクト」「アバター」「改変」の見方を切り替えて探し、右に押したもののビューを出す。
/// </summary>
public sealed partial class ModificationHubViewModel : ViewModelBase
{
    /// <summary>最後に使った見方。ナビから開き直したときに同じ見方で始める（アプリを閉じるまで）。</summary>
    private static ModificationHubLevel s_lastLevel = ModificationHubLevel.Project;

    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;

    /// <summary>探すときは大文字小文字・かなの種類・全角半角を区別しない（「くうた」で「クウタ」に当てる）。</summary>
    private const CompareOptions Loose =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private readonly AppServiceContainer _services;
    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "modifications.list");
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    private ModificationHubLevel _level;
    private string _query = string.Empty;
    private object? _detail;
    private string _status = string.Empty;
    private bool _isLoading = true;
    private ModificationHubSelection? _pendingSelection;
    private bool _isRefreshing;
    private UnityTools _tools = UnityTools.Unknown;

    private IReadOnlyList<ModificationRecord> _records = [];
    private IReadOnlyList<UnityProjectCandidate> _projects = [];
    private Dictionary<string, AvatarSummary> _avatars = new(StringComparer.Ordinal);
    private Dictionary<string, ItemRecord> _items = new(StringComparer.Ordinal);

    public ModificationHubViewModel(
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails,
        ModificationHubLevel? level = null,
        ModificationHubSelection? selection = null)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;
        _level = level ?? s_lastLevel;
        _pendingSelection = selection;

        OpenVccCommand = new RelayCommand(OpenVcc);
        OpenProjectCommand = new RelayCommand(parameter => OpenProject(PathOf(parameter)));
        OpenProjectFolderCommand = new RelayCommand(parameter => Shell.Reveal(PathOf(parameter)));
        ShowProjectCommand = new RelayCommand(parameter => ShowProject(PathOf(parameter)));
        ShowModificationCommand = new RelayCommand(parameter =>
        {
            if (RecordOf(parameter) is { } record)
            {
                ShowModification(record);
            }
        });
        ShowAvatarCommand = new RelayCommand(parameter =>
        {
            if (AvatarIdOf(parameter) is { } id)
            {
                ShowAvatar(id);
            }
        });
        ShowMemberCommand = new RelayCommand(
            parameter => ShowMember(parameter as HubMemberRow),
            parameter => parameter is HubMemberRow);
        SelectInUnityCommand = new RelayCommand(
            parameter => SelectInUnityAsync(parameter as HubMemberRow ?? (parameter as HubItemDetail)?.Row).Forget(),
            parameter => parameter is HubMemberRow or HubItemDetail);
        OpenAvatarManageCommand = new RelayCommand(parameter =>
        {
            if (AvatarIdOf(parameter) is { } id)
            {
                _main.ShowAvatar(id);
            }
        });
        StartCreateCommand = new RelayCommand(parameter =>
        {
            if (AvatarIdOf(parameter) is { } id)
            {
                ShowAvatar(id);
                Status = "右の「新しい改変」に名前を入れて、「改変を作る」を押してください。";
            }
        });
        CreateModificationCommand = new RelayCommand(
            () => CreateModificationAsync().Forget(),
            () => Detail is HubAvatarDetail avatar && avatar.NameInput.Trim().Length > 0);
        OpenItemPageCommand = new RelayCommand(parameter => OpenItemPageAsync(parameter as string).Forget());

        LoadAsync().Forget();
    }

    // ---- 見方 ----

    public ModificationHubLevel Level
    {
        get => _level;
        private set
        {
            if (SetField(ref _level, value))
            {
                s_lastLevel = value;
                OnPropertyChanged(nameof(IsProjectLevel));
                OnPropertyChanged(nameof(IsAvatarLevel));
                OnPropertyChanged(nameof(IsModificationLevel));
                OnPropertyChanged(nameof(QueryPlaceholder));
                Rebuild();
            }
        }
    }

    public bool IsProjectLevel
    {
        get => Level == ModificationHubLevel.Project;
        set
        {
            if (value)
            {
                Level = ModificationHubLevel.Project;
            }
        }
    }

    public bool IsAvatarLevel
    {
        get => Level == ModificationHubLevel.Avatar;
        set
        {
            if (value)
            {
                Level = ModificationHubLevel.Avatar;
            }
        }
    }

    public bool IsModificationLevel
    {
        get => Level == ModificationHubLevel.Modification;
        set
        {
            if (value)
            {
                Level = ModificationHubLevel.Modification;
            }
        }
    }

    // ---- 探す ----

    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value))
            {
                OnPropertyChanged(nameof(HasQuery));
                Rebuild();
            }
        }
    }

    public bool HasQuery => Query.Length > 0;

    public string QueryPlaceholder => Level switch
    {
        ModificationHubLevel.Project => "プロジェクト名・改変名から探す",
        ModificationHubLevel.Avatar => "アバター名・改変名から探す",
        _ => "改変名・アバター名・プロジェクト名から探す",
    };

    /// <summary>左の一覧。見方によって、見出しがプロジェクト・アバター・改変になる。</summary>
    public ObservableCollection<object> Groups { get; } = [];

    public bool IsEmpty => Groups.Count == 0;

    /// <summary>空のときに次にやることを書く。</summary>
    public string EmptyText => _isLoading
        ? "読み込んでいます…"
        : Query.Trim().Length > 0
            ? $"「{Query.Trim()}」に当てはまるものはありません。"
            : Level switch
            {
                ModificationHubLevel.Project => ProjectEmptyText(Tools),
                ModificationHubLevel.Avatar =>
                    "持っているアバターがまだありません。アバターの管理で検出するか、アバターの商品を取り込むと出ます。",
                _ => "改変はまだありません。「アバター」の見方で、アバターの行の「改変を作る」から作れます。",
            };

    // ---- 右側 ----

    /// <summary>右側に出しているもの。プロジェクト・アバター・改変・使ったもののどれか。</summary>
    public object? Detail
    {
        get => _detail;
        private set
        {
            if (SetField(ref _detail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasDetail => Detail is not null;

    /// <summary>右側に出しているものを、開き直せる形で。</summary>
    public ModificationHubSelection? Selection => Detail switch
    {
        HubProjectDetail project => new(ModificationHubSelectionKind.Project, project.Path),
        HubAvatarDetail avatar => new(ModificationHubSelectionKind.Avatar, avatar.AvatarItemId),
        ModificationViewModel modification => new(ModificationHubSelectionKind.Modification, modification.Record.Id),
        HubItemDetail item => new(ModificationHubSelectionKind.Member, item.Row.Record.Id, item.Row.Index),
        _ => null,
    };

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

    /// <summary>
    /// Unityプロジェクトの見方で一覧が空のとき。**Hub・VCC が見つからないのか、あるがプロジェクトが無いのかを言い分ける**
    /// （ユーザ判断 2026-09-13）。前は同じ文で、入れれば済むのか作れば済むのか分からなかった
    /// </summary>
    internal static string ProjectEmptyText(UnityTools tools) => tools switch
    {
        { HasHub: false, HasVcc: false } =>
            "Unity Hub も VCC も見つかりませんでした。どちらかを入れてプロジェクトを作るか開くと、ここに並びます。",
        { HasHub: true, HasVcc: false } =>
            "Unity Hub の一覧にプロジェクトがありません（VCC は見つかりませんでした）。Hub でプロジェクトを作るか開くと、ここに並びます。",
        { HasHub: false, HasVcc: true } =>
            "VCC の一覧にプロジェクトがありません（Unity Hub は見つかりませんでした）。VCC でプロジェクトを作るか開くと、ここに並びます。",
        _ => "Unity Hub と VCC の一覧にプロジェクトがありません。どちらかでプロジェクトを作るか開くと、ここに並びます。",
    };

    // ---- Unity Hub と VCC ----

    private const string VccMissingText =
        "VCC（VRChat Creator Companion）が見つかりませんでした。VRChat の公式サイトから VCC を入れると、ここから開けます。";

    /// <summary>Unity Hub と VCC が手元にあるか。窓が手前に戻るたびに調べ直す。</summary>
    public UnityTools Tools
    {
        get => _tools;
        private set
        {
            if (SetField(ref _tools, value))
            {
                OnPropertyChanged(nameof(CanOpenVcc));
                OnPropertyChanged(nameof(VccHint));
                OnPropertyChanged(nameof(EmptyText));
            }
        }
    }

    /// <summary>VCC が見つからなければ「VCCを開く」は押せない状態で出す（押してから見つからないと言わない・ユーザ判断 2026-09-13）。</summary>
    public bool CanOpenVcc => Tools.HasVcc;

    public string VccHint => Tools.HasVcc
        ? "VRChat Creator Companion を起動します。開いていれば手前に出します。"
        : VccMissingText;

    // ---- 操作 ----

    public RelayCommand OpenVccCommand { get; }

    public RelayCommand OpenProjectCommand { get; }

    public RelayCommand OpenProjectFolderCommand { get; }

    public RelayCommand ShowProjectCommand { get; }

    public RelayCommand ShowModificationCommand { get; }

    public RelayCommand ShowAvatarCommand { get; }

    public RelayCommand ShowMemberCommand { get; }

    /// <summary>
    /// 使ったもの1件を Unity で示す。入っていればプロジェクトタブの検索欄に入り先のフォルダ名を入れ、
    /// 入っていなければ取り込むか聞く（ユーザ仕様 2026-09-13）。
    /// </summary>
    public RelayCommand SelectInUnityCommand { get; }

    public RelayCommand OpenAvatarManageCommand { get; }

    /// <summary>アバターの行の「改変を作る」。右にそのアバターを出し、名前を入れてもらう。</summary>
    public RelayCommand StartCreateCommand { get; }

    public RelayCommand CreateModificationCommand { get; }

    public RelayCommand OpenItemPageCommand { get; }

    /// <summary>窓が手前に戻ったとき。記録・アバター・プロジェクト（開いているかの印も）・Hub と VCC の有無を読み直す。</summary>
    public void NoteWindowActivated() => RefreshAllAsync().Forget();

    // ---- 読み込み ----

    private async Task LoadAsync()
    {
        _isLoading = true;
        OnPropertyChanged(nameof(EmptyText));

        var modifications = _services.Modifications.LoadAllAsync();
        var avatars = Task.Run(() => _services.Avatars.LoadAsync());
        var projects = Task.Run(() => UnityProjects.Discover());
        var tools = Task.Run(() => UnityTools.Detect());
        await Task.WhenAll(modifications, avatars, projects, tools);
        Tools = tools.Result;

        // 商品は検索画面が持っている写しから引く（全商品の JSON を読み直さない。ショップ一覧と同じ扱い）
        var snapshot = _main.Search.SnapshotItems();
        if (snapshot.Count == 0)
        {
            snapshot = (await _services.Store.Items.LoadAllAsync()).Items;
        }

        _items = snapshot
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _avatars = avatars.Result
            .GroupBy(summary => summary.Entry.ItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        _records = modifications.Result.Modifications;
        _projects = await WithLinkedProjectsAsync(projects.Result);

        if (modifications.Result.FailedIds.Count > 0)
        {
            Status = $"読めなかった改変の記録が {modifications.Result.FailedIds.Count} 件あります"
                + "（保存先の modifications フォルダの JSON が壊れているかもしれません）。";
        }

        _isLoading = false;
        Rebuild();

        if (_pendingSelection is { } selection)
        {
            _pendingSelection = null;
            Restore(selection);
        }
    }

    /// <summary>
    /// Hub と VCC の一覧に、改変から紐付けたのに一覧に無いプロジェクトを足す。
    /// **一覧に無くても出す。**紐付けた改変がそのプロジェクトの下から消えると、無くしたように見える
    /// </summary>
    private async Task<IReadOnlyList<UnityProjectCandidate>> WithLinkedProjectsAsync(IReadOnlyList<UnityProjectCandidate> found)
    {
        var missing = _records
            .Select(record => record.UnityProject)
            .OfType<string>()
            .Where(path => !found.Any(candidate => ModificationService.SamePath(candidate.Path, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0)
        {
            return found;
        }

        var extra = await Task.Run(() => missing.Select(path => UnityProjects.Describe(path, UnityProjectSource.None)).ToList());
        return [.. found, .. extra];
    }

    private async Task RefreshRecordsAsync()
    {
        var loaded = await _services.Modifications.LoadAllAsync();
        _records = loaded.Modifications;
        _projects = await WithLinkedProjectsAsync(_projects.Where(candidate => candidate.Source != UnityProjectSource.None).ToList());
        Rebuild();
    }

    /// <summary>
    /// 窓が手前に戻ったときに、記録・アバター・プロジェクト・Hub と VCC の有無を読み直す。
    ///
    /// **「読み直す」のボタンの代わり**（ユーザ判断 2026-09-13）。ボタンでしか拾えなかったのは、アプリの外で改変の JSON や
    /// アバターの登録簿が書き換わったときだけで、それを直しに行けば窓は必ず一度離れる。Unity Hub や VCC でプロジェクトを
    /// 作ったり、Hub や VCC を入れたりして戻ってきたときも、ここで拾う。
    /// 右に出しているものは作り直さない（改変の入力中や、新しい改変の名前の入力中を消さない）。プロジェクトだけは「開いている」の印を出し直す
    /// </summary>
    private async Task RefreshAllAsync()
    {
        if (_isLoading || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var modifications = _services.Modifications.LoadAllAsync();
            var avatars = Task.Run(() => _services.Avatars.LoadAsync());
            var projects = Task.Run(() => UnityProjects.Discover());
            var tools = Task.Run(() => UnityTools.Detect());
            await Task.WhenAll(modifications, avatars, projects, tools);

            _records = modifications.Result.Modifications;
            _avatars = avatars.Result
                .GroupBy(summary => summary.Entry.ItemId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _projects = await WithLinkedProjectsAsync(projects.Result);
            Tools = tools.Result;
            Rebuild();

            if (Detail is HubProjectDetail project)
            {
                ShowProject(project.Path);
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    // ---- 右側に出す ----

    private void Restore(ModificationHubSelection selection)
    {
        switch (selection.Kind)
        {
            case ModificationHubSelectionKind.Project:
                ShowProject(selection.Key);
                break;
            case ModificationHubSelectionKind.Avatar:
                ShowAvatar(selection.Key);
                break;
            case ModificationHubSelectionKind.Modification:
                if (FindRecord(selection.Key) is { } record)
                {
                    ShowModification(record);
                }

                break;
            case ModificationHubSelectionKind.Member:
                if (FindRecord(selection.Key) is { } owner && selection.Index < owner.Members.Count)
                {
                    ShowMember(MemberRow(owner, owner.Members[selection.Index], selection.Index));
                }

                break;
        }
    }

    private ModificationRecord? FindRecord(string id)
        => _records.FirstOrDefault(record => string.Equals(record.Id, id, StringComparison.Ordinal));

    private void ShowProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var candidate = _projects.FirstOrDefault(entry => ModificationService.SamePath(entry.Path, path))
            ?? UnityProjects.Describe(path, UnityProjectSource.None);

        Detail = new HubProjectDetail
        {
            Candidate = candidate,
            Modifications = RecordsOf(candidate.Path).Select(record => ModRow(record, ModificationHubLevel.Project, false)).ToList(),
        };
    }

    private void ShowModification(ModificationRecord record)
    {
        var modification = new ModificationViewModel(record, _services, _main, _thumbnails) { IsEmbedded = true };

        // 開いた直後の読み込みでも Changed が来る。そのときは一覧を組み直さない（開くたびに一覧が動かないように）
        var first = true;
        modification.Changed += () =>
        {
            if (first)
            {
                first = false;
                return;
            }

            RefreshRecordsAsync().Forget();
        };
        modification.Deleted += () =>
        {
            Detail = null;
            Status = $"改変「{modification.Record.Name}」を消しました。";
            RefreshRecordsAsync().Forget();
        };

        Detail = modification;
    }

    private void ShowAvatar(string id)
    {
        _avatars.TryGetValue(id, out var summary);
        Detail = new HubAvatarDetail
        {
            AvatarItemId = id,
            Name = AvatarNameOf(id),
            BoothName = summary?.Entry.BoothName ?? string.Empty,
            IsOwned = summary?.IsOwned == true,
            BaseText = summary?.Entry.BaseName is { Length: > 0 } baseName ? $"共通素体：{baseName}" : string.Empty,
            HasItem = _items.ContainsKey(id),
            IconPath = AvatarIconPath(id),
            Thumbnails = _thumbnails,
            Modifications = _records
                .Where(record => string.Equals(record.AvatarItemId, id, StringComparison.Ordinal))
                .OrderByDescending(record => record.UpdatedAt)
                .Select(record => ModRow(record, ModificationHubLevel.Avatar, false))
                .ToList(),
        };
    }

    private void ShowMember(HubMemberRow? row)
    {
        if (row is null)
        {
            return;
        }

        _items.TryGetValue(row.ItemId, out var item);
        var detail = new HubItemDetail
        {
            Row = row,
            Item = item,
            VariationText = ModificationViewModel.VariationLabel(row.Member, item),
            ThumbnailPath = row.ThumbnailPath,
            Thumbnails = _thumbnails,
            UsedIn = _records
                .Where(record => record.Members.Any(member => string.Equals(member.ItemId, row.ItemId, StringComparison.Ordinal)))
                .OrderByDescending(record => record.UpdatedAt)
                .Select(record => ModRow(record, ModificationHubLevel.Modification, false))
                .ToList(),
        };

        Detail = detail;
        FillDestinationAsync(detail, item).Forget();
    }

    /// <summary>Unity のどこに入るか。unitypackage を解くのは重い（大きな物は1件0.2秒ほど）ので裏で読む。</summary>
    private async Task FillDestinationAsync(HubItemDetail detail, ItemRecord? item)
    {
        var packages = item is null ? [] : ModificationViewModel.PackagesFor(item, detail.Row.Member);
        if (packages.Count == 0)
        {
            detail.DestinationText = "Unityに入れられるファイル（zip の中の unitypackage）が手元にありません。";
            return;
        }

        var roots = await Task.Run(() => UnityHandoff.DestinationRoots(packages.SelectMany(UnityHandoff.ReadAssetPaths)));
        detail.DestinationText = roots.Count == 0 ? "Unityのどこに入るかを読めませんでした。" : UnityHandoff.DescribeDestinations(roots);
    }

    // ---- 作る・開く ----

    private async Task CreateModificationAsync()
    {
        if (Detail is not HubAvatarDetail avatar)
        {
            return;
        }

        var name = avatar.NameInput.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // **同じ名前を許すが、黙って2つ並べない**（アバターの管理と同じ）
        if (await _services.Modifications.HasSameNameAsync(avatar.AvatarItemId, name))
        {
            var answer = System.Windows.MessageBox.Show(
                $"「{name}」という改変が既にあります。\n\n"
                + "同じ名前で作れます（作り直したいときのため）。\n"
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

        var result = await _services.Commands.ExecuteAsync(new UiCommand.CreateModification(avatar.AvatarItemId, name));
        if (result is CommandResult.Failed failed)
        {
            Status = failed.Message;
            return;
        }

        Status = $"改変「{name}」を作りました。";
        await RefreshRecordsAsync();

        // 作った直後は使ったものもプロジェクトも空なので、そのまま右に開いて続きを入れてもらう
        if (result is CommandResult.ModificationCreated created)
        {
            ShowModification(created.Record);
        }
    }

    private async Task OpenItemPageAsync(string? itemId)
    {
        if (itemId is null)
        {
            return;
        }

        if ((_items.GetValueOrDefault(itemId) ?? await _services.Store.Items.LoadAsync(itemId)) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    // ---- ボタンの引数を読む ----

    private static string? PathOf(object? parameter) => parameter switch
    {
        string path => path,
        HubProjectGroup group => group.Path,
        HubProjectDetail detail => detail.Path,
        HubModificationRow row => row.ProjectPath,
        _ => null,
    };

    private static ModificationRecord? RecordOf(object? parameter) => parameter switch
    {
        HubModificationRow row => row.Record,
        HubMemberRow member => member.Record,
        HubItemDetail detail => detail.Row.Record,
        ModificationRecord record => record,
        _ => null,
    };

    private static string? AvatarIdOf(object? parameter) => parameter switch
    {
        string id => id,
        HubAvatarGroup group => group.AvatarItemId,
        HubAvatarDetail detail => detail.AvatarItemId,
        HubModificationRow row => row.AvatarItemId,
        _ => null,
    };
}
