using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 使ったものの「更新 ▾」の「新しい版を送る…」（feedback 2026-11-18・ユーザ判断）。
///
/// 前の「新しい版を選ぶ…」は記録の使ったファイルを書き換えるだけで Unity へは送らず、Unity に入っているのは古い版のまま、
/// 記録だけ新しい版になり得た。ここでは**送って Unity に入った時だけ**記録を新しい版に替える。
/// 送る部品は「Unityへ送る」と同じ（<see cref="UnityTargetPicker"/>・<see cref="UnityImportQueue"/>）で、書くのは
/// <see cref="UiCommand.ChangeModificationMemberFiles"/> 1本（錠の中で今の行に当てる）。
/// 送る前に、上書きで設定が消えうる事とバックアップを勧める確認を出す（ユーザ判断：初期のマテリアルのまま改変していた場合など）
/// </summary>
public static class MemberNewVersionSend
{
    public const string Title = "新しい版を送る";

    /// <summary>試験が送り先の Unity を決める（窓も Unity も無いので）。null なら本物の選び方。</summary>
    internal static Func<string, string?, OpenUnityEditor?>? PickTargetIntercept { get; set; }

    /// <summary>試験が送った結果を決める。null なら本物の送り方。</summary>
    internal static Func<IReadOnlyList<UnityPackageEntry>, IReadOnlyList<UnityQueueOutcome>>? SendIntercept { get; set; }

    /// <summary>行の下に出す文（何もしなかったら null）と、失敗か。</summary>
    public static async Task<(string? Text, bool Failed)> RunAsync(
        AppServiceContainer services,
        ModificationRecord record,
        ModificationMember member,
        string name,
        IProgress<UnityQueueProgress>? progress,
        Action<bool> sending,
        Action<ItemRecord>? itemChanged)
    {
        if (await services.Store.Items.LoadAsync(member.ItemId) is not { } item)
        {
            return ("この商品は手元にありません。", true);
        }

        // 新しい版が1つに決まれば窓を出さずに確認へ進む。決まらなければ選び直しの窓で選ばせる（推定で入れない）
        var model = MemberFilePickViewModel.ForRepick(member, name, item, preferNewVersion: true);
        var choice = model.Choices[0];
        var decided = !choice.Selected.IsNone && !choice.Selected.IsKeepCurrent && !choice.Selected.Matches(member);
        if (!decided)
        {
            if (choice.Options.Count(option => !option.IsNone && !option.IsKeepCurrent) == 0)
            {
                return ("送れるファイルが手元にありません。", false);
            }

            if (!MemberFilePickViewModel.AskRepick(model, $"「{name}」の新しい版として送るファイルを選びます。"))
            {
                return (null, false);
            }
        }

        var picked = choice.Picked.Where(option => !option.IsKeepCurrent).ToList();
        if (picked.Count == 0 || picked.All(option => option.Matches(member)))
        {
            return ("今の記録と同じファイルなので、送りませんでした。", false);
        }

        var files = picked.Select(option => option.ToFile()).ToList();
        var packages = files
            .SelectMany(file => ModificationSendPlan.PackagesFor(item, new ModificationMember
            {
                ItemId = member.ItemId,
                VariationId = file.VariationId,
                FileHash = file.FileHash,
                Package = file.Package,
            }))
            .ToList();
        if (packages.Count == 0)
        {
            return ("選んだファイルの中に、Unityへ送れるものがありませんでした。", true);
        }

        // 送信は1列に限る（「Unityへ送る」と同じ。Editor.log は全エディタが共有するので、終わりを取り違える）
        if (UnityImportQueue.IsRunning)
        {
            Notice.Show(UnityImportQueue.BusyMessage, Title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return (null, false);
        }

        var linkedName = record.UnityProject is { } project ? DisplayText.FolderName(project) : null;
        if ((PickTargetIntercept ?? UnityTargetPicker.Pick)(Title, linkedName) is not { } editor)
        {
            return (null, false);
        }

        if (!Confirm(name, picked, editor, linkedName))
        {
            return (null, false);
        }

        IReadOnlyList<UnityQueueOutcome> outcomes;
        sending(true);
        try
        {
            outcomes = SendIntercept is { } send
                ? send(packages)
                : await UnityImportQueue.RunAsync(editor.ProcessId, packages, progress, CancellationToken.None);
        }
        finally
        {
            sending(false);
        }

        if (outcomes.Any(outcome => outcome.Taken))
        {
            services.Recent.TouchAsync(member.ItemId, RecentKind.Used).Forget();
        }

        await FilePresenceNotes.NoteFailedSendsAsync(services, packages.Select(package => (member.ItemId, package)), outcomes, itemChanged);

        // **全部入った時だけ書く。**1つでも入っていなければ、記録を新しい版にすると Unity の中身と食い違う（このボタンを作った理由）
        if (outcomes.Count != packages.Count || !outcomes.All(outcome => outcome.Taken))
        {
            return ("Unityに入らなかったので、使ったファイルの記録は変えていません。" + UnityQueueOutcome.Describe(outcomes), true);
        }

        var result = await services.Commands.ExecuteAsync(new UiCommand.ChangeModificationMemberFiles(record.Id, member, files));
        return result is CommandResult.Failed failed
            ? (failed.Message, true)
            : ($"「{name}」の新しい版を送り、使ったファイルを新しい版に替えました。", false);
    }

    /// <summary>送る前の確認。既定は「キャンセル」（上書きで設定が消えうるので、読まずに進ませない）。</summary>
    private static bool Confirm(string name, IReadOnlyList<MemberFileOption> picked, OpenUnityEditor editor, string? linkedName)
    {
        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var where = linkedName is not null && !string.Equals(editor.ProjectName, linkedName, StringComparison.OrdinalIgnoreCase)
            ? $"紐付けたプロジェクト「{linkedName}」は開いていません。代わりに「{target}」へ送ります。"
            : $"Unityの「{target}」へ送ります。";
        var files = string.Join("\n", picked.Select(option => "・" + option.Text));

        var text = $"「{name}」の新しい版を送ります。\n{files}\n\n{where}\n\n"
            + "上書きで、Unityで変えた設定が消えることがあります。初期のマテリアルのまま改変していた場合などです。"
            + "送る前に、プロジェクトのバックアップをおすすめします。\n\n"
            + "Unityに入った時だけ、使ったファイルの記録を新しい版に替えます。";

        return Notice.Show(
            text, Title, System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK;
    }
}
