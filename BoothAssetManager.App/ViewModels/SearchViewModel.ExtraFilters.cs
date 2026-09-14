using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：積む条件とフォルダの木（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>積んだ条件。常設に置かないものはここへ足していく。</summary>
    public ObservableCollection<ExtraFilter> ExtraFilters { get; } = [];

    /// <summary>まだ積んでいない条件の名前。「条件を追加」の候補に出す。</summary>
    public ObservableCollection<string> AvailableExtraFilters { get; } = [];

    public RelayCommand AddExtraFilterCommand { get; }

    public bool HasExtraFilters => ExtraFilters.Count > 0;

    private void AddExtraFilter(string? label)
    {
        var entry = ExtraFilterCatalog.All.FirstOrDefault(candidate => candidate.Label == label);
        if (entry is not null)
        {
            AddExtraFilter(entry.Kind);
        }
    }

    private void AddExtraFilter(ExtraFilterKind kind, bool save = true)
    {
        if (ExtraFilters.Any(filter => filter.Kind == kind))
        {
            return;
        }

        // 自分で足したときは「この軸で選ぶ」という意思表示なので入りにする。
        // 起動時の復元は意思表示ではないので切りにする。
        // 入りのまま戻すと、起動した瞬間に0件になり、原因が積んだ条件の中にあると気付けない。
        var filter = new ExtraFilter { Kind = kind, IsOn = save };
        filter.Changed += ApplyFilters;
        filter.RemoveCommand = new RelayCommand(() => RemoveExtraFilter(filter));

        if (kind == ExtraFilterKind.Folder)
        {
            filter.Descended += () => RebuildFolderRows(filter);
            filter.PathMap = _services.Volumes.Current;
        }

        ExtraFilters.Add(filter);

        if (kind == ExtraFilterKind.Folder)
        {
            RebuildFolderRows(filter);
        }

        // **積んだ直後に候補を入れる。**入れないと「候補がありません」と出て、
        // 積んだのに何も選べない条件になる
        if (filter.IsSuggest)
        {
            RefreshExtraSuggestions();
        }

        RefreshAvailableExtraFilters();

        if (save)
        {
            SaveExtraFilterKinds();
            ApplyFilters();
        }
    }

    private void RemoveExtraFilter(ExtraFilter filter)
    {
        filter.Changed -= ApplyFilters;
        ExtraFilters.Remove(filter);
        RefreshAvailableExtraFilters();
        SaveExtraFilterKinds();
        ApplyFilters();
    }

    private void RefreshAvailableExtraFilters()
    {
        AvailableExtraFilters.Clear();
        foreach (var entry in ExtraFilterCatalog.All.Where(entry => ExtraFilters.All(f => f.Kind != entry.Kind)))
        {
            AvailableExtraFilters.Add(entry.Label);
        }

        OnPropertyChanged(nameof(HasExtraFilters));
    }

    /// <summary>
    /// 今いる階層の行を作り直す。
    ///
    /// 木は保存せず毎回ここで組む。1,300件でもパスの文字列を辿るだけなので軽い。
    /// </summary>
    private void RebuildFolderRows(ExtraFilter filter)
    {
        filter.Rows.Clear();
        filter.Crumbs.Clear();

        // パンくず。現在地であって条件ではないので、押すと移動するだけ
        filter.Crumbs.Add(new FolderCrumb
        {
            Label = "すべて",
            Path = null,
            IsLast = filter.CurrentPath is null,
            GoCommand = new RelayCommand(() => filter.CurrentPath = null),
        });

        if (filter.CurrentPath is { } current)
        {
            var walked = string.Empty;
            var segments = current.Split(System.IO.Path.DirectorySeparatorChar);

            for (var i = 0; i < segments.Length; i++)
            {
                walked = i == 0 ? segments[0] : walked + System.IO.Path.DirectorySeparatorChar + segments[i];
                var target = walked;

                filter.Crumbs.Add(new FolderCrumb
                {
                    Label = segments[i],
                    Path = target,
                    IsLast = i == segments.Length - 1,
                    GoCommand = new RelayCommand(() => filter.CurrentPath = target),
                });
            }
        }

        // 外付けのドライブ文字が変わった物は今の文字の下に出す（フォルダビューと同じ読み替え・ユーザ指示 2026-09-14）
        foreach (var node in Core.Services.FolderTree.Children(_allItems, filter.CurrentPath, _services.Volumes.Current))
        {
            var path = node.Path;

            var row = new FolderRow
            {
                Path = path,
                Name = node.Name,
                Count = node.ItemCount,
                CanDescend = node.CanDescend,
                RecordedLetters = node.RecordedLetters,

                CanAddToImport = !_services.Settings.ImportFolders.Contains(path, StringComparer.OrdinalIgnoreCase),
                IsSelected = filter.Selected.Contains(path, StringComparer.OrdinalIgnoreCase),
            };

            row.DescendCommand = new RelayCommand(() => filter.CurrentPath = path);
            row.OpenCommand = new RelayCommand(() => Shell.Reveal(path));
            row.AddToImportCommand = new RelayCommand(() => AddImportFolderAsync(path).Forget());
            row.Changed += () =>
            {
                if (row.IsSelected)
                {
                    filter.Add(path);
                }
                else
                {
                    filter.Remove(path);
                }
            };

            filter.Rows.Add(row);
        }

        MarkOfflineFolderRowsAsync(filter.Rows.ToList()).Forget();
    }

    /// <summary>
    /// 記録にはあるが今その場所が無い行（外付けを外したときなど）に印を付ける。
    /// 消さずに残す：「どこに置いたっけ」を一番知りたいのがこの状況。
    ///
    /// **在るかは画面のスレッドの外で見る**（技術的負債 4-2）。落ちたネットワークドライブは1回に数秒かかることがあり、
    /// 前は行を作るたびに（降りるたびに）画面が止まりえた。
    /// </summary>
    private static async Task MarkOfflineFolderRowsAsync(IReadOnlyList<FolderRow> rows)
    {
        var missing = await Task.Run(() => rows.Select(row => !Core.Services.DiskCheck.FolderExists(row.Path)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].IsOffline = missing[i];
        }
    }

    /// <summary>
    /// 取り込み元に足す。足すだけで、その場では読み込まない。
    /// そのフォルダの商品は既に登録済み（だから木に出ている）なので、
    /// 今すぐ読んでも新しく見つかるものはほぼ無い。
    /// 目的は今後の再スキャンと欠落検出の範囲に入れること。
    /// </summary>
    private async Task AddImportFolderAsync(string path)
    {
        if (_services.Settings.ImportFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSettings(settings =>
            settings.ImportFolders.Contains(path, StringComparer.OrdinalIgnoreCase)
                ? settings
                : settings with { ImportFolders = [.. settings.ImportFolders, path] }));

        ImportFolderNotice = $"「{path}」を取り込み元に足しました。次の取り込みから、このフォルダも見ます。";
        OnPropertyChanged(nameof(ImportFolderNotice));
        OnPropertyChanged(nameof(HasImportFolderNotice));

        foreach (var filter in ExtraFilters.Where(f => f.Kind == ExtraFilterKind.Folder))
        {
            RebuildFolderRows(filter);
        }
    }

    /// <summary>取り込み元に足した結果。押しても何も起きなかったように見えないよう出す。</summary>
    public string ImportFolderNotice { get; private set; } = string.Empty;

    public bool HasImportFolderNotice => ImportFolderNotice.Length > 0;

    /// <summary>積んでいる種類だけを設定へ書く。値は書かない。</summary>
    private void SaveExtraFilterKinds()
    {
        var kinds = ExtraFilters.Select(filter => filter.Kind.ToString()).ToList();
        _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
            state => state with { SearchExtraFilters = kinds })).Forget();
    }
}
