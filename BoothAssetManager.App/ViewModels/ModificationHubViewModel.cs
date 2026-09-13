using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

// 改変の画面（2026-09-13 ユーザ仕様「改変周りの刷新」）。
//
// 画面の構成をユーザの認識の単位に合わせる。改変を「Unityプロジェクト」「アバター」「改変」の3つの文脈から見る。
// **新しいデータは持たない。**今の改変の記録（ModificationRecord）をそのまま3通りに並べ直す見せ方だけ（ユーザ指示）。
// 左で見方を切り替えて探し、右に押したもののビューを出す。

/// <summary>左の一覧の見方。</summary>
public enum ModificationHubLevel
{
    Project,
    Avatar,
    Modification,
}

public enum ModificationHubSelectionKind
{
    Project,
    Avatar,
    Modification,
    Member,
}

/// <summary>右側に何を出していたか。戻るで戻ったときに同じものを出し直すために履歴へ預ける。</summary>
/// <param name="Key">プロジェクトならパス、アバターなら商品ID、改変と使ったものなら改変のID。</param>
/// <param name="Index">使ったものの位置（並びが導入の順なので位置で指す）。</param>
public sealed record ModificationHubSelection(ModificationHubSelectionKind Kind, string Key, int Index = 0);

/// <summary>
/// 畳んだ・開いたの状態。画面は開くたびに作り直すので、アプリを閉じるまでここに持つ（アバターの管理の見出しと同じ扱い）。
/// </summary>
internal static class HubExpansion
{
    private static readonly Dictionary<string, bool> States = new(StringComparer.OrdinalIgnoreCase);

    public static bool Get(string key, bool fallback) => States.TryGetValue(key, out var value) ? value : fallback;

    public static void Set(string key, bool value) => States[key] = value;
}

/// <summary>畳める行。</summary>
public abstract class HubExpandable : ViewModelBase
{
    private readonly string _key;
    private bool _isExpanded;

    /// <param name="forceOpen">
    /// 探している間は開いて出す（覚えた状態は変えない）。畳んだままだと、当たった改変が見えない
    /// </param>
    protected HubExpandable(string key, bool openByDefault, bool forceOpen)
    {
        _key = key;
        _isExpanded = forceOpen || HubExpansion.Get(key, openByDefault);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                HubExpansion.Set(_key, value);
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    /// <summary>畳む印。中身が無い行は出さない（押しても何も起きない印は嘘になる）。</summary>
    public string ExpandGlyph => !CanExpand ? string.Empty : IsExpanded ? "▾" : "▸";

    protected abstract bool CanExpand { get; }
}

/// <summary>
/// 改変に使ったもの1件。**ファイル単位**（ユーザ指摘 2026-09-13）——Unityへ送って足した分は、
/// どの zip のどの unitypackage かまで記録にある。手で足した分は分からないまま出す（推定で埋めない）。
/// </summary>
public sealed class HubMemberRow : ViewModelBase
{
    public required ModificationRecord Record { get; init; }

    /// <summary>改変の中の位置。同じ商品を別の版で2回足せるので、位置で指す。</summary>
    public required int Index { get; init; }

    public required ModificationMember Member { get; init; }

    public required string Name { get; init; }

    public required string FileText { get; init; }

    public required bool IsMissing { get; init; }

    public string ItemId => Member.ItemId;

    /// <summary>「Unityで選択」の相手。改変に紐付けたプロジェクト。</summary>
    public string? ProjectPath => Record.UnityProject;

    public string? ThumbnailPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>裏で読み、届いたら描き直す（アバターの管理の頭の絵と同じ扱い）。</summary>
    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    public string Initial => AvatarText.InitialOf(Name);
}

/// <summary>改変1件の行。3つの見方すべてで同じ形を使い、見方ごとに出す繋がり（アバター・プロジェクト）を変える。</summary>
public sealed class HubModificationRow(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public required ModificationRecord Record { get; init; }

    public string Name => Record.Name;

    public required string AvatarName { get; init; }

    public string AvatarItemId => Record.AvatarItemId;

    public string? ProjectPath => Record.UnityProject;

    public string ProjectName => ModificationHubViewModel.ProjectNameOf(Record.UnityProject);

    /// <summary>アバターの見方の中では出さない（見出しがそのアバター）。</summary>
    public bool ShowsAvatar { get; init; }

    /// <summary>プロジェクトの見方の中では出さない（見出しがそのプロジェクト）。</summary>
    public bool ShowsProject { get; init; }

    public bool ShowsProjectLink => ShowsProject && Record.HasUnityProject;

    /// <summary>メモの1行目。一覧では長いメモを全部出さない。</summary>
    public string MemoText => (Record.Memo ?? string.Empty)
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault() ?? string.Empty;

    public bool HasMemo => MemoText.Length > 0;

    public required IReadOnlyList<HubMemberRow> Members { get; init; }

    public string CountText => Members.Count == 0 ? "使ったものはまだありません" : $"使ったもの {Members.Count}";

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>頭の絵。改変に貼った写真の1枚目、無ければアバターの絵。</summary>
    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    public string Initial => AvatarText.InitialOf(Name);

    protected override bool CanExpand => Members.Count > 0;
}

/// <summary>プロジェクトの見方の見出し1つ。紐付けていない改変も1つの見出しにまとめる（Candidate が null）。</summary>
public sealed class HubProjectGroup(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public UnityProjectCandidate? Candidate { get; init; }

    public bool IsProject => Candidate is not null;

    public string Title => Candidate?.Name ?? "Unityプロジェクトに紐付けていない改変";

    public string? Path => Candidate?.Path;

    public bool IsOpen => Candidate?.IsOpen == true;

    public bool IsMissing => Candidate is { Exists: false };

    public bool CanOpen => Candidate is { Exists: true };

    public string DetailText => Candidate is null
        ? "改変の右側でプロジェクトを紐付けると、そのプロジェクトの下に並びます"
        : IsMissing ? "フォルダが見つかりません" : Candidate.Version ?? "バージョンが読めません";

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public string CountText => Modifications.Count == 0 ? string.Empty : $"改変 {Modifications.Count}";

    protected override bool CanExpand => Modifications.Count > 0;
}

/// <summary>アバターの見方の見出し1つ。</summary>
public sealed class HubAvatarGroup(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public required string AvatarItemId { get; init; }

    public required string Title { get; init; }

    public required bool IsOwned { get; init; }

    public string OwnedText => IsOwned ? "所有" : "所有していない";

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public string CountText => Modifications.Count == 0 ? "改変なし" : $"改変 {Modifications.Count}";

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    public string Initial => AvatarText.InitialOf(Title);

    protected override bool CanExpand => Modifications.Count > 0;
}

/// <summary>右側：Unityプロジェクト。</summary>
public sealed class HubProjectDetail
{
    public required UnityProjectCandidate Candidate { get; init; }

    public string Name => Candidate.Name;

    public string Path => Candidate.Path;

    public bool Exists => Candidate.Exists;

    public bool IsMissing => !Candidate.Exists;

    public bool IsOpen => Candidate.IsOpen;

    public string VersionText => Candidate.Version is { } version ? $"Unity {version}" : "バージョンが読めません";

    public string SourceText => Candidate.Source switch
    {
        UnityProjectSource.Hub | UnityProjectSource.Vcc => "Unity Hub と VCC の一覧",
        UnityProjectSource.Hub => "Unity Hub の一覧",
        UnityProjectSource.Vcc => "VCC の一覧",
        _ => "どちらの一覧にも無い（改変から紐付けたもの）",
    };

    public string OpenText => IsOpen ? "開いています" : "閉じています";

    public string LastWriteText => Candidate.LastWrite is { } time ? $"最後に触った日 {time.ToLocalTime():yyyy-MM-dd}" : string.Empty;

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public bool HasModifications => Modifications.Count > 0;

    public string EmptyText =>
        "このプロジェクトに紐付けた改変はまだありません。改変の右側の「Unityプロジェクト」で紐付けると、ここに並びます。";
}

/// <summary>右側：アバター。改変に関係する所だけ（名前・所有・改変・作る）。ほかの設定はアバターの管理へ。</summary>
public sealed class HubAvatarDetail : ViewModelBase
{
    public required string AvatarItemId { get; init; }

    public required string Name { get; init; }

    public required string BoothName { get; init; }

    public bool HasBoothName => BoothName.Length > 0 && BoothName != Name;

    public required bool IsOwned { get; init; }

    public string OwnedText => IsOwned ? "所有しているアバター" : "所有していないアバター";

    public required string BaseText { get; init; }

    public bool HasBase => BaseText.Length > 0;

    /// <summary>商品として手元にあるか（あれば商品ページを開ける）。</summary>
    public required bool HasItem { get; init; }

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    public string Initial => AvatarText.InitialOf(Name);

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public bool HasModifications => Modifications.Count > 0;

    private string _nameInput = string.Empty;

    /// <summary>新しく作る改変の名前。</summary>
    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }
}

/// <summary>右側：使ったもの1件（商品の要約）。全部の操作は商品ページで。</summary>
public sealed class HubItemDetail : ViewModelBase
{
    public required HubMemberRow Row { get; init; }

    public ItemRecord? Item { get; init; }

    public string Name => Row.Name;

    public string ItemId => Row.ItemId;

    public bool CanOpenItem => Item is not null;

    public string ShopText => Item?.Booth.Shop?.Name ?? string.Empty;

    public bool HasShop => ShopText.Length > 0;

    public string ModificationName => Row.Record.Name;

    public string FileText => Row.FileText;

    public required string VariationText { get; init; }

    public bool HasVariation => VariationText.Length > 0;

    public bool IsMissing => Row.IsMissing;

    public string ProjectText => Row.ProjectPath is { } path
        ? $"「Unityで選択」は、この改変に紐付けたプロジェクト「{ModificationHubViewModel.ProjectNameOf(path)}」を相手にします。"
        : "この改変はUnityプロジェクトに紐付いていないので、「Unityで選択」は使えません。改変を開いて紐付けてください。";

    public string? ThumbnailPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    private string _destinationText = "Unityのどこに入るかを読んでいます…";

    /// <summary>Unity のどこに入るか（商品ページの「Assets/〇〇 に入ります」と同じ読み方）。</summary>
    public string DestinationText
    {
        get => _destinationText;
        set => SetField(ref _destinationText, value);
    }

    /// <summary>この商品を使ったほかの改変も含めた一覧。</summary>
    public required IReadOnlyList<HubModificationRow> UsedIn { get; init; }
}

/// <summary>
/// 改変の画面。左で「Unityプロジェクト」「アバター」「改変」の見方を切り替えて探し、右に押したもののビューを出す。
/// </summary>
public sealed class ModificationHubViewModel : ViewModelBase
{
    /// <summary>最後に使った見方。ナビから開き直したときに同じ見方で始める（アプリを閉じるまで）。</summary>
    private static ModificationHubLevel s_lastLevel = ModificationHubLevel.Project;

    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;

    /// <summary>探すときは大文字小文字・かなの種類・全角半角を区別しない（「くうた」で「クウタ」に当てる）。</summary>
    private const CompareOptions Loose =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private readonly AppServiceContainer _services;
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
            parameter => _ = SelectInUnityAsync(parameter as HubMemberRow ?? (parameter as HubItemDetail)?.Row),
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
            () => _ = CreateModificationAsync(),
            () => Detail is HubAvatarDetail avatar && avatar.NameInput.Trim().Length > 0);
        OpenItemPageCommand = new RelayCommand(parameter => _ = OpenItemPageAsync(parameter as string));

        _ = LoadAsync();
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
    public void NoteWindowActivated() => _ = RefreshAllAsync();

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

    // ---- 左の一覧を組む ----

    private void Rebuild()
    {
        Groups.Clear();

        if (!_isLoading)
        {
            var query = Query.Trim();
            switch (Level)
            {
                case ModificationHubLevel.Project:
                    BuildProjects(query);
                    break;
                case ModificationHubLevel.Avatar:
                    BuildAvatars(query);
                    break;
                default:
                    BuildModifications(query);
                    break;
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// プロジェクトの見方。**改変の無いプロジェクトも出す**（この見方はランチャーも兼ねる）。
    /// 並びは Hub・VCC の一覧と同じ（開いているもの、次に新しく触ったもの）。
    /// </summary>
    private void BuildProjects(string query)
    {
        foreach (var candidate in _projects)
        {
            var records = RecordsOf(candidate.Path);
            var nameHit = query.Length == 0 || Hit(candidate.Name, query);
            var shown = nameHit ? records : records.Where(record => Hit(record.Name, query)).ToList();
            if (!nameHit && shown.Count == 0)
            {
                continue;
            }

            Groups.Add(new HubProjectGroup($"project:{candidate.Path}", openByDefault: true, forceOpen: query.Length > 0)
            {
                Candidate = candidate,
                Modifications = shown.Select(record => ModRow(record, ModificationHubLevel.Project, query.Length > 0)).ToList(),
            });
        }

        // 紐付けていない改変も、この見方から辿れるようにする（見方を変えないと見つからない改変を作らない）
        var unlinked = _records
            .Where(record => !record.HasUnityProject && (query.Length == 0 || Hit(record.Name, query)))
            .OrderByDescending(record => record.UpdatedAt)
            .ToList();
        if (unlinked.Count > 0)
        {
            Groups.Add(new HubProjectGroup("project:(none)", openByDefault: true, forceOpen: query.Length > 0)
            {
                Modifications = unlinked.Select(record => ModRow(record, ModificationHubLevel.Project, query.Length > 0)).ToList(),
            });
        }
    }

    /// <summary>
    /// アバターの見方。出すのは**所有しているアバターと、改変のあるアバター**。
    /// 対応表記で見かけただけのアバターまで出すと、改変を探す一覧が数百行になる（それはアバターの管理の役）。
    /// </summary>
    private void BuildAvatars(string query)
    {
        var byAvatar = _records
            .GroupBy(record => record.AvatarItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ModificationRecord>)group.OrderByDescending(record => record.UpdatedAt).ToList(),
                StringComparer.Ordinal);

        var ids = _avatars.Values
            .Where(summary => summary.IsOwned && summary.Entry.AvatarOverride != false)
            .Select(summary => summary.Entry.ItemId)
            .Concat(byAvatar.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Records: byAvatar.GetValueOrDefault(id) ?? []))
            // 改変のあるアバターを先に、最近触った改変のあるものから
            .OrderByDescending(entry => entry.Records.Count > 0)
            .ThenByDescending(entry => entry.Records.FirstOrDefault()?.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => AvatarNameOf(entry.Id), StringComparer.CurrentCulture);

        foreach (var (id, records) in ids)
        {
            var nameHit = query.Length == 0 || AvatarHit(id, query);
            var shown = nameHit ? records : records.Where(record => Hit(record.Name, query)).ToList();
            if (!nameHit && shown.Count == 0)
            {
                continue;
            }

            Groups.Add(new HubAvatarGroup($"avatar:{id}", openByDefault: true, forceOpen: query.Length > 0)
            {
                AvatarItemId = id,
                Title = AvatarNameOf(id),
                IsOwned = _avatars.TryGetValue(id, out var summary) && summary.IsOwned,
                IconPath = AvatarIconPath(id),
                Thumbnails = _thumbnails,
                Modifications = shown.Select(record => ModRow(record, ModificationHubLevel.Avatar, query.Length > 0)).ToList(),
            });
        }
    }

    /// <summary>改変の見方。新しく触ったものから。改変そのものが見出しなので、使ったものを開いて出す。</summary>
    private void BuildModifications(string query)
    {
        foreach (var record in _records.OrderByDescending(record => record.UpdatedAt))
        {
            if (query.Length > 0
                && !Hit(record.Name, query)
                && !Hit(AvatarNameOf(record.AvatarItemId), query)
                && !Hit(ProjectNameOf(record.UnityProject), query))
            {
                continue;
            }

            Groups.Add(ModRow(record, ModificationHubLevel.Modification, forceOpen: false));
        }
    }

    /// <summary>
    /// 改変の行。プロジェクト・アバターの見方の中では畳んで出す（見出しの下が長くなりすぎないように）。
    /// 改変の見方では改変そのものが見出しなので開いて出す。
    /// </summary>
    private HubModificationRow ModRow(ModificationRecord record, ModificationHubLevel level, bool forceOpen)
        => new($"mod:{level}:{record.Id}", openByDefault: level == ModificationHubLevel.Modification, forceOpen: false)
        {
            Record = record,
            AvatarName = AvatarNameOf(record.AvatarItemId),
            ShowsAvatar = level != ModificationHubLevel.Avatar,
            ShowsProject = level != ModificationHubLevel.Project,
            Members = record.Members.Select((member, index) => MemberRow(record, member, index)).ToList(),
            IconPath = ModificationIconPath(record),
            Thumbnails = _thumbnails,
        };

    private HubMemberRow MemberRow(ModificationRecord record, ModificationMember member, int index)
    {
        _items.TryGetValue(member.ItemId, out var item);
        return new HubMemberRow
        {
            Record = record,
            Index = index,
            Member = member,
            Name = item?.DisplayName ?? member.ItemId,
            FileText = FileTextOf(member),

            // 手元に無くても記録は残す。そのとき使ったのは事実
            IsMissing = item is null || !item.IsDownloaded,
            ThumbnailPath = item is null ? null : ItemThumbnailPath(item),
            Thumbnails = _thumbnails,
        };
    }

    /// <summary>
    /// どのファイルか。**空欄の意味を言い分ける**（改変の画面と同じ）。Unityへ送って足した分は unitypackage の名前、
    /// 手で足した分は分からないと言う。
    /// </summary>
    private static string FileTextOf(ModificationMember member) => member.Package is { } package
        ? Path.GetFileName(package)
        : member.IsFromUnity ? "Unityへ送った記録あり" : "どのファイルを使ったかは分かりません";

    private IReadOnlyList<ModificationRecord> RecordsOf(string projectPath) => _records
        .Where(record => ModificationService.SamePath(record.UnityProject, projectPath))
        .OrderByDescending(record => record.UpdatedAt)
        .ToList();

    private static bool Hit(string? text, string query)
        => !string.IsNullOrEmpty(text) && Compare.IndexOf(text, query, Loose) >= 0;

    /// <summary>アバターを引ける語。表示名・BOOTHの正式名・別名・商品ID（アバターの管理と同じ）。</summary>
    private bool AvatarHit(string id, string query)
    {
        if (Hit(AvatarNameOf(id), query) || id.Contains(query, StringComparison.Ordinal))
        {
            return true;
        }

        return _avatars.TryGetValue(id, out var summary)
            && (Hit(summary.Entry.BoothName, query) || summary.Entry.Aliases.Any(alias => Hit(alias.Text, query)));
    }

    private string AvatarNameOf(string id)
    {
        if (_avatars.TryGetValue(id, out var summary))
        {
            if (!string.IsNullOrWhiteSpace(summary.Name))
            {
                return summary.Name;
            }

            return summary.Entry.DisplayName ?? summary.Entry.BoothName ?? id;
        }

        return _items.TryGetValue(id, out var item) ? item.DisplayName : id;
    }

    /// <summary>プロジェクトの名前はフォルダ名（Unity の窓の題に出るのもフォルダ名）。</summary>
    public static string ProjectNameOf(string? path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private string? AvatarIconPath(string id)
        => AvatarImageSync.IconPath(_services.Paths, id, _items.GetValueOrDefault(id));

    private string? ModificationIconPath(ModificationRecord record)
    {
        if (record.Images.Count > 0)
        {
            var path = Path.Combine(_services.Paths.ModificationImagesDir(record.Id), record.Images[0].FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return AvatarIconPath(record.AvatarItemId);
    }

    /// <summary>商品の1枚目。検索のカードと同じ選び方（BOOTHの並び・★・役割の指定）。</summary>
    private string? ItemThumbnailPath(ItemRecord item)
    {
        var directory = _services.Paths.ItemImagesDir(item.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, item.Booth.Images, _thumbnails.ListFiles(directory), item.Local.UserImages);
        return Core.Images.ItemImageOrder.Thumbnail(
            ordered, item.Local.ThumbnailImage, _services.Settings.ThumbnailRole, item.Local.ImageRoles);
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

            _ = RefreshRecordsAsync();
        };
        modification.Deleted += () =>
        {
            Detail = null;
            Status = $"改変「{modification.Record.Name}」を消しました。";
            _ = RefreshRecordsAsync();
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
        _ = FillDestinationAsync(detail, item);
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

    // ---- Unity・VCC ----

    private void OpenVcc()
    {
        Status = VccLaunch.Open() switch
        {
            VccOpenResult.Launched => "VCC を起動しました。",
            VccOpenResult.BroughtToFront => "VCC は開いていたので、手前に出しました。",
            VccOpenResult.AlreadyOpenNotFront =>
                "VCC は開いています。手前に出せなかったので、タスクバーの VCC を押して切り替えてください。",
            VccOpenResult.NotInstalled => VccMissingText,
            _ => "VCC を起動できませんでした。スタートメニューから開いてみてください。",
        };
    }

    /// <summary>プロジェクトを開く。**結果を必ず言う**（開いていたら手前に出るだけで、何も起きなかったように見える）。</summary>
    private void OpenProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Status = UnityOpenText.For(UnityLaunch.OpenProject(path), ProjectNameOf(path));
    }

    /// <summary>
    /// 使ったもの1件を Unity で示す（ユーザ仕様 2026-09-13）。
    ///
    /// **入っていれば、入り先のルートフォルダ（作者名のフォルダなど）の名前をプロジェクトタブの検索欄に入れる。**
    /// どのフォルダがそのアセットかを示せれば十分（ユーザ判断）なので、選んで開くところまではしない。
    /// 入っていなければ、取り込むか聞く。どちらもプロジェクトが開いている必要がある。
    /// 入っているかは、送ったファイル（無ければ今ある zip）の中身のパスがプロジェクトにあるかで見る（プロジェクトの中を調べると同じ数え方）。
    /// </summary>
    private async Task SelectInUnityAsync(HubMemberRow? row)
    {
        const string title = "Unityで選択";
        if (row is null)
        {
            return;
        }

        if (row.ProjectPath is not { } project)
        {
            Status = "この改変はUnityプロジェクトに紐付いていません。改変を開いて、右側の「Unityプロジェクト」から紐付けてください。";
            return;
        }

        var projectName = ProjectNameOf(project);
        if (!Directory.Exists(project))
        {
            Status = $"紐付けたプロジェクト「{projectName}」のフォルダが見つかりません。";
            return;
        }

        var item = _items.GetValueOrDefault(row.ItemId) ?? await _services.Store.Items.LoadAsync(row.ItemId);
        if (item is null)
        {
            Status = $"「{row.Name}」は手元にありません。";
            return;
        }

        var packages = ModificationViewModel.PackagesFor(item, row.Member);
        if (packages.Count == 0)
        {
            Status = $"「{row.Name}」には、Unityに入れられるファイル（zip の中の unitypackage）が手元にありません。";
            return;
        }

        Status = $"「{projectName}」の中を調べています…";
        var (roots, present) = await Task.Run(() =>
        {
            var paths = packages.SelectMany(UnityHandoff.ReadAssetPaths).ToList();
            var matches = UnityProjectMatcher.Match(
                project, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [item.Id] = paths });
            return (UnityHandoff.DestinationRoots(paths), matches.FirstOrDefault()?.Present ?? 0);
        });

        var editor = UnityEditors.Open().FirstOrDefault(candidate =>
            string.Equals(candidate.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

        // 開いている印はあるのに窓が特定できない（起動中・コンパイル中で題が読めない）
        if (editor is null && UnityProjects.IsProjectOpen(project))
        {
            Status = $"「{projectName}」は開いていますが、読み込み中のようです。落ち着いてから、もう一度押してください。";
            return;
        }

        if (present > 0)
        {
            if (roots.Count == 0)
            {
                Status = "入り先のフォルダを読めませんでした。";
                return;
            }

            var root = roots[0];
            var folder = root.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            if (editor is null)
            {
                Status = $"「{projectName}」の {root} に入っています。プロジェクトが開いていないので、"
                    + "「Unityで開く」で開いてからもう一度押すと、Unity のプロジェクトタブで示します。";
                return;
            }

            // 名前ではなくパスで渡す。同じ名前のフォルダが別の場所にあると、名前では取り違える（§13-7）
            var outcome = await UnityProjectTab.SelectFolderAsync(editor.ProcessId, project, root);
            Status = outcome switch
            {
                { Problem: { } problem } => problem,

                // Packages の下は Unity の検索に出ないので探していない。見つける場所の名前を伝える
                { Searched: false, StopReason: { } where } =>
                    $"入り先は {root} です。{where}、Unity では探さずに手前に出しました。",

                // 1件と言い切れないときは、一番上を開かずに検索の結果で止めている（ユーザ指示）。理由と、何をすればよいかを書く
                { StopReason: { } reason } =>
                    $"Unity の「{projectName}」の{outcome.Where}で探しました。{reason}、開かずに検索の結果で止めています。"
                    + $"入り先は {root} です。Unity で選んでください。",
                _ => $"Unity の「{projectName}」の{outcome.Where}で「{folder}」を開きました（入り先 {root}）。",
            };
            return;
        }

        if (editor is null)
        {
            Status = $"「{row.Name}」は「{projectName}」にまだ入っていません。取り込むには、先に「Unityで開く」でプロジェクトを開いてください。";
            return;
        }

        // 送信は1列に限る。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (UnityImportQueue.IsRunning)
        {
            System.Windows.MessageBox.Show(UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<UnityPackageEntry> toSend = packages;
        var recorded = false;
        if (row.Member.FileHash is null && packages.Count > 1 && PackageChoiceSection.Build(item, row.Index) is { } choice)
        {
            // どのファイルを使ったか記録が無く、送れる物が2つ以上ある。全部送ると古い版や別の種類まで入るので選ばせ、
            // 選んだ物をこの行に記録する（ユーザ判断 2026-09-13）
            var model = new PickPackagesDialogViewModel(
                title,
                $"「{row.Name}」は、Unityの「{projectName}」にまだ入っていません。取り込む物を選んでください。",
                [choice],
                othersCount: 0,
                records: true);
            if (!Views.PickPackagesDialog.Ask(model))
            {
                Status = string.Empty;
                return;
            }

            toSend = choice.CheckedPackages;
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RecordModificationMemberFiles(row.Record.Id, row.Index, choice.CheckedMembers));
            recorded = result is not CommandResult.Failed;
        }
        else
        {
            var answer = System.Windows.MessageBox.Show(
                $"「{row.Name}」（{row.FileText}）は、Unityの「{projectName}」にまだ入っていません。取り込みますか？\n\n"
                + "Unity側で取り込む内容の一覧が出るので、そこで確認してから取り込めます。",
                title,
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.OK);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                Status = string.Empty;
                return;
            }
        }

        var outcomes = await UnityImportQueue.RunAsync(
            editor.ProcessId, toSend, new Progress<UnityQueueProgress>(report => Status = report.Text), CancellationToken.None);

        // 「使った」の足跡。Cancel された物は入っていないので付けない（ほかの送り方と同じ）
        if (outcomes.Any(outcome => outcome.Opened && !outcome.Cancelled))
        {
            _ = _services.Recent.TouchAsync(item.Id, RecentKind.Used);
        }

        var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
        Status = failed.Count > 0
            ? $"Unityへ送れませんでした（{failed[0].Problem}）。"
            : outcomes.All(outcome => outcome.Cancelled)
                ? "Cancel されたので、入っていません。"
                : $"「{projectName}」に取り込み画面を出しました。入った後にもう一度押すと、プロジェクトタブで示します。";

        // 記録した行（どのファイルを使ったか）を一覧に出す
        if (recorded)
        {
            await RefreshRecordsAsync();
        }
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
