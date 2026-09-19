using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

/// <summary>商品ページ：Unityへ送る・改変に足す・使った改変（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    /// <summary>
    /// 送り先のUnityを1つに絞る。絞れなければ理由を出して null を返す。
    ///
    /// **「改変に足して送る」と共通の門。**どちらのボタンでも同じ条件で
    /// 送れる／送れないが決まるべきで、片方だけ通ると挙動が読めなくなる。
    /// </summary>
    private Services.OpenUnityEditor? PickUnityTarget(string title)
    {
        // 連続送りの最中は混ぜない。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (Services.UnityImportQueue.IsRunning)
        {
            System.Windows.MessageBox.Show(
                Services.UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return null;
        }

        // 窓を名指しして送る道なので、複数開いていても選べば送れる（U14・ユーザ判断）。
        // 以前はファイルの関連付けに渡していて、どれに入るかを指名できず、2つ以上開いていると断っていた
        return Services.UnityTargetPicker.Pick(title);
    }

    private void SendToUnity(object? parameter) => SendToUnityAsync(parameter).Forget();

    /// <summary>
    /// 「Unity ▾」の「選択」（ユーザ指示 2026-09-19：改変の画面でできる「Unity のプロジェクトタブで示す」を商品ページにも）。
    /// 相手は送り先と同じ、いま開いている Unity。**入っていなければ言うだけで、取り込みには進まない**——
    /// 「示す」つもりで押した物が「取り込む」話にすり替わると意図と違う（動線の洗い出し A1 と同じ種類）。入れるなら「Unityへ送る」を選ぶ
    /// </summary>
    private async Task SelectInUnityAsync(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        const string title = "Unityで選択";

        // 選ぶ門は「送れません」と言うので、開いていないときはこちらで「示せない」と言う
        if (Services.UnityEditors.Open().Count == 0)
        {
            UnityRecordNotice = "Unityが開いていないので、示せません。プロジェクトを開いてから、もう一度選んでください。";
            return;
        }

        // 送るのではないので、連続送りの最中でも止めない（PickUnityTarget は送る用の門）
        if (Services.UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        // 窓の題の名前から、プロジェクトのフォルダを引く（「改変に足して送る」と同じ引き方）
        var projectPath = editor.ProjectName is { } name
            ? await Task.Run(() => Core.Services.UnityProjects.Discover()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))?.Path)
            : null;

        if (projectPath is null)
        {
            UnityRecordNotice = $"Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」の場所が分からないので、中を調べられません"
                + "（Unity Hub にも VRChat Creator Companion にも載っていないプロジェクトです）。";
            return;
        }

        var projectName = editor.ProjectName!;
        if (!await UnityMemberSelect.ShowIfPresentAsync(projectPath, projectName, editor, [package], Item.Id,
                text => UnityRecordNotice = text))
        {
            UnityRecordNotice = $"「{package.Name}」は、Unityの「{projectName}」にまだ入っていません。"
                + "入れるには「Unity ▾」の「Unityへ送る」を選んでください。";
        }
    }

    private async Task SendToUnityAsync(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        const string title = "Unityへ送る";

        if (PickUnityTarget(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var answer = System.Windows.MessageBox.Show(
            $"「{package.Name}」を、Unityの「{target}」に送ります。\n\n"
            + "Unity側で取り込む内容の一覧が出るので、そこで確認してから取り込めます。",
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            await SendOneToUnityAsync(editor, package, title);
        }
    }

    /// <summary>
    /// 1件を、選んだ Unity の窓へ名指しで送る（U14）。検索の複数選択・改変と同じ道（1件だけの列）。
    /// 取り込みの終わりをログで見るので、Cancel されたかも分かる。
    /// </summary>
    /// <returns>取り込み画面を出せたか。</returns>
    private async Task<bool> SendOneToUnityAsync(
        Services.OpenUnityEditor editor,
        Core.Services.UnityPackageEntry package,
        string title)
    {
        var outcomes = await Services.UnityImportQueue.RunAsync(
            editor.ProcessId, [package], progress: null, CancellationToken.None);
        var outcome = outcomes.FirstOrDefault();

        if (outcome is null || !outcome.Opened)
        {
            System.Windows.MessageBox.Show(
                $"「{package.Name}」をUnityへ送れませんでした。\n\n{outcome?.Problem ?? "理由が分かりませんでした。"}",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // 「使った」の足跡。Unityへ送ったことが一番強い証拠（ユーザ判断）。
        // 取り込み画面で Cancel された物は入っていないので付けない（検索の複数選択と同じ扱い）
        if (!outcome.Cancelled)
        {
            _services.Recent.TouchAsync(Item.Id, Core.Services.RecentKind.Used).Forget();
        }

        return true;
    }

    /// <summary>
    /// 改変に足して送る。
    ///
    /// **「送る」と別のボタンにしてある**（ユーザ判断）。送る前に「記録しますか」と
    /// 聞くと、記録を使っていない人の邪魔になる。ボタンで分ければ、
    /// **押した人だけが記録の話に入る。**
    /// </summary>
    private async Task SendToUnityWithRecordAsync(object? parameter)
    {
        if (parameter is not Core.Services.UnityPackageEntry package)
        {
            return;
        }

        const string title = "改変に足して送る";

        if (PickUnityTarget(title) is not { } editor)
        {
            return;
        }

        // 送り先のプロジェクトを、窓のタイトルの名前から実体のパスに直す。
        // HubにもVCCにも載っていないプロジェクトだと引けない——そのときは
        // 候補を絞らずに全部出す（**推定で絞ると、正しい改変が消える**）
        var projectPath = editor.ProjectName is { } name
            ? await Task.Run(() => Core.Services.UnityProjects.Discover()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))?.Path)
            : null;

        var records = projectPath is not null
            ? await _services.Modifications.LoadForProjectAsync(projectPath)
            : (await _services.Modifications.LoadAllAsync()).Modifications;

        var model = await BuildPickModificationAsync(
            title,
            $"「{package.Name}」を送って、改変に足します。",
            projectPath is not null
                ? $"送り先：Unityの「{editor.ProjectName}」"
                : $"送り先：Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」"
                    + "（一覧に無いプロジェクトなので、改変は全部出しています）",
            records,
            existingLabel: "このプロジェクトの改変に足す",
            commitLabel: "足して送る",
            emptyText: "このプロジェクトに紐付いた改変はまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        // **記録してから送る。**送るのは Unity 側の取り込み画面を待つので時間がかかり、
        // 途中で窓を閉じられることもある。先に記録を確定させておく方が失うものが少ない
        var owner = LocalFiles.FirstOrDefault(file => file.UnityPackages.Contains(package));
        if (await CommitPickedModificationAsync(model, title, projectPath, owner, package.EntryPath)
            is not { } record)
        {
            return;
        }

        // 窓を名指しして送る（U14）。取り込み画面を出せなかったら、記録だけ済んだと正直に言う
        var sent = await SendOneToUnityAsync(editor, package, title);

        UnityRecordNotice = sent
            ? $"「{record.Name}」に足して、Unityへ送りました。"
            : $"「{record.Name}」に足しました。Unityへは送れませんでした。";
    }

    /// <summary>
    /// 改変に足す（送らない）。
    ///
    /// **見せる場所と足す場所を同じにする**（ユーザ指摘）。
    /// 使った改変を出しているカードから、そのまま足せるようにした。
    /// </summary>
    private async Task AddToModificationAsync()
    {
        const string title = "改変に足す";

        var model = await BuildPickModificationAsync(
            title,
            $"「{Item.DisplayName}」を改変に足します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（Unityへ送ると残ります）。",
            (await _services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に足す",
            commitLabel: "足す",
            emptyText: "改変がまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        if (await CommitPickedModificationAsync(model, title, project: null, owner: null, package: null)
            is not { } record)
        {
            return;
        }

        UnityRecordNotice = $"「{record.Name}」に足しました。";
        await LoadModificationsAsync();
    }

    /// <summary>ダイアログの中身を組む。送るときと足すだけのときで文言だけ変える（組み方は検索画面と共通）。</summary>
    private Task<PickModificationDialogViewModel> BuildPickModificationAsync(
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<Core.Models.ModificationRecord> records,
        string existingLabel,
        string commitLabel,
        string emptyText)
        => Task.FromResult(ModificationPicking.BuildDialog(
            _services, title, headingText, contextText, records, existingLabel, commitLabel, emptyText));

    /// <summary>
    /// ダイアログの答えを記録に落とす。作る側なら先に作る。
    /// 作れなかったときは理由を出して null を返す。
    /// </summary>
    private async Task<Core.Models.ModificationRecord?> CommitPickedModificationAsync(
        PickModificationDialogViewModel model,
        string title,
        string? project,
        LocalFileRow? owner,
        string? package)
    {
        if (await ModificationPicking.ResolvePickedAsync(_services, model, title, project) is not { } record)
        {
            return null;
        }

        await _services.Commands.ExecuteAsync(
            new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new Core.Models.ModificationMember
                {
                    ItemId = Item.Id,
                    VariationId = owner?.VariationId,
                    FileHash = owner?.Hash,
                    Package = package,
                    AddedAt = DateTimeOffset.Now,
                }));

        return record;
    }

    private string? _unityRecordNotice;

    /// <summary>
    /// 直前の Unity まわりの結果（改変へ積んだ・Unity で選択した）。
    ///
    /// **積んだことは画面のどこにも出ない。**Unityへ渡した先の反応は
    /// こちらに返ってこないので、記録が入ったことだけは言っておく。「選択」の結果（示した・まだ入っていない）もここで言う
    /// </summary>
    public string? UnityRecordNotice
    {
        get => _unityRecordNotice;
        private set
        {
            if (SetField(ref _unityRecordNotice, value))
            {
                OnPropertyChanged(nameof(HasUnityRecordNotice));
            }
        }
    }

    public bool HasUnityRecordNotice => !string.IsNullOrEmpty(UnityRecordNotice);

    /// <summary>
    /// この商品を使った改変。
    ///
    /// **改変から辿れば分かる情報を商品ページで隠さない。**
    /// 「持っているのに出していない」を直した直後なので、同じ指摘を作らない。
    /// </summary>
    public ObservableCollection<UsedInModificationRowViewModel> UsedInModifications { get; } = [];

    public bool HasUsedInModifications => UsedInModifications.Count > 0;

    public string UsedInModificationsEmptyText =>
        "まだどの改変にも入っていません。下の「改変に足す」で残せます。";

    /// <summary>改変の詳細へ。戻るとこの商品へ帰る（見比べに戻ってくる。画面の履歴・U23）。</summary>
    private void OpenModification(UsedInModificationRowViewModel? row)
    {
        if (row is not null)
        {
            _main.ShowModification(row.Record);
        }
    }

    private async Task LoadModificationsAsync()
    {
        var records = await _services.Modifications.LoadUsingItemAsync(Item.Id);
        var registry = _services.Store.Avatars.Load();

        RunOnUiThread(() =>
        {
            UsedInModifications.Clear();
            foreach (var record in records)
            {
                UsedInModifications.Add(new UsedInModificationRowViewModel
                {
                    Record = record,
                    AvatarText = registry.Entries.FirstOrDefault(entry =>
                        string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                        is { } found
                            ? AvatarNames.ShownName(found)
                            : record.AvatarItemId,

                    // 同じ商品を別のバージョンで2回足せるので、何回入っているかを出す
                    UseCount = record.Members.Count(member =>
                        string.Equals(member.ItemId, Item.Id, StringComparison.Ordinal)),
                });
            }

            OnPropertyChanged(nameof(HasUsedInModifications));
        });
    }

    /// <summary>この商品にUnityへ送れるものが1つでもあるか。無ければ送り先の話もしない。</summary>
    public bool HasAnyUnityPackage => LocalFiles.Any(file => file.HasUnityPackages);

    /// <summary>
    /// 送り先の表示を読み直す。
    ///
    /// **Unityの開き閉じはこのアプリの外で起きる。**画面を組んだときの値を
    /// 持ち続けると、「開いていません」と出したまま実は開いている状態になる。
    /// ウィンドウが手前に戻ったら読み直す（<see cref="MainViewModel.NoteWindowActivated"/>）。
    /// </summary>
    public void NoteUnityChanged() => OnPropertyChanged(nameof(UnityTargetText));

    /// <summary>いま送るとどこへ行くか。押す前に見えている必要がある。</summary>
    public string UnityTargetText
    {
        get
        {
            var editors = Services.UnityEditors.Open();
            return editors.Count switch
            {
                0 => "Unityが開いていません（開いてから送れます）",
                1 => $"送り先：Unityの「{editors[0].ProjectName ?? "名前不明のプロジェクト"}」",
                // 窓を名指しして送るので、複数開いていても送るときに選べる（U14）
                _ => $"Unityが {editors.Count} つ開いています（送るときにどれへ送るか選べます）",
            };
        }
    }
}
