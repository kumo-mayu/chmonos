using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>改変の画面：Unity・VCC（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ModificationHubViewModel
{
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
        if (!await Core.Services.DiskCheck.FolderExistsAsync(project))
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
                    + "「Unityを開く」で開いてからもう一度押すと、Unity のプロジェクトタブで示します。";
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
            Status = $"「{row.Name}」は「{projectName}」にまだ入っていません。取り込むには、先に「Unityを開く」でプロジェクトを開いてください。";
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
            _services.Recent.TouchAsync(item.Id, RecentKind.Used).Forget();
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
}
