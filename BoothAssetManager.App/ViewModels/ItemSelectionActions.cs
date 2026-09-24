using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

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
        Action<string> setQueueText)
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

        var steps = new List<(ItemCardViewModel Card, IReadOnlyList<Core.Services.UnityPackageEntry> Packages)>();
        var nothing = new List<string>();

        // zip を開いて数えるので、画面のスレッドの外で読む
        var found = await Task.Run(() => cards.Select(card => (Card: card, Packages: Services.UnityImportQueue.PackagesOf(card.Item))).ToList());
        foreach (var (card, packages) in found)
        {
            if (packages.Count == 0)
            {
                nothing.Add(card.Name);
                continue;
            }

            steps.Add((card, packages));
        }

        if (steps.Count == 0)
        {
            Services.Notice.Show(
                "選んだ商品には、Unityへ送れるもの（zip の中の .unitypackage）が入っていませんでした。",
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

        // 送れる物が2つ以上ある商品は、右クリックと同じ一覧から選ぶ窓で、1件ずつ「2/5」のように番号を付けて順に聞く
        // （ユーザ指示 2026-09-19。前はまとめて1つの窓にチェックで並べていた）。
        // 1つの zip に依存物と本体が入っている商品は両方要るので、一覧の最後に「全部」を置く
        var choosing = steps.Where(step => step.Packages.Count > 1).ToList();
        var picked = new Dictionary<ItemCardViewModel, IReadOnlyList<Core.Services.UnityPackageEntry>>();
        var notSending = new List<string>();
        for (var i = 0; i < choosing.Count; i++)
        {
            var (card, packages) = choosing[i];
            var labels = ItemFileActions.PackageLabels(packages);
            labels.Add(new ListChoiceItem($"全部（{packages.Count} 件を zip に入っている順に）", "依存するものが同じ zip に入っているときは、こちらを選びます"));

            var answer = ListChoice.Ask(
                $"{title}（{i + 1}/{choosing.Count}）",
                $"「{card.Name}」には unitypackage が {packages.Count} 件あります。Unityの「{target}」へどれを送りますか？\n"
                    + $"（選ぶ必要がある商品 {choosing.Count} 件のうち {i + 1} 件目）",
                labels,
                "これを送る",
                skipText: "この商品を飛ばす");

            if (answer is not { } index)
            {
                return;
            }

            if (index == ListChoice.Skipped)
            {
                notSending.Add(card.Name);
                continue;
            }

            picked[card] = index == packages.Count ? packages : [packages[index]];
        }

        var queue = steps
            .Where(step => step.Packages.Count == 1 || picked.ContainsKey(step.Card))
            .SelectMany(step => (step.Packages.Count == 1 ? step.Packages : picked[step.Card]).Select(package => (step.Card, Package: package)))
            .ToList();
        if (queue.Count == 0)
        {
            // 全部飛ばしたときに黙って終わらない（I1 と同じ理由。2026-09-20 に実機で見た）
            Services.Notice.Show(
                "送るものが無くなりました（選んだ商品を全部飛ばしました）。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // 数えているのは unitypackage の数（選んだ商品の数ではない）。選び終えた後に、全体で何を送るかを1回だけ確かめる
        var confirm = Services.Notice.Show(
            $"unitypackage {queue.Count} 件を、Unityの「{target}」へ順に送ります。\n\n"
            + "1件ずつ Unity の取り込み画面が表示されます。Unity側で「Import」（入れない物は「Cancel」）を押すと、次の1件が表示されます。"
            + (nothing.Count > 0 ? $"\n\n送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty)
            + (notSending.Count > 0 ? $"\n飛ばすと選んだ {notSending.Count} 件は送りません。" : string.Empty),
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (confirm != System.Windows.MessageBoxResult.OK)
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
            var taken = outcomes.Where(outcome => outcome.Opened && !outcome.Cancelled).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => taken.Contains(entry.Package)).Select(entry => entry.Card.Item.Id).Distinct())
            {
                services.Recent.TouchAsync(itemId, Core.Services.RecentKind.Used).Forget();
            }

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            var shown = UnityQueueOutcome.Describe(outcomes);
            setQueueText(string.Empty);
            // 送っている間は Unity が手前にいるので、主の窓を戻してから言う
            // 止めたときは、1件ずつ理由を並べない（全部同じ理由なので読む物が増えるだけ。E7）
            FrontNotice.Show(
                failed.Count == 0 || failed.All(outcome => outcome.Problem == Services.UnityImportQueue.StoppedMessage)
                    ? shown
                    : $"{shown}{failed.Count} 件は送れませんでした：\n\n"
                        + string.Join("\n", failed.Select(outcome => $"・{outcome.Package.Name}：{outcome.Problem}").Distinct().Take(6)),
                title,
                System.Windows.MessageBoxButton.OK,
                failed.Count == 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            setSending(false);
        }
    }

    /// <summary>
    /// 選んだ物を1つの改変に足す。どの改変かは商品ページの「改変に足す」と同じ画面で1回だけ選ぶ。
    ///
    /// **既にその改変に入っている商品は重ねて足さない。**まとめて足すときは、
    /// どれが入っていたかを1件ずつ覚えていないので、同じ物が2行並ぶと記録を確かめにくい
    /// （別の版を2回入れたいときは、商品ページから1件ずつ足せる）。
    /// </summary>
    /// <returns>改変を書き換えたか（呼ぶ側が「着せているアバター」の絞り込みを読み直すため）。</returns>
    public static async Task<bool> AddToModificationAsync(AppServiceContainer services, IReadOnlyList<ItemCardViewModel> cards)
    {
        const string title = "改変に追加";
        if (cards.Count == 0)
        {
            return false;
        }

        var model = ModificationPicking.BuildDialog(
            services,
            title,
            $"選んだ {cards.Count} 件を改変に追加します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（商品ページからUnityへ送ると残ります）。",
            (await services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に追加",
            commitLabel: "追加",
            emptyText: "改変がまだありません。新しく作って、そこに追加できます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return false;
        }

        if (await ModificationPicking.ResolvePickedAsync(services, model, title, project: null) is not { } record)
        {
            return false;
        }

        var present = record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var card in cards.Where(card => !present.Contains(card.Item.Id)))
        {
            await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new ModificationMember { ItemId = card.Item.Id, AddedAt = DateTimeOffset.Now }));
            added++;
        }

        var skipped = cards.Count - added;
        Services.Notice.Show(
            skipped == 0
                ? $"「{record.Name}」に {added} 件を追加しました。"
                : $"「{record.Name}」に {added} 件を追加しました。{skipped} 件は既に入っていたので、重ねて追加していません。",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
        return true;
    }
}
