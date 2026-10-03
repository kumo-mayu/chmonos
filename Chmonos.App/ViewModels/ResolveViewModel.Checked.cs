using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>未確定画面：チェックした物（選んだ物）。選んでいる間は、今の対象がチェックした全部になる（ResolveViewModel.Target.cs）</summary>
public sealed partial class ResolveViewModel
{
    public int CheckedCount => Files.Count(row => row.IsSelected);

    public bool HasChecked => CheckedCount > 0;

    private List<UnresolvedRow> CheckedRows => Files.Where(row => row.IsSelected).ToList();

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(LocalIdPreview));
        OnPropertyChanged(nameof(AssignOutcomeText));

        // 名前は人が書き換えていなければ、選んだ物に合わせて下書きし直す（書いた名前は消さない）
        if (LocalNameInput == _localNameDraft)
        {
            ResetLocalNameDraft();
        }

        RaiseTargetChanged();
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>押した人が下書きから書き換えたかを見分けるため、最後に入れた下書きを覚えておく。</summary>
    private string _localNameDraft = string.Empty;

    private void ResetLocalNameDraft()
    {
        _localNameDraft = LocalNameDraft();
        LocalNameInput = _localNameDraft;
    }

    /// <summary>
    /// 名前の下書き。1件のときは元zipが分かればその名前、無ければファイル名（中の1ファイルの名前 cloth.psd などより商品名に近い）。
    /// 選んでいるときは1件目に同じ決まりを当てる。ただし zip が無いフォルダの束だけを選んでいれば、そのフォルダの名前
    /// （展開したフォルダの名前が商品名で、中のファイルの名前は texture.png などになりやすい）
    /// </summary>
    private string LocalNameDraft()
    {
        var checkedRows = Files.Where(row => row.IsSelected).ToList();
        if (checkedRows.Count == 0)
        {
            return Selected is null ? string.Empty : NameDraftOf(Selected);
        }

        var first = checkedRows[0];
        if (first.IsArchiveContent
            && checkedRows.All(row => string.Equals(row.GroupKey, first.GroupKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Chmonos.Core.Resolution.FileNameQuery.ToNameDraft(Path.GetFileName(first.GroupKey));
        }

        return NameDraftOf(first);
    }

    private static string NameDraftOf(UnresolvedRow row)
        => Chmonos.Core.Resolution.FileNameQuery.ToNameDraft(row.Origin?.ArchiveName ?? row.FileName);

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
            string.Equals(row.GroupKey, directory, StringComparison.OrdinalIgnoreCase)))
        {
            row.IsSelected = true;
        }

        // 右にそのフォルダの話（展開元のzipが無いときの片付け方など）を出すため、フォルダのファイルを1つ選ぶ（ユーザ指示 2026-09-17）
        FocusFolder(directory);
    }

    private string? _activeGroup;

    /// <summary>
    /// まとめて扱っている元zipの束。null なら選んだ1件だけを扱う。
    /// 確定・「管理対象から除外する」がこの束の全件に効く。元zipが単位の基本で、
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
                OnPropertyChanged(nameof(AssignOutcomeText));
                RaiseTargetChanged();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveGroup => ActiveGroup is not null;

    /// <summary>選んでいないときの対象。束を立てていればその全件、でなければ選んだ1件。</summary>
    private IReadOnlyList<UnresolvedRow> ActiveRows => ActiveGroup is { } key
        ? Files.Where(row => string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase)).ToList()
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

    /// <summary>
    /// 元のzipが残っている中身を「元zipとして扱う」：元のzipの行を選ぶ。そのままzipで登録すれば、中身は一覧から消える。
    /// 前は中身を管理対象から除外していたが、外した記録は残り続けるので、後でzipを消しても中身が戻らなかった
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

    /// <summary>決めた行をまとめて管理対象から除外する。外す前に件数と名前を見せて聞く。外したら true。</summary>
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

        var answer = Services.Notice.Show(
            (lead is null ? string.Empty : lead + "\n\n")
            + $"{targets.Count} 件を管理対象から除外します。\n\n{sample}\n\n"
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
            await _services.Commands.ExecuteAsync(
                new UiCommand.ExcludeFiles([.. targets.Select(row => row.File)], "未確定画面からまとめて除外"));

            RemoveRows(targets);
            RememberExcluded(targets);
            ListNoticeText = $"{targets.Count} 件を管理対象から除外しました。";
            return true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    // ---- Esc で選択を解除（ユーザ指示 2026-09-20・M6。解除はボタンだけで、位置も画面ごとに違った） ----

    bool ISelectionScreen.HasSelection => HasChecked;

    void ISelectionScreen.ClearSelection() => ClearChecks();
}
