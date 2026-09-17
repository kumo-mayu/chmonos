using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定画面：チェックした物へのまとめた操作（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
    public int CheckedCount => Files.Count(row => row.IsSelected);

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => $"{CheckedCount} 件を選択中";

    public string AssignCheckedText => $"選択した {CheckedCount} 件をこのIDで確定";

    public string ExcludeCheckedText => $"選択した {CheckedCount} 件を管理対象から外す";

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(AssignCheckedText));
        OnPropertyChanged(nameof(ExcludeCheckedText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 同じフォルダのものをまとめて選ぶ。
    /// 1つのアーカイブを展開した中身が並んでいることが多く、
    /// それらは1件ずつ判断する必要がないため。
    /// </summary>
    private void SelectFolder(object? parameter)
    {
        if (parameter is not string directory)
        {
            return;
        }

        foreach (var row in Files.Where(row =>
            string.Equals(row.DirectoryText, directory, StringComparison.OrdinalIgnoreCase)))
        {
            row.IsSelected = true;
        }

        // 右にそのフォルダの話（展開元のzipが無いときの片付け方など）を出すため、フォルダのファイルを1つ選ぶ（ユーザ指示 2026-09-17）
        FocusFolder(directory);
    }

    private string? _activeGroup;

    /// <summary>
    /// まとめて扱っている元zipの束。null なら選んだ1件だけを扱う。
    /// 確定・管理から外すがこの束の全件に効く。元zipが単位の基本で、
    /// 1件ずつ扱いたいときは束を開いて行を選ぶ（行を選ぶと束は外れる）。
    /// </summary>
    public string? ActiveGroup
    {
        get => _activeGroup;
        private set
        {
            if (SetField(ref _activeGroup, value))
            {
                OnPropertyChanged(nameof(HasActiveGroup));
                OnPropertyChanged(nameof(ActiveGroupText));
                OnPropertyChanged(nameof(AssignOutcomeText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveGroup => ActiveGroup is not null;

    public string ActiveGroupText => ActiveGroup is null
        ? string.Empty
        : $"元zip「{ActiveGroup}」を展開した中身 {ActiveRows.Count} 件をまとめて扱っています"
          + (ActiveRows.Count(row => !MatchesFilter(row)) is var hidden && hidden > 0 ? $"（うち {hidden} 件は検索で隠れています）" : string.Empty);

    /// <summary>確定・管理から外すの対象。束を選んでいればその全件、でなければ選んだ1件。</summary>
    private IReadOnlyList<UnresolvedRow> ActiveRows => ActiveGroup is { } key
        ? Files.Where(row => row.HasOrigin && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase)).ToList()
        : Selected is null ? [] : [Selected];

    private void SelectGroup(object? parameter)
    {
        if (parameter is not string key)
        {
            return;
        }

        var first = Files.FirstOrDefault(row =>
            row.HasOrigin && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase));
        if (first is null)
        {
            return;
        }

        // 行を選ぶと束は外れるので、先に代表の行を選んでから束を立てる。
        // 代表の行は手掛かりの表示と検索の対象に使う（検索は元zipの名前で引くので、どの行でも同じ）
        Selected = first;
        ActiveGroup = key;
    }

    /// <summary>見えている行だけを選ぶ。探して絞っているときに、見えない行までまとめて外したり確定したりしないため。</summary>
    private void SelectAll()
    {
        foreach (var row in Files.Where(MatchesFilter))
        {
            row.IsSelected = true;
        }
    }

    private void ClearChecks()
    {
        foreach (var row in Files.Where(row => row.IsSelected))
        {
            row.IsSelected = false;
        }
    }

    private Task<bool> ExcludeCheckedAsync() => ExcludeRowsAsync(Files.Where(row => row.IsSelected).ToList(), "まとめて管理対象から外す");

    /// <summary>
    /// 元のzipが残っている中身を「元zipとして扱う」：元のzipの行を選ぶ。そのままzipで登録すれば、中身は一覧から消える。
    /// 前は中身を管理対象から外していたが、外した記録は残り続けるので、後でzipを消しても中身が戻らなかった
    /// （ユーザ判断 2026-09-17：登録済みのzipの中身は出さないだけにする、と揃えた）。
    /// </summary>
    /// <param name="parameter">中身の束の鍵（zipの名前）か、1行で出ている中身の行。</param>
    private void TreatAsOriginZip(object? parameter)
    {
        var content = parameter switch
        {
            UnresolvedRow row when row.HasOriginZip => row,
            string key => Files.FirstOrDefault(row => row.HasOriginZip
                && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
        if (content is null)
        {
            return;
        }

        Selected = content;
        UseOriginZip();
    }

    /// <summary>決めた行をまとめて管理対象から外す。外す前に件数と名前を見せて聞く。外したら true。</summary>
    /// <param name="lead">確認の窓の頭に置く、なぜ外すのかの一文（無ければ出さない）。</param>
    private async Task<bool> ExcludeRowsAsync(List<UnresolvedRow> targets, string title, string? lead = null)
    {
        if (targets.Count == 0)
        {
            return false;
        }

        var sample = string.Join("\n", targets.Take(8).Select(row => $"・{row.FileName}"));
        if (targets.Count > 8)
        {
            sample += $"\n…ほか {targets.Count - 8} 件";
        }

        var answer = System.Windows.MessageBox.Show(
            (lead is null ? string.Empty : lead + "\n\n")
            + $"{targets.Count} 件を管理対象から外します。\n\n{sample}\n\n"
            + "ファイル自体は消しません。設定の「隠したもの」から戻せます。",
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            foreach (var row in targets)
            {
                await _services.Commands.ExecuteAsync(
                    new UiCommand.ExcludeFile(row.File.Hash, row.File.Paths, "未確定画面からまとめて除外"));
            }

            RemoveRows(targets);
            RememberExcluded(targets);
            StatusText = $"{targets.Count} 件を管理対象から外しました。";
            return true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    /// <summary>
    /// 選んだ複数のファイルを同じ商品IDへ確定する。
    /// 1商品に複数のファイル（本体zipと差分、psdなど）が付くことは普通にある。
    /// 2件目以降はローカルのitemへ追加されるだけで、BOOTHへは行かない。
    /// </summary>
    private async Task AssignCheckedAsync()
    {
        if (Preview is null)
        {
            return;
        }

        // zipを展開した中身は、元のzipの単位に揃える（元のzipが未確定にあれば止め、無ければ同じzipの中身全件まで広げる）
        var (targets, blocked) = ExpandToZipUnits(Files.Where(row => row.IsSelected).ToList());
        if (blocked is not null)
        {
            StatusText = blocked;
            OnPropertyChanged(nameof(HasStatus));
            return;
        }

        if (targets.Count == 0)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"{targets.Count} 件を「{Preview.Name}」（ID {Preview.Id}）のファイルとして確定します。\n\n"
            + "同じ商品のファイルであることを確認してください。",
            "まとめて確定",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var settled = new List<UnresolvedRow>();
            foreach (var row in targets)
            {
                var result = await _services.Commands.ExecuteAsync(
                    new UiCommand.AssignItemId(row.File.Hash, Preview.Id));

                if (result is not CommandResult.Failed)
                {
                    settled.Add(row);
                }
            }

            if (!_settledItemIds.Contains(Preview.Id))
            {
                _settledItemIds.Add(Preview.Id);
            }

            RemoveRows(settled);
            StatusText = settled.Count == targets.Count
                ? $"{settled.Count} 件を確定しました。"
                : $"{settled.Count} / {targets.Count} 件を確定しました（残りは失敗）。";
            HideCoveredContentsAsync().Forget();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }
}
