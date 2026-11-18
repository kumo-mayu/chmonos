using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「zipで登録し直す」の流れ（命令・窓で聞く事情・結果の文）。通知の行と商品ページの黄色の枠の両方から使う
/// （ユーザ判断 2026-11-19・open 150：同じ処理・同じ窓）。前は通知の中にだけあり、商品ページからは呼べなかった。
/// 結果をどこに出すか（行の近く・一覧の見出し・ファイルの欄）は呼ぶ側が決める。
/// </summary>
internal static class ArchiveSwapFlow
{
    /// <summary>流れの結果。</summary>
    /// <param name="Done">登録し直した（zipを付けてフォルダの登録を外した）。</param>
    /// <param name="Text">言う文。やめた時は空。</param>
    /// <param name="TakenFrom">付け直した・両方に付けた相手の商品（検索の写しに知らせる）。</param>
    /// <param name="OpenItemId">「その商品を開く」を選んだ時の行き先。</param>
    public sealed record Result(bool Done, string Text, IReadOnlyList<ArchiveHolder> TakenFrom, string? OpenItemId);

    private const string Title = "zipで登録し直す";

    public static async Task<Result> RunAsync(CommandHandler commands, string itemId, string folderPath)
    {
        // zip が除外してある・ほかの商品に付いているときは、Core は何も書かずにそう返す。人が窓で選んだときだけ、頼みを足して呼び直す
        // （除外も持ち主も人が決めたこと。押した方が新しい判断として扱うが、黙っては上書きしない。ユーザ判断 2026-10-05）。
        // 除外を解いた後でほかの持ち主が分かることがあるので、聞くのは多くても2回
        var command = new UiCommand.SwapFolderForArchive(itemId, folderPath);
        IReadOnlyList<ArchiveHolder> takenFrom = [];
        var outcome = await commands.ExecuteAsync(command);
        for (var asked = 0; asked < 2 && outcome is CommandResult.ArchiveSwapped { Outcome: { } pending }; asked++)
        {
            if (pending.Result == ArchiveSwapResult.Excluded && !command.LiftExclusion)
            {
                var answer = Services.Notice.Show(
                    $"「{pending.ArchiveName}」は管理対象から除外しています。除外を解除して、この商品に登録しますか？\n\n"
                    + "あとで商品ページの「この商品から外す」で外せます。",
                    Title,
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.Cancel);
                if (answer != System.Windows.MessageBoxResult.OK)
                {
                    return new Result(false, string.Empty, [], null);
                }

                command = command with { LiftExclusion = true };
            }
            else if (pending.Result == ArchiveSwapResult.OwnedElsewhere
                     && command.OtherHolders == OtherHolders.Ask && pending.Holders.Count > 0)
            {
                var choice = ChoiceQuestion.Ask(ChoiceQuestion.OwnedElsewhere(Title, pending.ArchiveName ?? string.Empty, pending.Holders));
                if (choice == Views.ChoiceDialogResult.First)
                {
                    return new Result(false, string.Empty, [], pending.Holders[0].ItemId);
                }

                if (ChoiceQuestion.Answer(choice) is not { } answer)
                {
                    return new Result(false, string.Empty, [], null);
                }

                command = command with { OtherHolders = answer };
                takenFrom = pending.Holders;
            }
            else
            {
                break;
            }

            outcome = await commands.ExecuteAsync(command);
        }

        if (outcome is not CommandResult.ArchiveSwapped { Outcome: { } swapped })
        {
            return new Result(false, "登録し直せませんでした。", takenFrom, null);
        }

        return swapped.Result switch
        {
            ArchiveSwapResult.Registered => new Result(
                true,
                $"「{swapped.ArchiveName}」で登録し直しました。展開したフォルダのファイルは削除していません。" + MembersText(swapped.Members),
                takenFrom,
                null),
            ArchiveSwapResult.AlreadyRegistered => new Result(
                true,
                $"「{swapped.ArchiveName}」は登録済みなので、展開したフォルダの登録だけ外しました。ファイルは削除していません。"
                    + MembersText(swapped.Members),
                takenFrom,
                null),
            ArchiveSwapResult.ArchiveMissing => new Result(
                false,
                "隣にzipが見つかりませんでした。移動したか、外付けを外している可能性があります。登録はそのままにしてあります。",
                takenFrom,
                null),
            ArchiveSwapResult.ArchiveUnreadable => new Result(
                false, "zipを読めませんでした。ほかのアプリが開いている可能性があります。登録はそのままです。", takenFrom, null),

            // 聞いた後でまた別の事情が返った（間に除外・登録された）。黙って進めず、そのままにする
            ArchiveSwapResult.Excluded or ArchiveSwapResult.OwnedElsewhere => new Result(
                false, "zipの扱いが変わったので、登録はそのままにしました。もう一度押してください。", takenFrom, null),
            _ => new Result(false, "この商品は見つかりませんでした。", takenFrom, null),
        };
    }

    /// <summary>
    /// 改変の記録を付け替えた数（open 144：当たらない・2つ当たる行は残し、結果で数を言う）。
    /// 付け替える行が無ければ何も言わない（改変に使っていない商品がほとんどなので、毎回「0件」と言うと読む物が増えるだけ）
    /// </summary>
    internal static string MembersText(FolderMemberRelink.Counts counts) => counts switch
    {
        { Relinked: 0, Unmatched: 0 } => string.Empty,
        { Unmatched: 0 } => $"改変の記録 {counts.Relinked} 件を、zipの中の同じファイルに付け替えました。",
        { Relinked: 0 } => $"zipの中に同じファイルが見つからない改変の記録が {counts.Unmatched} 件あり、そのままにしました。",
        _ => $"改変の記録 {counts.Relinked} 件を、zipの中の同じファイルに付け替えました。"
            + $"見つからない {counts.Unmatched} 件はそのままにしました。",
    };
}
