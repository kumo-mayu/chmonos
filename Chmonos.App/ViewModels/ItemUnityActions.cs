using Chmonos.App.Services;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

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

    /// <summary>
    /// 1件を、選んだ Unity に送る（送る前に何をどこへ送るかを確かめる）。
    /// </summary>
    /// <param name="notify">人が止めたときに言う先（商品ページは欄の下の1行）。無ければ知らせの窓で言う。</param>
    public static Task SendAsync(
        AppServiceContainer services, ItemRecord item, UnityPackageEntry package, UnitySendUi? ui = null, NoticeSink? notify = null,
        Action<ItemRecord>? presenceChanged = null)
        => SendAsync(services, item, [package], ui, notify, presenceChanged);

    /// <summary>
    /// 1つの商品から選んだ unitypackage を、選んだ Unity へ順に送る（カードの右クリックで複数選んだとき。ユーザ指示 2026-11-03）。
    /// 1件なら前と同じ1件送り。
    /// </summary>
    public static async Task SendAsync(
        AppServiceContainer services, ItemRecord item, IReadOnlyList<UnityPackageEntry> packages, UnitySendUi? ui = null, NoticeSink? notify = null,
        Action<ItemRecord>? presenceChanged = null)
    {
        const string title = "Unityへ送る";

        if (packages.Count == 0 || PickTarget(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var text = packages.Count == 1
            ? $"「{packages[0].Name}」を、Unityの「{target}」に送ります。\n\n"
                + "Unityの取り込み画面で内容を確認してから取り込めます。"
            : $"unitypackage {packages.Count} 件を、Unityの「{target}」へ順に送ります。\n\n"
                + "1件ずつUnityの取り込み画面が表示されます。「Import」か「Cancel」を押すと、次の1件が表示されます。";
        if (UnitySendNotice.Confirm(services, text, title))
        {
            await SendPickedAsync(services, item, editor, packages, title, ui, notify, presenceChanged);
        }
    }

    /// <summary>送り先と送る物が決まった後の「Unityへ送る」（2つ以上なら順に送り、終わりに1回だけ言う）。</summary>
    internal static async Task SendPickedAsync(
        AppServiceContainer services, ItemRecord item, OpenUnityEditor editor, IReadOnlyList<UnityPackageEntry> packages,
        string title, UnitySendUi? ui, NoticeSink? notify, Action<ItemRecord>? presenceChanged = null)
    {
        if (packages.Count == 1)
        {
            await SendPickedAsync(services, item, editor, packages[0], title, ui, notify, presenceChanged);
            return;
        }

        var (text, failed) = await SendManyAsync(services, item, editor, packages, ui, presenceChanged);
        if (text.Length == 0)
        {
            // VPM の窓でやめた（何も送っていない）。窓で選んだ直後なので言わない
            return;
        }

        if (failed)
        {
            FrontNotice.Show(text, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        else if (notify is not null)
        {
            notify(text);
        }
        else
        {
            UnitySendNotice.Result(services, text, title);
        }
    }

    /// <summary>
    /// 1つの商品の unitypackage を、連続送りの列（検索の複数選択と同じ <see cref="UnityImportQueue.RunAsync"/>）で順に送る。
    /// 1件ずつ取り込み画面を待つので、終わりの言い方も複数選択と同じにする（何件出したか・止めたか・送れなかった物と理由）。
    /// </summary>
    /// <returns>言う文と、送れなかった物があるか（人が止めただけなら失敗にしない）。</returns>
    internal static async Task<(string Text, bool Failed)> SendManyAsync(
        AppServiceContainer services, ItemRecord item, OpenUnityEditor editor, IReadOnlyList<UnityPackageEntry> packages,
        UnitySendUi? ui, Action<ItemRecord>? presenceChanged = null, bool askVpm = true)
    {
        ui?.Begin($"unitypackage {packages.Count} 件をUnityへ送っています…");
        IReadOnlyList<UnityQueueOutcome> outcomes;
        try
        {
            outcomes = await UnityImportQueue.RunAsync(editor.ProcessId, packages, ui?.Progress, CancellationToken.None, askVpm: askVpm);
        }
        finally
        {
            ui?.End();
        }

        // 取り込み画面で Cancel された物は入っていないので、1件でも入ったときだけ「使った」の足跡を付ける
        if (outcomes.Any(outcome => outcome.Taken))
        {
            services.Recent.TouchAsync(item.Id, RecentKind.Used).Forget();
        }

        // 送れなかった物の zip が無ければ記録へ（1件の送り方と同じ）。窓の前に書く
        await FilePresenceNotes.NoteFailedSendsAsync(
            services, packages.Select(package => (item.Id, package)), outcomes, presenceChanged);

        // VPM の窓でやめた：言う文は無い（呼び手は空なら黙る）
        if (UnityQueueOutcome.AllDeclined(outcomes))
        {
            return (string.Empty, false);
        }

        var failed = UnityQueueOutcome.FailedNotChosen(outcomes);
        if (failed.All(outcome => UnityImportQueue.IsStopped(outcome.Problem)))
        {
            return (UnityQueueOutcome.Describe(outcomes), false);
        }

        return ($"{UnityQueueOutcome.DescribeShown(outcomes)}{failed.Count} 件は送れませんでした：\n\n"
            + string.Join("\n", failed.Select(outcome => $"・{outcome.Package.Name}：{outcome.Problem}").Distinct().Take(6)), true);
    }

    /// <summary>
    /// 送り先と送る物が決まった後の「Unityへ送る」。送って、人が止めたときだけここで言う
    /// （本当に送れなかったときは <see cref="SendOneAsync"/> が警告の窓で言う。うまくいったときは Unity の取り込み画面が出るので言わない。D7）。
    /// </summary>
    internal static async Task SendPickedAsync(
        AppServiceContainer services, ItemRecord item, OpenUnityEditor editor, UnityPackageEntry package,
        string title, UnitySendUi? ui, NoticeSink? notify, Action<ItemRecord>? presenceChanged = null)
    {
        // VPM の窓で「やめる」を押したときは、押した直後なので言わない（plan 40）
        if (await SendOneAsync(services, item, editor, package, title, ui, presenceChanged) is { Stopped: { } stopped } && stopped != UnityVpmGuard.DeclinedMessage)
        {
            // 止めたのは人なので、失敗の顔（⚠）で出さない。まとめて送るときと同じ文で言う。
            // 取り込み画面が残ったときは閉じ方も要るので、黙りはしない
            if (notify is not null)
            {
                notify(stopped);
            }
            else
            {
                UnitySendNotice.Result(services, stopped, title);
            }
        }
    }

    /// <summary>1件を送った結果。<paramref name="Stopped"/> は人が止めたときの言い方（止めていなければ null）。</summary>
    internal readonly record struct SendResult(bool Sent, string? Stopped);

    /// <summary>
    /// 1件を、選んだ Unity の窓へ名指しで送る（U14）。検索の複数選択・改変と同じ道（1件だけの列）。
    /// 取り込みの終わりをログで見るので、Cancel されたかも分かる。
    ///
    /// **本当に送れなかったときだけ、ここで警告の窓を出す。**人が「中止」を押したときに
    /// 「送れませんでした」と出すと、失敗に読めた（2026-10-02）。止めたときの言い方は呼ぶ側が決める
    /// </summary>
    internal static async Task<SendResult> SendOneAsync(
        AppServiceContainer services, ItemRecord item, OpenUnityEditor editor, UnityPackageEntry package,
        string title, UnitySendUi? ui, Action<ItemRecord>? presenceChanged = null, bool askVpm = true)
    {
        // 1件でも進み具合を出す（E10）。出す先を持っている画面だけが渡す
        ui?.Begin($"「{package.Name}」をUnityへ送っています…");
        IReadOnlyList<UnityQueueOutcome> outcomes;
        try
        {
            outcomes = await UnityImportQueue.RunAsync(
                editor.ProcessId, [package], ui?.Progress, CancellationToken.None, askVpm: askVpm);
        }
        finally
        {
            ui?.End();
        }

        var outcome = outcomes.FirstOrDefault();

        if (outcome is { Opened: false } && UnityImportQueue.IsStopped(outcome.Problem))
        {
            return new SendResult(false, outcome.Problem);
        }

        if (outcome is null || !outcome.Opened)
        {
            // 送れなかった理由が「zip が無い」なら記録へ（見つからなくなった日時。ユーザ判断 2026-10-04）。
            // 理由の文では見分けず、そのファイルを見直す（在れば何も書かない）。窓の前に書く（窓の間も検索の印が合うように）
            var files = item.Local.LocalFiles
                .Where(file => string.Equals(file.Hash, package.ZipHash, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (await FilePresenceNotes.LookAndNoteAsync(services, item, files) is { } reloaded)
            {
                presenceChanged?.Invoke(reloaded);
            }

            FrontNotice.Show(
                $"「{package.Name}」をUnityへ送れませんでした。\n\n{outcome?.Problem ?? "理由が分かりませんでした。"}",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return new SendResult(false, null);
        }

        // 「使った」の足跡。Unityへ送ったことが一番強い証拠（ユーザ判断）。
        // 取り込み画面で Cancel された物は入っていないので付けない（検索の複数選択と同じ扱い）
        if (outcome.Taken)
        {
            services.Recent.TouchAsync(item.Id, RecentKind.Used).Forget();
        }

        return new SendResult(true, null);
    }

    /// <summary>
    /// 改変に足した後、送った結果をどう言うか。記録は送る前に済んでいるので、どの場合も「追加しました」は言う。
    /// 人が止めたときは失敗にしない（窓の ⚠ で「送れませんでした」と出ると、失敗に読めた。2026-10-02）
    /// </summary>
    /// <param name="names">足した改変の名前（<see cref="ModificationPicking.NamesText"/> の形。「」付き）。</param>
    internal static (string Text, bool Failed) AfterRecordText(string names, SendResult sent)
        => sent switch
        {
            { Sent: true } => ($"{names}に追加して、Unityへ送りました。", false),
            { Stopped: { } stopped } => ($"{names}に追加しました。{stopped}", false),
            _ => ($"{names}に追加しました。Unityへは送れませんでした。", true),
        };

    /// <summary>
    /// 改変に足して送る。
    ///
    /// **「送る」と別の選択肢にしてある**（ユーザ判断）。送る前に「記録しますか」と
    /// 聞くと、記録を使っていない人の邪魔になる。分ければ、**選んだ人だけが記録の話に入る。**
    /// </summary>
    /// <returns>足した改変（足せなかった・やめたら null）。</returns>
    public static Task<IReadOnlyList<ModificationRecord>?> SendWithRecordAsync(
        AppServiceContainer services, ItemRecord item, UnityPackageEntry package, NoticeSink notify, UnitySendUi? ui = null,
        Action<ItemRecord>? presenceChanged = null)
        => SendWithRecordAsync(services, item, [package], notify, ui, presenceChanged);

    /// <summary>
    /// 1つの商品から選んだ unitypackage を、全部改変の使ったものに記録してから順に送る（カードの右クリックで複数選んだとき。ユーザ指示 2026-11-03）。
    /// 選んだ1つずつが使ったものの1行になる（改変の画面で2つ選んだときと同じ形）。
    /// </summary>
    public static async Task<IReadOnlyList<ModificationRecord>?> SendWithRecordAsync(
        AppServiceContainer services, ItemRecord item, IReadOnlyList<UnityPackageEntry> packages, NoticeSink notify, UnitySendUi? ui = null,
        Action<ItemRecord>? presenceChanged = null)
    {
        const string title = "改変に追加して送る";

        if (packages.Count == 0 || PickTarget(title) is not { } editor)
        {
            return null;
        }

        // 送り先のプロジェクトの改変に絞る。一覧に無いプロジェクトなら絞らずに全部出す（**推定で絞ると、正しい改変が消える**）
        var projectPath = await ProjectPathOf(editor);

        // VPM との重なりは記録の前に聞く（plan 40）。記録してから送る決まりなので、送る列の中で聞くと、やめても記録だけ残る。
        // やめたら何もしない（何も起きていないのは見れば分かる）。飛ばした物は記録にも入れない
        if (await UnityVpmGuard.FilterAsync(projectPath, packages) is not { Count: > 0 } screened)
        {
            return null;
        }

        packages = screened;
        var records = projectPath is not null
            ? await services.Modifications.LoadForProjectAsync(projectPath)
            : (await services.Modifications.LoadAllAsync()).Modifications;

        var model = ModificationPicking.BuildDialog(
            services,
            title,
            packages.Count == 1
                ? $"「{packages[0].Name}」を送って、改変に追加します。"
                : $"unitypackage {packages.Count} 件を送って、改変に追加します。",
            projectPath is not null
                ? $"送り先：Unityの「{editor.ProjectName}」"
                : $"送り先：Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」"
                    + "。すべての改変から選べます。",
            records,
            existingLabel: "このプロジェクトの改変に追加",
            commitLabel: "追加して送る",
            emptyText: "このプロジェクトに紐付いた改変はまだありません。新しく作って、そこに追加できます。",
            ownedItems: await ModificationPicking.LoadOwnedItemsAsync(services),
            // 一覧に無いプロジェクトへ送るときは全部の改変を並べるので、紐付けたプロジェクトが同じ物だけを一緒に選ばせる（送り先は1つ）
            requireSameProject: projectPath is null);

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return null;
        }

        // **記録してから送る。**送るのは Unity 側の取り込み画面を待つので時間がかかり、
        // 途中で窓を閉じられることもある。先に記録を確定させておく方が失うものが少ない
        var members = packages.Select(package =>
        {
            var owner = item.Local.OwnedFiles.FirstOrDefault(file => file.Hash == package.ZipHash);
            return new ModificationMember
            {
                ItemId = item.Id,
                VariationId = owner?.VariationId,
                FileHash = owner?.Hash,
                Package = package.EntryPath,
                AddedAt = DateTimeOffset.Now,
            };
        }).ToList();
        if (await CommitPickedAsync(services, model, title, projectPath, members) is not { } picked)
        {
            return null;
        }

        // 窓を名指しして送る（U14）。取り込み画面を出せなかったら、記録だけ済んだと正直に言う。
        // 改変をいくつ選んでも送り先は同じプロジェクトなので、送るのは1回（改変ごとに送ると、同じ物の取り込み画面が何度も出る）
        var names = ModificationPicking.NamesText(picked);
        if (packages.Count == 1)
        {
            var (text, failed) = AfterRecordText(names, await SendOneAsync(services, item, editor, packages[0], title, ui, presenceChanged, askVpm: false));
            notify(text, failed);
        }
        else
        {
            var (text, failed) = await SendManyAsync(services, item, editor, packages, ui, presenceChanged, askVpm: false);
            notify($"{names}に {packages.Count} 件を追加しました。{text}", failed);
        }

        return picked;
    }

    /// <summary>
    /// 「改変に追加して送る」の窓の答えを記録に落とす（作る側なら先に作る）。送った物ごとに記録するので、既に入っている商品でも足す
    /// （送らない「改変に追加」は重ねない。<see cref="ItemSelectionActions.AddPickedAsync"/>）。
    /// 選んだ改変（いくつでも。ユーザ指示 2026-11-03）に、行をいくつか足す（並べた順に後ろへ付ける）。
    /// 改変ごとに1回の保存でまとめて足す（1行ずつだと途中の失敗で一部だけ残り、やり直すと2重になった。Codex の点検 2026-11-03）。
    /// 改変はそれぞれ別のファイルなので、全部をまとめて1回にはできない。足せない改変があればそこで止め、
    /// どれに足せてどれに足せなかったかを言い、送らない（記録してから送る決まりなので、記録の欠けたまま送らない）
    /// </summary>
    public static async Task<IReadOnlyList<ModificationRecord>?> CommitPickedAsync(
        AppServiceContainer services,
        PickModificationDialogViewModel model,
        string title,
        string? project,
        IReadOnlyList<ModificationMember> members)
    {
        if (await ModificationPicking.ResolvePickedAsync(services, model, title, project) is not { } records)
        {
            return null;
        }

        var done = new List<ModificationRecord>();
        foreach (var record in records)
        {
            if (await ItemSelectionActions.SaveMembersAsync(services, record, members) is { } problem)
            {
                var head = done.Count > 0 ? $"{ModificationPicking.NamesText(done)}には追加しました。" : string.Empty;
                FrontNotice.Show(
                    $"{head}「{record.Name}」に追加できませんでした。Unityへは送っていません。\n\n{problem}",
                    title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return null;
            }

            done.Add(record);
        }

        return done;
    }

    /// <summary>画像の行の文。見つかり方を頭に付けて、選ぶ前に確度が分かるようにする。</summary>
    internal static string ImageRowText(UnityImageMatch match)
        => $"{(match.Kind == UnityImageMatchKind.SameImage ? "同じ画像" : "名前が同じ")}：{match.ProjectPath}";

    /// <summary>
    /// 商品を Unity で示す（カードと商品ページのファイルの欄）。unitypackage で入った物に加え、手で置いた画像も示す（ユーザ判断 2026-11-01）。
    /// 画像が見つからなければ前と同じ unitypackage の道。
    /// </summary>
    public static async Task SelectItemAsync(AppServiceContainer services, ItemRecord item, NoticeSink notify)
    {
        const string title = "Unityで選択";
        if (UnityEditors.Open().Count == 0)
        {
            notify("Unityが開いていません。プロジェクトを開いてから、もう一度選んでください。", failed: true);
            return;
        }

        if (UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        if (await ProjectPathOf(editor) is not { } projectPath)
        {
            var tools = await Task.Run(() => services.DetectUnityTools());
            notify($"Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」の中を調べられません。"
                + UnityToolsText.NotListed(tools, services.Settings.ProjectManager), failed: true);
            return;
        }

        var projectName = editor.ProjectName ?? System.IO.Path.GetFileName(projectPath);
        notify($"「{projectName}」の中を調べています…");
        var (images, packages) = await Task.Run(() => (
            UnityProjectItemScan.FindImages(projectPath, item, services.ZipImages, CancellationToken.None),
            UnityImportQueue.PackagesOf(item)));
        notify(string.Empty);

        if (images.Count == 0 && packages.Count == 0)
        {
            notify($"この商品の画像は、Unityの「{projectName}」に見つかりませんでした。", failed: true);
            return;
        }

        // 同じ画像が1枚だけで unitypackage も無ければ、迷う所が無いので聞かない。名前が同じだけの物は別の画像かもしれないので見せてから
        UnityImageMatch? image = null;
        UnityPackageEntry? package = null;
        if (images.Count == 1 && packages.Count == 0 && images[0].Kind == UnityImageMatchKind.SameImage)
        {
            image = images[0];
        }
        else if (images.Count == 0 && packages.Count == 1)
        {
            package = packages[0];
        }
        else
        {
            var picked = ListChoice.Ask(
                title,
                "どれを選択しますか？",
                [.. images.Select(match => new ListChoiceItem(ImageRowText(match), match.SourceName)),
                    .. packages.Select(entry => new ListChoiceItem(entry.Name, "unitypackage"))],
                "これを選択");
            if (picked is not { } index || index < 0)
            {
                return;
            }

            if (index < images.Count)
            {
                image = images[index];
            }
            else
            {
                package = packages[index - images.Count];
            }
        }

        if (package is not null)
        {
            if (!await UnityMemberSelect.ShowIfPresentAsync(projectPath, projectName, editor, [package], item.Id, notify))
            {
                notify($"「{package.Name}」は、Unityの「{projectName}」にまだ入っていません。"
                    + "入れるには「Unityへ送る」を選んでください。", failed: true);
            }

            return;
        }

        var outcome = await UnityProjectTab.SelectImageAsync(editor.ProcessId, image!.ProjectPath);
        notify(outcome.Problem ?? $"{ImageRowText(image)} を選択しました。", failed: outcome.Problem is not null);
    }

    /// <summary>
    /// Unity で選択する（ユーザ指示 2026-09-19：改変の画面でできる「Unity のプロジェクトタブで示す」を商品ページ・カードにも）。
    /// 相手は送り先と同じ、いま開いている Unity。**入っていなければ言うだけで、取り込みには進まない**——
    /// 「示す」つもりで押した物が「取り込む」話にすり替わると意図と違う（動線の洗い出し A1 と同じ種類）。入れるなら「Unityへ送る」を選ぶ
    /// </summary>
    public static async Task SelectAsync(
        AppServiceContainer services, ItemRecord item, UnityPackageEntry package, NoticeSink notify)
    {
        const string title = "Unityで選択";

        // 選ぶ門は「送れません」と言うので、開いていないときはこちらで「示せない」と言う
        if (UnityEditors.Open().Count == 0)
        {
            notify("Unityが開いていません。プロジェクトを開いてから、もう一度選んでください。", failed: true);
            return;
        }

        // 送るのではないので、連続送りの最中でも止めない（PickTarget は送る用の門）
        if (UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        if (await ProjectPathOf(editor) is not { } projectPath)
        {
            // 引けなかった理由は、入っている物の一覧の名前で言う（入っていない物まで見たかのように言わない）。
            // レジストリを見るので、言うときだけ裏で調べる
            var tools = await Task.Run(() => services.DetectUnityTools());
            notify($"Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」の中を調べられません。"
                + UnityToolsText.NotListed(tools, services.Settings.ProjectManager), failed: true);
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
