using System.IO;
using System.Windows;
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
    private sealed class ProjectContents
    {
        public IReadOnlySet<string> ItemIds { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        public bool IsScanning { get; set; } = true;

        public int Done { get; set; }

        public int Total { get; set; }

        public string? Problem { get; set; }

        public CancellationTokenSource Cancel { get; } = new();
    }

    private readonly Dictionary<string, ProjectContents> _projectContents = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<UnityProjectCandidate>? _unityProjects;
    private bool _loadingUnityProjects;

    public RelayCommand ShowOpenUnityContentsCommand { get; }

    private bool ProjectContains(string project, string itemId)
        => _projectContents.TryGetValue(project, out var contents) && contents.ItemIds.Contains(itemId);

    private IEnumerable<ListModule> ProjectContentsModules()
        => Modules.OfType<ListModule>().Where(module => module.Kind == SearchModuleKind.UnityProjectContents);

    /// <summary>候補は Hub と VCC の一覧。開いている物を先に並べる（見たいのは大抵これ。改変の紐付けの候補と同じ）。</summary>
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

    private async Task LoadUnityProjectsAsync()
    {
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

        _unityProjects = projects;
        _loadingUnityProjects = false;
        foreach (var module in ProjectContentsModules().ToList())
        {
            RefreshModuleSource(module);
        }
    }

    /// <summary>効いている札のうち、まだ調べていないプロジェクトを裏で調べ始める。</summary>
    private void EnsureProjectContents()
    {
        // 読み込む前に調べると、商品が0件の結果を覚えてしまう
        if (_allItems.Count == 0)
        {
            return;
        }

        foreach (var key in ProjectContentsModules()
            .Where(module => module.IsEnabled)
            .SelectMany(module => module.Chips)
            .Select(chip => chip.Key)
            .ToList())
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
        var items = _allItems.Where(item => item.HasOwnedFiles).ToList();
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
                var matches = await Task.Run(() => _services.ScanUnityProject(project, items, progress, token), token);
                contents.ItemIds = matches.Select(match => match.ItemId).ToHashSet(StringComparer.Ordinal);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
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
            module.StatusText = string.Join('\n', module.Chips
                .Select(chip => _projectContents.GetValueOrDefault(chip.Key) is { } contents
                    ? contents.Problem ?? (contents.IsScanning
                        ? $"「{ProjectNameOf(chip.Key)}」の中を調べています…（{contents.Done}/{contents.Total}）"
                        : null)
                    : null)
                .OfType<string>());
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
        _unityProjects = null;
        foreach (var module in ProjectContentsModules().ToList())
        {
            RefreshModuleSource(module);
        }

        ApplyFilters();
    }

    /// <summary>
    /// メニューから条件を足したとき、開いている Unity が1つならそれを入れる。
    /// 複数なら人が候補から選ぶ（開いている物は候補の先頭にある）。
    /// </summary>
    private async Task FillOpenProjectAsync(ListModule module)
    {
        var editors = await Task.Run(() => _services.OpenUnityEditors());
        if (editors.Count != 1 || await Task.Run(() => UnityEditors.PathOf(editors[0])) is not { } path)
        {
            return;
        }

        // 待っている間に人が選んだ・条件を外したなら、触らない
        if (module.Chips.Count == 0 && Modules.Contains(module))
        {
            module.AddKey(UnityProjectPath.Normalize(path) ?? path);
        }
    }

    /// <summary>「開いているUnityの中身を表示」：条件が無ければ足し、開いているプロジェクトだけに置き換えて絞る。</summary>
    private async Task ShowOpenUnityContentsAsync()
    {
        const string title = "開いているUnityの中身";
        var editors = await Task.Run(() => _services.OpenUnityEditors());
        if (editors.Count == 0)
        {
            Notice.Show("Unityが開いていません。プロジェクトを開いてから、もう一度押してください。", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if ((editors.Count == 1 ? editors[0] : _services.ChooseUnityEditor(title, editors)) is not { } editor)
        {
            return;
        }

        if (await Task.Run(() => UnityEditors.PathOf(editor)) is not { } path)
        {
            // 引けなかった理由は、入っている物の一覧の名前で言う（入っていない物まで見たかのように言わない）
            var tools = await Task.Run(() => _services.DetectUnityTools());
            Notice.Show(
                $"Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」の中を調べられません。"
                + UnityToolsText.NotListed(tools, _services.Settings.ProjectManager),
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _unityProjects = null;
        var module = ProjectContentsModules().FirstOrDefault()
            ?? (ListModule)AddModule(SearchModuleKind.UnityProjectContents, apply: false);

        // 切ってある・除く・畳んである条件のままだと、押しても中身が見えない
        module.IsEnabled = true;
        module.IsExcluded = false;
        module.IsCollapsed = false;
        module.ReplaceKeys(UnityProjectPath.Normalize(path) ?? path, null);
        RefreshModuleSource(module);
        ModuleAdded?.Invoke(module);
    }
}
