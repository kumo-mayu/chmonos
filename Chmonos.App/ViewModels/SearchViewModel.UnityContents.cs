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

    public RelayCommand ShowOpenUnityContentsCommand { get; }

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

    /// <summary>窓が前に戻った。Unity を開いた・閉じたかもしれない。</summary>
    internal void NoteOpenProjectsMaybeChanged()
    {
        if (ProjectContentsModules().Any())
        {
            RefreshOpenProjectsAsync().Forget();
        }
    }

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var key in keys)
        {
            if (!_projectContents.ContainsKey(key))
            {
                var contents = new ProjectContents();
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
        IProgress<int> progress = new Progress<int>(done =>
        {
            contents.Done = done;
            RefreshProjectStatus();
        });

        try
        {
            if (await DiskCheck.FolderExistsAsync(project))
            {
                contents.Scan = await Task.Run(() => _services.ScanUnityProject(project, items, progress, token), token);
            }
            else
            {
                contents.Problem = $"「{ProjectNameOf(project)}」のフォルダが見つかりません。";
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

            contents.Problem = $"「{ProjectNameOf(project)}」の中を調べられませんでした。「読み直す」でやり直せます。";
        }

        contents.IsScanning = false;

        // 調べている間に「読み直す」が押されていれば、古い結果で絞り直さない
        if (_projectContents.TryGetValue(project, out var current) && ReferenceEquals(current, contents))
        {
            ApplyFilters();
        }
    }

    private static string ProjectNameOf(string project) => Path.GetFileName(project.TrimEnd('\\', '/'));

    /// <summary>札ごとの状況を条件の下に出す。調べている間の0件を「入っていない」と読ませない。</summary>
    private void RefreshProjectStatus()
    {
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
    /// 「開いているUnityで絞る」：条件が無ければ足し、札を外して「今開いているプロジェクト」だけにする（ユーザ判断 2026-11-01）。
    /// 複数開いていても選ばせない（どれか1つを見たいなら、条件の中のプロジェクトのボタンで足せる）。開いていない・場所が分からないことは条件の中の文が言う。
    /// </summary>
    private void ShowOpenUnityContents()
    {
        ForgetUnityProjects();
        var module = ProjectContentsModules().FirstOrDefault()
            ?? (ListModule)AddModule(SearchModuleKind.UnityProjectContents, apply: false);

        // 切ってある・除く・畳んである条件のままだと、押しても中身が見えない
        module.IsEnabled = true;
        module.IsExcluded = false;
        module.IsCollapsed = false;
        module.IncludeOn = true;
        module.ClearKeys();
        RefreshModuleSource(module);
        ModuleAdded?.Invoke(module);
        RefreshOpenProjectsAsync().Forget();
    }
}
