using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 条件「Unityプロジェクト」：プロジェクトの中に実際に入っている商品で絞る（ユーザ判断 2026-11-01）。
/// 改変に紐付けなくても、開いている Unity の中身をすぐ見られるようにする。
///
/// **調べるのは裏で、結果はアプリを閉じるまで覚える。**1回で持っている商品の全部の中身の一覧を読むので、
/// 絞り込みのたびには調べられない。入れ直した後は「読み直す」で取り直す。
///
/// **前回の結果を先に見せ、裏で調べ直して差し替える**（ユーザ判断 2026-11-02）。条件は次の起動でも効くので、
/// 控えが無いと調べ終わるまで一覧が空になる。プロジェクトの中身はアプリの外で変わるので、控えだけで済ませない。
/// </summary>
public sealed partial class SearchViewModel
{
    /// <summary>「今開いているプロジェクト」の文。条件の要約にも出る。</summary>
    public const string OpenProjectIncludeLabel = "今開いているプロジェクト";

    /// <summary>名前だけ一致したファイルも足す切り替え。取り違えがあり得るので既定は切る（ユーザ判断 2026-11-01）。</summary>
    public const string SameNameFlagLabel = "名前が同じファイルも含める";

    private sealed class ProjectContents
    {
        public UnityProjectScan Scan { get; set; } = UnityProjectScan.Empty;

        public bool IsScanning { get; set; } = true;

        public int Done { get; set; }

        public int Total { get; set; }

        public string? Problem { get; set; }

        public CancellationTokenSource Cancel { get; } = new();
    }

    private readonly Dictionary<string, ProjectContents> _projectContents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>控えを読んだプロジェクト。「読み直す」は入れ直した後に押すので、消えたはずの物を控えからまた見せない。</summary>
    private readonly HashSet<string> _savedScansRead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>起動のときの、開いているプロジェクトの確認。これが終わるまで最初の一覧を出さない。</summary>
    private Task? _firstOpenProjectsCheck;

    private IReadOnlyList<UnityProjectCandidate>? _unityProjects;
    private bool _loadingUnityProjects;

    /// <summary>今開いているプロジェクトの場所（比べる形）。見直すのは足したとき・窓が前に戻ったとき・読み直すとき。</summary>
    private IReadOnlyList<string> _openProjects = [];

    /// <summary>開いているのに場所の分からないプロジェクトの名前（Hub にも VCC にも載っていない）。理由を言うために持つ。</summary>
    private IReadOnlyList<string> _unknownOpenProjects = [];

    private bool _openProjectsChecked;

    /// <summary>何回目の見直しか。窓に戻る・読み直すで重なり、後に始めた方が先に終わると、古い結果で上書きしてしまう。</summary>
    private int _openProjectsGeneration;

    /// <summary>候補の一覧の読み直しを頼んだ回数（読んでいる途中に頼まれたら、終わってから読み直す）。</summary>
    private int _unityProjectsGeneration;

    private bool _openUnityOnly;

    /// <summary>
    /// 検索欄の右の「開いているUnity」（ユーザ判断 2026-11-03）。前は「…」の中の近道で条件を足していたが、
    /// 使っていると切り替えたくなる場面が多かった。条件としては並べず、ここに持つだけ。
    /// 手で足した条件「Unityプロジェクト」とは両方に当てはまる物を出す。
    /// </summary>
    public bool OpenUnityOnly
    {
        get => _openUnityOnly;
        set
        {
            if (!SetField(ref _openUnityOnly, value))
            {
                return;
            }

            _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
                state => state with { SearchOpenUnityOnly = value })).Forget();
            ApplyFilters();

            // オンにしたときは開いているかを見直す（灰色にする前に Unity を閉じていたかもしれない）
            if (value)
            {
                RefreshOpenProjectsAsync().Forget();
            }
        }
    }

    /// <summary>
    /// スイッチを押せるか。開いていなければ灰色にし、オンのままでも絞らない（ユーザ判断 2026-11-03）。
    /// 開き直せば、覚えておいたオンのまま絞り直す
    /// </summary>
    public bool CanUseOpenUnity => _openProjects.Count > 0;

    /// <summary>押せないときの理由（吹き出し）。</summary>
    public string OpenUnityDisabledReason => _unknownOpenProjects.Count > 0
        ? "開いているUnityのプロジェクトの場所が分かりません。"
        : "Unityが開いていません。";

    /// <summary>スイッチで今絞っているか。</summary>
    private bool OpenUnityFilterOn => _openUnityOnly && _openProjects.Count > 0;

    /// <summary>スイッチで絞っているか（件数の帯の「読み直す」を出す）。</summary>
    public bool IsOpenUnityFiltering => OpenUnityFilterOn;

    /// <summary>
    /// スイッチで絞っている間の状況（件数の帯に出す）。条件の中の文はスイッチだけで絞っているときはどこにも見えないので、
    /// 調べている間の0件を「入っていない」と読ませないように、ここで言う
    /// </summary>
    public string OpenUnityStatus => !OpenUnityFilterOn
        ? string.Empty
        : string.Join('\n', _openProjects
            .Select(key => _projectContents.GetValueOrDefault(key) is { } contents
                ? contents.Problem ?? (contents.IsScanning
                    ? $"「{ProjectNameOf(key)}」の中を調べています…（{contents.Done}/{contents.Total}）"
                    : null)
                : null)
            .OfType<string>());

    public bool HasOpenUnityStatus => OpenUnityStatus.Length > 0;

    /// <summary>スイッチの見た目に関わる値を知らせ直す。</summary>
    private void NotifyOpenUnitySwitch()
    {
        OnPropertyChanged(nameof(CanUseOpenUnity));
        OnPropertyChanged(nameof(OpenUnityDisabledReason));
        OnPropertyChanged(nameof(IsOpenUnityFiltering));
        OnPropertyChanged(nameof(OpenUnityStatus));
        OnPropertyChanged(nameof(HasOpenUnityStatus));
    }

    /// <summary>スイッチで外した商品か（絞り込みの基礎の述語）。名前だけ同じファイルは数えない（取り違えがあり得る）。</summary>
    private bool PassesOpenUnity(ItemRecord item) => !OpenUnityFilterOn || OpenProjectContains(item.Id, sameName: false);

    /// <summary>
    /// 0件のとき、スイッチのせいかを見る。スイッチを除いても0件なら、ほかの条件の0件として案内する。
    /// 全商品をもう1回照らすので、0件で、スイッチで絞っているときだけ
    /// </summary>
    private bool EmptyBecauseOfOpenUnity()
        => OpenUnityFilterOn
           && _matches.Count == 0
           && _moduleContext is { } context
           && SearchFilterPass.Run(_allItems, Modules, item => PassesBase(item) && MatchesQuery(item), context).Matches.Count > 0;

    /// <summary>最後の絞り込みで決めた、スイッチで絞っているときの0件の案内。</summary>
    private string? _openUnityEmptyHint;

    /// <summary>スイッチで絞っているときの0件の案内。null ならスイッチで絞っていない。</summary>
    private string? OpenUnityEmptyHint()
    {
        if (!OpenUnityFilterOn)
        {
            return null;
        }

        if (_openProjects.Any(key => _projectContents.GetValueOrDefault(key) is not { IsScanning: false }))
        {
            return "開いているUnityの中を調べています。";
        }

        if (!EmptyBecauseOfOpenUnity())
        {
            // ほかの条件の0件。「条件をクリア」はスイッチを切らないので、全件には戻らないと言う
            return "「条件をクリア」で、開いているUnityに入っている商品すべてに戻ります";
        }

        return ProjectContentsModules().Any(module => module.IsActive)
            ? "「開いているUnity」と条件「Unityプロジェクト」の両方に入っている商品はありません。"
            : "開いているUnityのプロジェクトに入っている商品はありません。";
    }

    private bool ProjectContains(string project, string itemId, bool sameName)
        => _projectContents.TryGetValue(project, out var contents) && contents.Scan.Contains(itemId, sameName);

    private bool OpenProjectContains(string itemId, bool sameName)
        => _openProjects.Any(project => ProjectContains(project, itemId, sameName));

    private IEnumerable<ListModule> ProjectContentsModules()
        => Modules.OfType<ListModule>().Where(module => module.Kind == SearchModuleKind.UnityProjectContents);

    /// <summary>候補は Hub と VCC の一覧。開いている物を先に並べる（改変の紐付けの候補と同じ）。</summary>
    private IEnumerable<(string Text, string Key)> UnityProjectContentsCandidates()
    {
        EnsureUnityProjectsLoaded();
        return (_unityProjects ?? [])
            .Where(project => project.Exists)
            .OrderByDescending(project => project.IsOpen)
            .ThenByDescending(project => project.LastWrite)
            .ThenBy(project => project.Name, StringComparer.CurrentCulture)
            .Select(project => (
                project.IsOpen ? $"{project.Name}（開いています・{project.Path}）" : $"{project.Name}（{project.Path}）",
                UnityProjectPath.Normalize(project.Path) ?? project.Path));
    }

    /// <summary>一覧はファイルを読むので、画面のスレッドの外で読む。読めたら候補を入れ直す。</summary>
    private void EnsureUnityProjectsLoaded()
    {
        if (_unityProjects is null && !_loadingUnityProjects)
        {
            _loadingUnityProjects = true;
            LoadUnityProjectsAsync().Forget();
        }
    }

    /// <summary>候補の一覧を読み直す（プロジェクトの開け閉めは、この画面の外で変わる）。</summary>
    private void ForgetUnityProjects()
    {
        _unityProjects = null;
        _unityProjectsGeneration++;
    }

    private async Task LoadUnityProjectsAsync()
    {
        var generation = _unityProjectsGeneration;
        IReadOnlyList<UnityProjectCandidate> projects;
        try
        {
            projects = await Task.Run(() => _services.DiscoverUnityProjects());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めなくても空で置く。null のままだと読み直しを繰り返す
            projects = [];
        }

        _loadingUnityProjects = false;
        if (generation != _unityProjectsGeneration)
        {
            EnsureUnityProjectsLoaded();
            return;
        }

        _unityProjects = projects;
        foreach (var module in ProjectContentsModules().ToList())
        {
            RefreshModuleSource(module);
        }
    }

    /// <summary>
    /// 戻した条件が「今開いているプロジェクト」を見るなら、一覧を読むのと並べて確かめ始める。
    /// 分かる前は絞らないので、確かめる前に一覧を出すと、全商品が一瞬出てから絞られる。
    /// </summary>
    private void StartFirstOpenProjectsCheck()
    {
        if (_openUnityOnly || ProjectContentsModules().Any(module => module.IsEnabled && module.IncludeOn))
        {
            _firstOpenProjectsCheck = RefreshOpenProjectsAsync();
            _firstOpenProjectsCheck.Forget();
        }
        else
        {
            // 待たずに見るだけ。スイッチを押せるか（灰色か）を決める
            RefreshOpenProjectsAsync().Forget();
        }
    }

    /// <summary>最初の確認を待つ。プロセスの列挙が詰まっても一覧は出す（1秒は、ふつう数十ミリ秒で済む確認に対する余裕）。</summary>
    private async Task WaitFirstOpenProjectsCheckAsync()
    {
        if (_firstOpenProjectsCheck is { } check)
        {
            _firstOpenProjectsCheck = null;
            await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>
    /// 窓が前に戻った・検索へ戻った。Unity を開いた・閉じたかもしれない。
    /// スイッチはいつも見えているので、条件が無くても見直す（灰色を解く）。プロセスを見るだけで、裏で数十ミリ秒
    /// </summary>
    internal void NoteOpenProjectsMaybeChanged() => RefreshOpenProjectsAsync().Forget();

    /// <summary>開いているプロジェクトを見直す。プロセスと一覧のファイルを見るので裏で。変わったら絞り直す。</summary>
    private async Task RefreshOpenProjectsAsync()
    {
        _openProjectsChecked = true;
        var generation = ++_openProjectsGeneration;
        var (paths, unknown) = await Task.Run(() =>
        {
            var found = new List<string>();
            var names = new List<string>();
            foreach (var editor in _services.OpenUnityEditors())
            {
                if (UnityEditors.PathOf(editor) is { } path)
                {
                    found.Add(UnityProjectPath.Normalize(path) ?? path);
                }
                else
                {
                    names.Add(editor.ProjectName ?? "名前の分からないプロジェクト");
                }
            }

            return ((IReadOnlyList<string>)found, (IReadOnlyList<string>)names);
        });

        if (generation != _openProjectsGeneration)
        {
            return;
        }

        var same = paths.SequenceEqual(_openProjects, StringComparer.OrdinalIgnoreCase) && unknown.SequenceEqual(_unknownOpenProjects);
        _openProjects = paths;
        _unknownOpenProjects = unknown;
        foreach (var module in ProjectContentsModules())
        {
            module.IncludeUnavailable = paths.Count == 0;
        }

        if (same)
        {
            RefreshProjectStatus();
            return;
        }

        NotifyOpenUnitySwitch();
        ApplyFilters();
    }

    /// <summary>効いている札と開いているプロジェクトのうち、まだ調べていない物を裏で調べ始める。</summary>
    private void EnsureProjectContents()
    {
        foreach (var module in ProjectContentsModules())
        {
            module.IncludeUnavailable = _openProjects.Count == 0;
        }

        // 前回の状態を戻したときは、まだ開いているプロジェクトを見ていない
        if (!_openProjectsChecked && ProjectContentsModules().Any())
        {
            RefreshOpenProjectsAsync().Forget();
        }

        // 読み込む前に調べると、商品が0件の結果を覚えてしまう
        if (_allItems.Count == 0)
        {
            return;
        }

        var keys = ProjectContentsModules()
            .Where(module => module.IsEnabled)
            .SelectMany(module => module.Chips.Select(chip => chip.Key).Concat(module.IncludeOn ? _openProjects : []))
            .Concat(OpenUnityFilterOn ? _openProjects : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var key in keys)
        {
            if (!_projectContents.ContainsKey(key))
            {
                var contents = new ProjectContents();

                // 画面のスレッドで読むのは、この後すぐの絞り込みに間に合わせて0件を見せないため。
                // 控えは見つかった商品の分だけの小さなファイルで、読むのはプロジェクトごとに1回だけ
                if (_savedScansRead.Add(key) && _services.UnityScans.Load(key) is { } saved)
                {
                    contents.Scan = saved;
                }

                _projectContents[key] = contents;
                ScanProjectAsync(key, contents).Forget();
            }
        }

        RefreshProjectStatus();
    }

    private async Task ScanProjectAsync(string project, ProjectContents contents)
    {
        var items = _allItems.Where(item => item.IsOwned).ToList();
        contents.Total = items.Count;
        var token = contents.Cancel.Token;
        var shown = contents.Scan;
        IProgress<int> progress = new Progress<int>(done =>
        {
            contents.Done = done;
            RefreshProjectStatus();
        });

        try
        {
            switch (await Task.Run(() => _services.ProjectFolderState(project)))
            {
                case DiskAnswer.Present:
                    contents.Scan = await Task.Run(
                        () =>
                        {
                            var scan = _services.ScanUnityProject(project, items, progress, token);

                            // 「読み直す」の後に終わった古い調べが、新しい結果の控えを上書きしないように
                            token.ThrowIfCancellationRequested();
                            _services.UnityScans.Save(project, scan);
                            return scan;
                        },
                        token);
                    break;

                case DiskAnswer.Missing:
                    // 控えは消さない（外付けのドライブなら、つなぎ直せばまた使える）。今は無いので見せもしない
                    contents.Scan = UnityProjectScan.Empty;
                    contents.Problem = $"「{ProjectNameOf(project)}」のフォルダが見つかりません。";
                    break;

                default:
                    // 拒まれた・ネットワークが答えない。無いとは言えないので、前回の結果は残す（調べに失敗したときと同じ）
                    contents.Problem = CouldNotScanText(project);
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 「読み直す」で取り消した。新しい調べが結果を出す
            return;
        }
        catch (Exception exception)
        {
            // 何で失敗しても「調べています」のまま残さない（裏の作業なので、投げ直しても画面には届かない）
            if (exception is not (IOException or UnauthorizedAccessException or InvalidDataException))
            {
                Chmonos.Core.Diagnostics.AppLog.Error("条件「Unityプロジェクト」の調べ", exception);
            }

            contents.Problem = CouldNotScanText(project);
        }

        contents.IsScanning = false;

        // 調べている間に「読み直す」が押されていれば、古い結果で絞り直さない
        if (_projectContents.TryGetValue(project, out var current) && ReferenceEquals(current, contents))
        {
            if (contents.Scan.FindsSameAs(shown))
            {
                // 控えのとおりだった。組み直すと、見ている一覧が動く
                RefreshProjectStatus();
            }
            else
            {
                ApplyFilters();
            }
        }
    }

    private static string CouldNotScanText(string project)
        => $"「{ProjectNameOf(project)}」の中を調べられませんでした。「読み直す」でやり直せます。";

    private static string ProjectNameOf(string project) => Path.GetFileName(project.TrimEnd('\\', '/'));

    /// <summary>札ごとの状況を条件の下に出す。調べている間の0件を「入っていない」と読ませない。</summary>
    private void RefreshProjectStatus()
    {
        NotifyOpenUnitySwitch();

        // 調べ終えて結果が控えと同じなら絞り直さない（ScanProjectAsync）。「調べています」の0件の案内はここで直す
        if (_matches.Count == 0 && OpenUnityFilterOn)
        {
            _openUnityEmptyHint = OpenUnityEmptyHint();
            OnPropertyChanged(nameof(EmptyHint));
        }
        foreach (var module in ProjectContentsModules())
        {
            module.SetOpenProjects(_openProjects
                .Where(project => module.Chips.All(chip => !string.Equals(chip.Key, project, StringComparison.OrdinalIgnoreCase)))
                .ToList());

            var lines = new List<string>();
            if (_unknownOpenProjects.Count > 0)
            {
                lines.AddRange(_unknownOpenProjects.Select(name => $"Unityの「{name}」は場所が分からないので調べられません。"));
            }
            else if (_openProjectsChecked && _openProjects.Count == 0)
            {
                lines.Add("Unityが開いていません。");
            }

            var keys = module.Chips.Select(chip => chip.Key).Concat(module.IncludeOn ? _openProjects : []).Distinct(StringComparer.OrdinalIgnoreCase);
            lines.AddRange(keys
                .Select(key => _projectContents.GetValueOrDefault(key) is { } contents
                    ? contents.Problem ?? (contents.IsScanning
                        ? $"「{ProjectNameOf(key)}」の中を調べています…（{contents.Done}/{contents.Total}）"
                        : null)
                    : null)
                .OfType<string>());
            module.StatusText = string.Join('\n', lines);
        }
    }

    /// <summary>覚えた結果を捨てて調べ直す。</summary>
    private void ReloadProjectContents()
    {
        foreach (var contents in _projectContents.Values)
        {
            contents.Cancel.Cancel();
        }

        _projectContents.Clear();
        ForgetUnityProjects();
        foreach (var module in ProjectContentsModules().ToList())
        {
            RefreshModuleSource(module);
        }

        ApplyFilters();
        RefreshOpenProjectsAsync().Forget();
    }

    /// <summary>
    /// スイッチだけで絞っているときの「読み直す」。条件の中の同じボタンは、条件を足していないと無いので、
    /// Unity へ入れ直した後に取り直す道が無くなる
    /// </summary>
    public RelayCommand ReloadOpenUnityCommand => _reloadOpenUnityCommand ??= new RelayCommand(ReloadProjectContents);

    private RelayCommand? _reloadOpenUnityCommand;
}
