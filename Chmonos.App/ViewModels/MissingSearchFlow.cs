using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 名指しした商品の、見つからないファイル・登録フォルダを探す口（商品ページの「探す…」と、検索の選択の帯の「その他」。
/// ユーザ判断 2026-11-20）。範囲を選ぶ窓は取り込み画面の「見つからないファイルを探す…」と同じ物で、探す処理も同じ命令
/// （<see cref="UiCommand.FindMissingFiles"/>）に商品の ID を渡すだけ。
/// </summary>
/// <remarks>
/// 取り込み画面の口は全商品を探すので、1商品のために押すと、ほかの商品の記録まで黙って変わり、読む量も増える。
/// 中身で探す（大きさの合う物だけハッシュを取る）ので、絞れば読む量も減る。
/// </remarks>
internal static class MissingSearchFlow
{
    /// <summary>
    /// 範囲を選ぶ窓を出し、選んだフォルダの中から名指しした商品の分だけを探す。窓でやめたら null。
    /// 結び直した・見つからなかった日時を付けたときは、検索の写しを読み直す（取り込み画面の口と同じ。点検 2026-09-30 の C）。
    /// </summary>
    public static async Task<CommandResult?> RunAsync(
        AppServiceContainer services,
        MainViewModel main,
        IReadOnlyList<string> itemIds,
        IProgress<(int Hashed, string? Detail)>? progress = null,
        Action? started = null)
    {
        var scope = new MissingSearchScopeViewModel(services.Settings.WatchedFolders ?? []);
        if (!MissingSearchScopeViewModel.Ask(scope))
        {
            return null;
        }

        started?.Invoke();
        var result = await services.Commands.ExecuteAsync(new UiCommand.FindMissingFiles(progress, scope.SelectedFolders, itemIds));
        if (result is CommandResult.MissingFilesSearched { Result: { Relinked: > 0 } or { MarkedItems: > 0 } })
        {
            await main.ReloadLibraryAsync();
        }

        return result;
    }

    /// <summary>結果の1文（取り込み画面の要約と同じ言い方）。探せなかった場所・読めなかった物があれば続けて言う。</summary>
    public static string SummaryOf(CommandResult? result, IReadOnlyList<string> watchedFolders) => result switch
    {
        CommandResult.MissingFilesSearched { Result: var found } when found.MissingBefore == 0 && found.MissingFolders.Count == 0
            => string.Join(string.Empty, ["見つからないファイルはありませんでした。", .. ImportViewModel.MissingSearchNoteLines(found, watchedFolders)]),
        CommandResult.MissingFilesSearched { Result: var found }
            => string.Join(string.Empty, [SummaryLine(found), .. ImportViewModel.MissingSearchNoteLines(found, watchedFolders)]),
        CommandResult.Failed failed => failed.Message,
        _ => string.Empty,
    };

    /// <summary>ファイルが無く、登録フォルダだけが見つからなかった回は、フォルダの数で言う（ファイルの「0 件を探しました」と言わない）。</summary>
    private static string SummaryLine(MissingFileSearchResult found)
        => found.MissingBefore > 0
            ? ImportViewModel.MissingSearchSummary(found)
            : found.MissingFolders.Any(folder => folder.Candidates.Count > 0)
                ? "見つからないフォルダの候補が見つかりました。"
                : "見つからないフォルダに合うフォルダはありませんでした。";
}
