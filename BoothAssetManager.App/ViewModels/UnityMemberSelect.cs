using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 改変の使ったもの1件を Unity で示す（ユーザ仕様 2026-09-13）。改変の画面の「Unityで選択」と、
/// 改変の詳細の「Unity ▾」の「選択」（ユーザ指示 2026-09-14）で同じ道を通すため、改変の画面から切り出した（中身は変えていない）。
///
/// **入っていれば、入り先のルートフォルダ（作者名のフォルダなど）の名前をプロジェクトタブの検索欄に入れる。**
/// どのフォルダがそのアセットかを示せれば十分（ユーザ判断）なので、選んで開くところまではしない。
/// 入っていなければ、取り込むか聞く。どちらもプロジェクトが開いている必要がある。
/// 入っているかは、送ったファイル（無ければ今ある zip）の中身のパスがプロジェクトにあるかで見る（プロジェクトの中を調べると同じ数え方）。
/// </summary>
internal static class UnityMemberSelect
{
    /// <param name="index">改変の中の位置（何件目か）。選んだファイルを記録するときに使う。</param>
    /// <param name="fileText">使ったファイルの言い方（取り込むか聞くときに添える）。</param>
    /// <param name="setStatus">結果を言う先（呼んだ画面の1行）。</param>
    /// <returns>どのファイルを使ったかを記録した（呼んだ側は並びを読み直す）。</returns>
    public static async Task<bool> RunAsync(
        AppServiceContainer services,
        ModificationRecord record,
        int index,
        ModificationMember member,
        string name,
        string fileText,
        ItemRecord? item,
        Action<string> setStatus)
    {
        const string title = "Unityで選択";

        if (record.UnityProject is not { } project)
        {
            setStatus("この改変はUnityプロジェクトに紐付いていません。改変を開いて、右側の「Unityプロジェクト」から紐付けてください。");
            return false;
        }

        var projectName = ModificationHubViewModel.ProjectNameOf(project);
        if (!await DiskCheck.FolderExistsAsync(project))
        {
            setStatus($"紐付けたプロジェクト「{projectName}」のフォルダが見つかりません。");
            return false;
        }

        if (item is null)
        {
            setStatus($"「{name}」は手元にありません。");
            return false;
        }

        var packages = ModificationViewModel.PackagesFor(item, member);
        if (packages.Count == 0)
        {
            setStatus($"「{name}」には、Unityに入れられるファイル（zip の中の unitypackage）が手元にありません。");
            return false;
        }

        // 場所が分かるエディタは場所で照らす（名前だけだと「cleanTest - コピー」と「cleanTest」を取り違えた。2026-09-19）
        var editor = UnityEditors.Open().FirstOrDefault(candidate => candidate.ProjectPath is { } path
            ? PathText.Same(path, project.TrimEnd('\\', '/'))
            : string.Equals(candidate.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

        // 開いている印はあるのに窓が特定できない（起動中・コンパイル中で題が読めない）
        if (editor is null && UnityProjects.IsProjectOpen(project))
        {
            setStatus($"「{projectName}」は開いていますが、読み込み中のようです。落ち着いてから、もう一度押してください。");
            return false;
        }

        // 開いていなければ、中を調べずにそれだけを言う（ユーザ判断 2026-09-19：入っているかどうかより先に、開いていないことが要る。
        // 改変に入っている時点で使う・使ったと分かっているので、入っていないときに取り込むか聞くのは今のままでよい）
        if (editor is null)
        {
            setStatus($"プロジェクト「{projectName}」がまだ開かれていません。");
            return false;
        }

        if (await ShowIfPresentAsync(project, projectName, editor, packages, item.Id, setStatus))
        {
            return false;
        }

        // 送信は1列に限る。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (UnityImportQueue.IsRunning)
        {
            Services.Notice.Show(UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return false;
        }

        IReadOnlyList<UnityPackageEntry> toSend = packages;
        var recorded = false;
        if (member.FileHash is null && packages.Count > 1 && PackageChoiceSection.Build(item, index) is { } choice)
        {
            // どのファイルを使ったか記録が無く、送れる物が2つ以上ある。全部送ると古い版や別の種類まで入るので選ばせ、
            // 選んだ物をこの行に記録する（ユーザ判断 2026-09-13）
            var model = new PickPackagesDialogViewModel(
                title,
                $"「{name}」は、Unityの「{projectName}」にまだ入っていません。取り込む物を選んでください。",
                [choice],
                othersCount: 0,
                records: true);
            if (!Views.PickPackagesDialog.Ask(model))
            {
                setStatus(string.Empty);
                return false;
            }

            toSend = choice.CheckedPackages;
            var result = await services.Commands.ExecuteAsync(
                new UiCommand.RecordModificationMemberFiles(record.Id, index, choice.CheckedMembers));
            recorded = result is not CommandResult.Failed;
        }
        else
        {
            var answer = Services.Notice.Show(
                $"「{name}」（{fileText}）は、Unityの「{projectName}」にまだ入っていません。Unityへ送りますか？\n\n"
                + "Unity側で取り込む内容の一覧が出るので、そこで確認してから取り込めます。",
                title,
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.OK);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                setStatus(string.Empty);
                return false;
            }
        }

        var outcomes = await UnityImportQueue.RunAsync(
            editor.ProcessId, toSend, new Progress<UnityQueueProgress>(report => setStatus(report.Text)), CancellationToken.None);

        // 「使った」の足跡。Cancel された物は入っていないので付けない（ほかの送り方と同じ）
        if (outcomes.Any(outcome => outcome.Opened && !outcome.Cancelled))
        {
            services.Recent.TouchAsync(item.Id, RecentKind.Used).Forget();
        }

        var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
        setStatus(failed.Count > 0
            ? $"Unityへ送れませんでした（{failed[0].Problem}）。"
            : outcomes.All(outcome => outcome.Cancelled)
                ? "Cancel されたので、入っていません。"
                : outcomes.All(outcome => outcome.AlreadyPresent)
                    ? $"「{projectName}」には既に全部入っていました。もう一度押すと、プロジェクトタブで示します。"
                    : $"「{projectName}」へ送りました。入った後にもう一度押すと、プロジェクトタブで示します。");

        return recorded;
    }

    /// <summary>
    /// プロジェクトの中を調べ、**入っていれば**入り先のフォルダを Unity のプロジェクトタブで示して true を返す（結果は <paramref name="setStatus"/> へ）。
    /// 入っていなければ何も言わずに false——入っていないときにどうするか（取り込むか聞く・言うだけ）は呼ぶ側で決める。
    /// 改変の「選択」と商品ページの「Unity ▾」の「選択」（ユーザ指示 2026-09-19）で同じ道を通す
    /// </summary>
    public static async Task<bool> ShowIfPresentAsync(
        string project,
        string projectName,
        OpenUnityEditor? editor,
        IReadOnlyList<UnityPackageEntry> packages,
        string itemId,
        Action<string> setStatus)
    {
        setStatus($"「{projectName}」の中を調べています…");
        var (roots, present) = await Task.Run(() =>
        {
            var paths = packages.SelectMany(UnityHandoff.ReadAssetPaths).ToList();
            var matches = UnityProjectMatcher.Match(
                project, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [itemId] = paths });
            // 入り先の頭の記号（_FUKA）を利用者が消していれば、実際の名前（FUKA）で探す（UnityFolderNames）
            var children = UnityFolderNames.DiskChildren(project);
            var roots = UnityHandoff.DestinationRoots(paths).Select(root => UnityFolderNames.ResolveRoot(root, children)).ToList();
            return (roots, matches.FirstOrDefault()?.Present ?? 0);
        });

        if (present == 0)
        {
            setStatus(string.Empty);
            return false;
        }

        if (roots.Count == 0)
        {
            setStatus("入り先のフォルダを読めませんでした。");
            return true;
        }

        var root = roots[0];
        var folder = root.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        if (editor is null)
        {
            setStatus($"「{projectName}」の {root} に入っています。プロジェクトが開いていないので、"
                + "「Unityを開く」で開いてからもう一度押すと、Unity のプロジェクトタブで示します。");
            return true;
        }

        // 名前ではなくパスで渡す。同じ名前のフォルダが別の場所にあると、名前では取り違える（§13-7）
        var outcome = await UnityProjectTab.SelectFolderAsync(editor.ProcessId, project, root);
        setStatus(outcome switch
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
        });
        return true;
    }
}
