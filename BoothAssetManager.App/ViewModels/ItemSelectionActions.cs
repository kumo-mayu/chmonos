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
        var steps = new List<(ItemCardViewModel Card, IReadOnlyList<Core.Services.UnityPackageEntry> Fixed, PackageChoiceSection? Choice)>();
        var nothing = new List<string>();

        foreach (var card in cards)
        {
            var packages = Services.UnityImportQueue.PackagesOf(card.Item);
            if (packages.Count == 0)
            {
                nothing.Add(card.Name);
                continue;
            }

            // 送れる物が2つ以上ある商品は、送り先を決めた後に選ばせる（ユーザ判断 2026-09-13。前は全部送っていた）
            var choice = packages.Count > 1 ? PackageChoiceSection.Build(card.Item) : null;
            steps.Add((card, choice is null ? packages : [], choice));
        }

        if (steps.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "選んだ商品には、Unityへ送れるもの（zip の中の .unitypackage）が入っていませんでした。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // 送信は1列に限る。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (Services.UnityImportQueue.IsRunning)
        {
            System.Windows.MessageBox.Show(Services.UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Services.UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var choices = steps.Where(step => step.Choice is not null).Select(step => step.Choice!).ToList();
        var fixedCount = steps.Sum(step => step.Fixed.Count);

        if (choices.Count > 0)
        {
            var model = new PickPackagesDialogViewModel(
                title,
                $"Unityの「{target}」へ順に送ります。"
                    + (nothing.Count > 0 ? $"（送れるものが無い {nothing.Count} 件は飛ばします）" : string.Empty),
                choices,
                fixedCount,
                records: false);
            if (!Views.PickPackagesDialog.Ask(model))
            {
                return;
            }
        }
        else
        {
            // 数えているのは unitypackage の数（選んだ商品の数ではない）
            var confirm = System.Windows.MessageBox.Show(
                $"unitypackage {fixedCount} 件を、Unityの「{target}」へ順に送ります。\n\n"
                + "1件ずつ取り込み画面が出ます。Unity側で「Import」（入れない物は「Cancel」）を押すと、次の1件が出ます。\n"
                + "1つの zip に依存するものが入っていれば、zip に入っている順に送ります。"
                + (nothing.Count > 0 ? $"\n\n送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty),
                title,
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.OK);

            if (confirm != System.Windows.MessageBoxResult.OK)
            {
                return;
            }
        }

        var queue = steps
            .SelectMany(step => (step.Choice?.CheckedPackages ?? step.Fixed).Select(package => (step.Card, Package: package)))
            .ToList();
        if (queue.Count == 0)
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
            var opened = outcomes.Where(outcome => outcome.Opened).Select(outcome => outcome.Package).ToHashSet();
            var taken = outcomes.Where(outcome => outcome.Opened && !outcome.Cancelled).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => taken.Contains(entry.Package)).Select(entry => entry.Card.Item.Id).Distinct())
            {
                services.Recent.TouchAsync(itemId, Core.Services.RecentKind.Used).Forget();
            }

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            var skipped = outcomes.Count(outcome => outcome.Cancelled);
            var shown = skipped == 0
                ? $"{opened.Count} 件の取り込み画面を順に出しました。"
                : $"{opened.Count} 件の取り込み画面を順に出しました（うち {skipped} 件は Cancel されたので入っていません）。";
            setQueueText(string.Empty);
            System.Windows.MessageBox.Show(
                failed.Count == 0
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
        const string title = "改変に足す";
        if (cards.Count == 0)
        {
            return false;
        }

        var model = ModificationPicking.BuildDialog(
            services,
            title,
            $"選んだ {cards.Count} 件を改変に足します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（商品ページからUnityへ送ると残ります）。",
            (await services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に足す",
            commitLabel: "足す",
            emptyText: "改変がまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return false;
        }

        if (await ModificationPicking.ResolvePickedAsync(services, model, title, project: null) is not { } record)
        {
            return false;
        }

        var present = record.Members.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var card in cards.Where(card => !present.Contains(card.Item.Id)))
        {
            await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new ModificationMember { ItemId = card.Item.Id, AddedAt = DateTimeOffset.Now }));
            added++;
        }

        var skipped = cards.Count - added;
        System.Windows.MessageBox.Show(
            skipped == 0
                ? $"「{record.Name}」に {added} 件を足しました。"
                : $"「{record.Name}」に {added} 件を足しました。{skipped} 件は既に入っていたので、重ねて足していません。",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
        return true;
    }
}
