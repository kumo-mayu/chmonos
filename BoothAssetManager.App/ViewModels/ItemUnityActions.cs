using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 商品1件の unitypackage 1つを Unity へ送る・改変に足して送る・Unity で選択する。
/// 商品ページの「Unity ▾」と、検索などのカードの右クリックの「Unity」で同じ道を通す（ユーザ指示 2026-09-19：
/// 右クリックに Unity が無かった）。前は商品ページの画面の中にあり、商品ページを開かないと使えなかった。中身は変えていない
///
/// 結果の言い方は呼ぶ側が決める（<c>notify</c>）。商品ページは欄の下の1行、カードは知らせの窓
/// </summary>
internal static class ItemUnityActions
{
    /// <summary>
    /// 送り先のUnityを1つに絞る。絞れなければ理由を出して null を返す。
    ///
    /// **「Unityへ送る」と「改変に足して送る」で共通の門。**どちらでも同じ条件で
    /// 送れる／送れないが決まるべきで、片方だけ通ると挙動が読めなくなる。
    /// </summary>
    private static OpenUnityEditor? PickTarget(string title)
    {
        // 連続送りの最中は混ぜない。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (UnityImportQueue.IsRunning)
        {
            Services.Notice.Show(
                UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return null;
        }

        // 窓を名指しして送る道なので、複数開いていても選べば送れる（U14・ユーザ判断）。
        // 以前はファイルの関連付けに渡していて、どれに入るかを指名できず、2つ以上開いていると断っていた
        return UnityTargetPicker.Pick(title);
    }

    /// <summary>
    /// 窓の題から一覧で言い当てた場所を先に使う。一覧で当てられなかったときだけ、名前から引く。
    /// Hub にも VCC にも載っていないプロジェクトだと引けない（null）
    /// </summary>
    private static async Task<string?> ProjectPathOf(OpenUnityEditor editor)
        => editor.ProjectPath ?? await Task.Run(() => UnityEditors.PathOf(editor));

    /// <summary>1件を、選んだ Unity に送る（送る前に何をどこへ送るかを確かめる）。</summary>
    public static async Task SendAsync(
        AppServiceContainer services, ItemRecord item, UnityPackageEntry package, UnitySendUi? ui = null)
    {
        const string title = "Unityへ送る";

        if (PickTarget(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var answer = Services.Notice.Show(
            $"「{package.Name}」を、Unityの「{target}」に送ります。\n\n"
            + "Unity側で取り込む内容の一覧が表示されるので、そこで確認してから取り込めます。",
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            await SendOneAsync(services, item, editor, package, title, ui);
        }
    }

    /// <summary>
    /// 1件を、選んだ Unity の窓へ名指しで送る（U14）。検索の複数選択・改変と同じ道（1件だけの列）。
    /// 取り込みの終わりをログで見るので、Cancel されたかも分かる。
    /// </summary>
    /// <returns>取り込み画面を出せたか。</returns>
    private static async Task<bool> SendOneAsync(
        AppServiceContainer services, ItemRecord item, OpenUnityEditor editor, UnityPackageEntry package,
        string title, UnitySendUi? ui)
    {
        // 1件でも進み具合を出す（E10）。出す先を持っている画面だけが渡す
        ui?.Begin($"「{package.Name}」をUnityへ送っています…");
        IReadOnlyList<UnityQueueOutcome> outcomes;
        try
        {
            outcomes = await UnityImportQueue.RunAsync(
                editor.ProcessId, [package], ui?.Progress, CancellationToken.None);
        }
        finally
        {
            ui?.End();
        }

        var outcome = outcomes.FirstOrDefault();

        if (outcome is null || !outcome.Opened)
        {
            FrontNotice.Show(
                $"「{package.Name}」をUnityへ送れませんでした。\n\n{outcome?.Problem ?? "理由が分かりませんでした。"}",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // 「使った」の足跡。Unityへ送ったことが一番強い証拠（ユーザ判断）。
        // 取り込み画面で Cancel された物は入っていないので付けない（検索の複数選択と同じ扱い）
        if (!outcome.Cancelled)
        {
            services.Recent.TouchAsync(item.Id, RecentKind.Used).Forget();
        }

        return true;
    }

    /// <summary>
    /// 改変に足して送る。
    ///
    /// **「送る」と別の選択肢にしてある**（ユーザ判断）。送る前に「記録しますか」と
    /// 聞くと、記録を使っていない人の邪魔になる。分ければ、**選んだ人だけが記録の話に入る。**
    /// </summary>
    /// <returns>足した改変（足せなかった・やめたら null）。</returns>
    public static async Task<ModificationRecord?> SendWithRecordAsync(
        AppServiceContainer services, ItemRecord item, UnityPackageEntry package, NoticeSink notify, UnitySendUi? ui = null)
    {
        const string title = "改変に追加して送る";

        if (PickTarget(title) is not { } editor)
        {
            return null;
        }

        // 送り先のプロジェクトの改変に絞る。一覧に無いプロジェクトなら絞らずに全部出す（**推定で絞ると、正しい改変が消える**）
        var projectPath = await ProjectPathOf(editor);
        var records = projectPath is not null
            ? await services.Modifications.LoadForProjectAsync(projectPath)
            : (await services.Modifications.LoadAllAsync()).Modifications;

        var model = ModificationPicking.BuildDialog(
            services,
            title,
            $"「{package.Name}」を送って、改変に追加します。",
            projectPath is not null
                ? $"送り先：Unityの「{editor.ProjectName}」"
                : $"送り先：Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」"
                    + "（一覧に無いプロジェクトなので、改変はすべて表示しています）",
            records,
            existingLabel: "このプロジェクトの改変に追加",
            commitLabel: "追加して送る",
            emptyText: "このプロジェクトに紐付いた改変はまだありません。新しく作って、そこに追加できます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return null;
        }

        // **記録してから送る。**送るのは Unity 側の取り込み画面を待つので時間がかかり、
        // 途中で窓を閉じられることもある。先に記録を確定させておく方が失うものが少ない
        var owner = item.Local.OwnedFiles.FirstOrDefault(file => file.Hash == package.ZipHash);
        if (await CommitPickedAsync(services, item, model, title, projectPath, owner, package.EntryPath) is not { } record)
        {
            return null;
        }

        // 窓を名指しして送る（U14）。取り込み画面を出せなかったら、記録だけ済んだと正直に言う
        var sent = await SendOneAsync(services, item, editor, package, title, ui);
        notify(
            sent
            ? $"「{record.Name}」に追加して、Unityへ送りました。"
            : $"「{record.Name}」に追加しました。Unityへは送れませんでした。",
            failed: !sent);
        return record;
    }

    /// <summary>
    /// ダイアログの答えを記録に落とす。作る側なら先に作る。作れなかったときは理由を出して null を返す。
    /// どのファイル（バリエーション・unitypackage）を使ったかは、送ったときだけ分かる（送らないなら null のまま。**推定で埋めない**）
    /// </summary>
    public static async Task<ModificationRecord?> CommitPickedAsync(
        AppServiceContainer services,
        ItemRecord item,
        PickModificationDialogViewModel model,
        string title,
        string? project,
        LocalFileRecord? owner,
        string? package)
    {
        if (await ModificationPicking.ResolvePickedAsync(services, model, title, project) is not { } record)
        {
            return null;
        }

        await services.Commands.ExecuteAsync(
            new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new ModificationMember
                {
                    ItemId = item.Id,
                    VariationId = owner?.VariationId,
                    FileHash = owner?.Hash,
                    Package = package,
                    AddedAt = DateTimeOffset.Now,
                }));

        return record;
    }

    /// <summary>
    /// Unity で選択する（ユーザ指示 2026-09-19：改変の画面でできる「Unity のプロジェクトタブで示す」を商品ページ・カードにも）。
    /// 相手は送り先と同じ、いま開いている Unity。**入っていなければ言うだけで、取り込みには進まない**——
    /// 「示す」つもりで押した物が「取り込む」話にすり替わると意図と違う（動線の洗い出し A1 と同じ種類）。入れるなら「Unityへ送る」を選ぶ
    /// </summary>
    public static async Task SelectAsync(ItemRecord item, UnityPackageEntry package, NoticeSink notify)
    {
        const string title = "Unityで選択";

        // 選ぶ門は「送れません」と言うので、開いていないときはこちらで「示せない」と言う
        if (UnityEditors.Open().Count == 0)
        {
            notify("Unityが開いていないので、示せません。プロジェクトを開いてから、もう一度選んでください。", failed: true);
            return;
        }

        // 送るのではないので、連続送りの最中でも止めない（PickTarget は送る用の門）
        if (UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        if (await ProjectPathOf(editor) is not { } projectPath)
        {
            notify($"Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」の場所が分からないので、中を調べられません"
                + "（Unity HubにもVRChat Creator Companionにも載っていないプロジェクトです）。", failed: true);
            return;
        }

        var projectName = editor.ProjectName ?? System.IO.Path.GetFileName(projectPath);
        if (!await UnityMemberSelect.ShowIfPresentAsync(projectPath, projectName, editor, [package], item.Id, notify))
        {
            notify($"「{package.Name}」は、Unityの「{projectName}」にまだ入っていません。"
                + "入れるには「Unityへ送る」を選んでください。", failed: true);
        }
    }
}
