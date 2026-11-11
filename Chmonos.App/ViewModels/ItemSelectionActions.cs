using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 選んだ商品へのまとめた操作。検索画面とフォルダビューの右側で共通（ユーザ指示 2026-09-14：
/// フォルダも人によっては検索と同等の発見手段なので、まとめて操作する機能も同等にする）。
/// 中身は検索画面から移した（変えていない）。進み具合の出し先だけを呼ぶ側から受け取る。
/// </summary>
internal static class ItemSelectionActions
{
    /// <summary>
    /// 選んだ商品の unitypackage を、選んだ順（表示中の並び）に1件ずつ Unity へ積む（#69・ユーザ追加要望）。
    /// 1件ずつ取り込み画面が出るので、利用者が Import か Cancel を押すと次が出る。
    /// </summary>
    public static async Task SendToUnityAsync(
        AppServiceContainer services,
        IReadOnlyList<ItemCardViewModel> cards,
        Action<bool> setSending,
        Action<string> setQueueText,
        Action<ItemRecord>? presenceChanged = null)
    {
        const string title = "Unityへ順に送る";

        // **送信中なら押した直後に断る**（ユーザ判断 2026-09-21・N8）。
        // 判定が zip を開いて数え・送る物を選ばせ・確かめの窓を出した後にあったので、
        // 二度押しすると最後まで進んでから断られていた（壊れはしないが、その手間が全部無駄になる）
        if (Services.UnityImportQueue.IsRunning)
        {
            Services.Notice.Show(Services.UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var steps = new List<(ItemCardViewModel Card, IReadOnlyList<Core.Services.UnityPackageEntry> Packages, PackageChoiceSection? Choice)>();
        var nothing = new List<string>();

        // zip を開いて数えるので、画面のスレッドの外で読む（選ぶ窓の候補も一緒に作る。2つ以上のときだけ出来る）
        var fileOrder = services.Settings.FileSort;
        var found = await Task.Run(() => cards
            .Select(card => (Card: card, Packages: Services.UnityImportQueue.PackagesOf(card.Item), Choice: PackageChoiceSection.Build(card.Item, fileOrder)))
            .ToList());
        foreach (var (card, packages, choice) in found)
        {
            if (packages.Count == 0)
            {
                nothing.Add(card.Name);
                continue;
            }

            steps.Add((card, packages, choice));
        }

        if (steps.Count == 0)
        {
            Services.Notice.Show(
                "選んだ商品には、Unityへ送れるもの（zipやフォルダの中の .unitypackage）が入っていませんでした。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // zip を読んでいる間に別の送信が始まっていることがあるので、ここでももう一度見る
        // （送信は1列に限る。Editor.log は全エディタが共有するので、終わりを取り違える §11-3）
        if (Services.UnityImportQueue.IsRunning)
        {
            Services.Notice.Show(Services.UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Services.UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";

        // 送れる物が2つ以上ある商品は、1件ずつ「2/5」のように番号を付けて順に聞く（ユーザ指示 2026-09-19。前はまとめて1つの窓にチェックで並べていた）。
        // 1つの商品の中ではいくつでも選べる（ユーザ指示 2026-11-03。前は1つか「すべて」で、本体と依存物だけを選べなかった）。
        // 窓は改変から送るときと同じ物で、探す欄と「すべて選択」「選択を解除」を持ち、最初は何も選んでいない
        var choosing = steps.Where(step => step.Choice is not null).ToList();
        var picked = new Dictionary<ItemCardViewModel, IReadOnlyList<Core.Services.UnityPackageEntry>>();
        var notSending = new List<string>();
        for (var i = 0; i < choosing.Count; i++)
        {
            var (card, packages, choice) = choosing[i];
            var model = new PickPackagesDialogViewModel(
                $"{title}（{i + 1}/{choosing.Count}）",
                $"Unityの「{target}」へ送るunitypackageを選んでください。",
                [choice!],
                othersCount: 0,
                records: false)
            {
                SkipText = "この商品を飛ばす",
            };

            if (!PickPackagesDialogViewModel.Ask(model))
            {
                if (model.Skipped)
                {
                    notSending.Add(card.Name);
                    continue;
                }

                return;
            }

            picked[card] = choice!.CheckedPackages;
        }

        // 選んだ商品の並び（表示中の順）に、商品の中は選ぶ窓で見えている順（上から）に積む
        var queue = steps
            .Where(step => step.Choice is null || picked.ContainsKey(step.Card))
            .SelectMany(step => (step.Choice is null ? step.Packages : picked[step.Card]).Select(package => (step.Card, Package: package)))
            .ToList();
        if (queue.Count == 0)
        {
            // 全部飛ばしたときに黙って終わらない（I1 と同じ理由。2026-09-20 に実機で見た）
            Services.Notice.Show(
                "選んだ商品をすべて飛ばしたので、送るものはありません。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // 数えているのは unitypackage の数（選んだ商品の数ではない）。選び終えた後に、全体で何を送るかを1回だけ確かめる
        var text = $"unitypackage {queue.Count} 件を、Unityの「{target}」へ順に送ります。\n\n"
            + "1件ずつUnityの取り込み画面が表示されます。「Import」か「Cancel」を押すと、次の1件が表示されます。"
            + (nothing.Count > 0 ? $"\n\n送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty)
            + (notSending.Count > 0 ? $"\n飛ばすと選んだ {notSending.Count} 件は送りません。" : string.Empty);

        // 飛ばす物があるときは、確認を出さない設定でも聞く（選んだ数より少なく送ることを、送る前に知らせる）
        var confirmed = nothing.Count > 0 || notSending.Count > 0
            ? Services.Notice.Show(text, title, System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.OK) == System.Windows.MessageBoxResult.OK
            : UnitySendNotice.Confirm(services, text, title);

        if (!confirmed)
        {
            return;
        }

        setSending(true);
        try
        {
            var progress = new Progress<Services.UnityQueueProgress>(report => setQueueText(report.Text));
            var outcomes = await Services.UnityImportQueue.RunAsync(
                editor.ProcessId, queue.Select(entry => entry.Package).ToList(), progress, CancellationToken.None);

            // 「使った」の足跡。Unityへ送ったことが一番強い証拠（Unityへ送る と同じ扱い）。
            // 取り込み画面で Cancel された物は入っていないので付けない
            var taken = outcomes.Where(outcome => outcome.Taken).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => taken.Contains(entry.Package)).Select(entry => entry.Card.Item.Id).Distinct())
            {
                services.Recent.TouchAsync(itemId, Core.Services.RecentKind.Used).Forget();
            }

            // 送れなかった物の zip が無ければ記録へ（商品ページの1件の送り方と同じ）。窓の前に書く
            await FilePresenceNotes.NoteFailedSendsAsync(
                services, queue.Select(entry => (entry.Card.Item.Id, entry.Package)), outcomes, presenceChanged);

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            var shown = UnityQueueOutcome.Describe(outcomes);
            setQueueText(string.Empty);

            // 止めただけ（送れなかった物が全部、人が止めた分）なら失敗ではない。警告の印にすると失敗に読める
            // （1件だけ送るのを止めたときと同じ扱い。ユーザ判断 2026-10-02「おすすめで良い」）
            var onlyStopped = failed.All(outcome => Services.UnityImportQueue.IsStopped(outcome.Problem));

            // 送っている間は Unity が手前にいるので、主の窓を戻してから言う
            // 止めたときは、1件ずつ理由を並べない（全部同じ理由なので読む物が増えるだけ。E7）
            if (onlyStopped)
            {
                UnitySendNotice.Result(services, shown, title);
            }
            else
            {
                FrontNotice.Show(
                    $"{shown}{failed.Count} 件は送れませんでした：\n\n"
                        + string.Join("\n", failed.Select(outcome => $"・{outcome.Package.Name}：{outcome.Problem}").Distinct().Take(6)),
                    title,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        finally
        {
            setSending(false);
        }
    }

    /// <summary>
    /// 右クリックで押したカードに対して操作する物。選んでいる最中に、選んだ物の上で押したら選んだ全部（帯の操作と同じ）、
    /// 選んでいない物の上なら押した1件だけ（選んだ物には触らない）。
    /// </summary>
    internal static IReadOnlyList<ItemCardViewModel> CardsForMenu(ItemCardViewModel? pressed, IReadOnlyList<ItemCardViewModel> selected)
    {
        if (pressed is null)
        {
            return [];
        }

        return pressed.IsSelected && selected.Contains(pressed) ? selected : [pressed];
    }

    /// <summary>
    /// 選んだ物を改変に足す。どの改変かは商品ページの「改変に足す」と同じ画面で1回だけ選ぶ。
    ///
    /// **既にその改変に入っている商品は重ねて足さない。**まとめて足すときは、
    /// どれが入っていたかを1件ずつ覚えていないので、同じ物が2行並ぶと記録を確かめにくい
    /// （別の版を重ねて入れたいときは、「改変に追加して送る」なら送った物ごとに記録する）。
    /// </summary>
    /// <returns>改変を書き換えたか（呼ぶ側が「着せているアバター」の絞り込みを読み直すため）。</returns>
    public static async Task<bool> AddToModificationAsync(AppServiceContainer services, IReadOnlyList<ItemCardViewModel> cards)
    {
        const string title = "改変に追加";
        if (cards.Count == 0)
        {
            return false;
        }

        // 使ったファイルは、改変を選ぶ同じ窓で商品ごとに選ぶ（メモ26-②）。商品ごとに窓を出すと、数十件で数十回聞かれる。
        // 選ばなければ空のまま。**推定で埋めない**
        var files = new MemberFilePickViewModel(cards.Select(card => card.Item));
        var model = ModificationPicking.BuildDialog(
            services,
            title,
            cards.Count == 1 ? $"「{cards[0].Item.DisplayName}」を改変に追加します。" : $"選んだ {cards.Count} 件を改変に追加します。",
            ModificationPicking.FilesContextText(files, "記録するには、商品ページからUnityへ送ってください。"),
            (await services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に追加",
            commitLabel: "追加",
            emptyText: "改変がまだありません。新しく作って、そこに追加できます。",
            files,
            ownedItems: await ModificationPicking.LoadOwnedItemsAsync(services));

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return false;
        }

        if (await ModificationPicking.ResolvePickedAsync(services, model, title, project: null) is not { } records)
        {
            return false;
        }

        var result = await AddPickedAsync(services, records, cards.Select(card => card.Item).ToList(), files);
        Services.Notice.Show(
            result.Text,
            title,
            System.Windows.MessageBoxButton.OK,
            result.Failed ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
        return result.Changed;
    }

    /// <summary>送らない「改変に追加」の結果。<paramref name="Changed"/> は1つでも足したか（絞り込みを読み直すため）。</summary>
    internal sealed record AddPickedResult(string Text, bool Failed, bool Changed);

    /// <summary>
    /// 窓で決めた改変に、選んだ商品を足す（送らない「改変に追加」の後ろ半分。カードと商品ページが使い、試験もここを呼ぶ）。
    /// 使ったファイルは窓で選んだ物（<paramref name="files"/>）。
    ///
    /// 改変はいくつでも選べる（ユーザ指示 2026-11-03）。送らないので、紐付けたプロジェクトが違っても一緒に足せる。
    /// **既に入っている商品は重ねて足さない**（商品ページからも。前は商品ページの道だけ重ねていた）。
    /// 改変ごとに1回の保存でまとめて足し、足せない改変があればそこで止めて、足せた所までを言う
    /// （前は商品ごとに保存して失敗を見ず、足せていないのに「追加しました」と言うことがあった。Codex の点検 2026-11-03 の2回目）
    /// </summary>
    internal static async Task<AddPickedResult> AddPickedAsync(
        AppServiceContainer services,
        IReadOnlyList<ModificationRecord> records,
        IReadOnlyList<ItemRecord> items,
        MemberFilePickViewModel files)
    {
        var lines = new List<string>();
        var changed = false;
        foreach (var record in records)
        {
            var present = record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            var now = DateTimeOffset.Now;
            var members = items.Where(item => present.Add(item.Id)).Select(item => files.MemberFor(item.Id, now)).ToList();
            var skipped = items.Count - members.Count;

            if (members.Count > 0 && await SaveMembersAsync(services, record, members) is { } problem)
            {
                lines.Add($"「{record.Name}」に追加できませんでした。");
                return new AddPickedResult($"{string.Join("\n", lines)}\n\n{problem}", Failed: true, changed);
            }

            changed |= members.Count > 0;
            lines.Add((items.Count, members.Count) switch
            {
                (1, 1) => $"「{record.Name}」に追加しました。",
                (1, _) => $"「{record.Name}」には既に入っています。",
                (_, 0) => $"「{record.Name}」には {skipped} 件とも既に入っています。",
                _ when skipped > 0 => $"「{record.Name}」に {members.Count} 件を追加しました。{skipped} 件は既に入っています。",
                _ => $"「{record.Name}」に {members.Count} 件を追加しました。",
            });
        }

        return new AddPickedResult(string.Join("\n", lines), Failed: false, changed);
    }

    /// <summary>1つの改変に行をまとめて足す。足せなければ使う人に見せる理由を返す。</summary>
    internal static async Task<string?> SaveMembersAsync(
        AppServiceContainer services, ModificationRecord record, IReadOnlyList<ModificationMember> members)
    {
        try
        {
            return await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddModificationMembers(record.Id, members))
                is Core.Commands.CommandResult.Failed failed
                ? failed.Message
                : null;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            return "改変のファイルを保存できませんでした。保存先のドライブと空き容量を確かめてください。";
        }
    }
}
