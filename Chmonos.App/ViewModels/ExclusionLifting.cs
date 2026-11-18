using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 除外の一覧（<see cref="ExclusionListViewModel"/>）を、除外の記録と外した記録につなぐ。設定の画面と未確定の「除外の解除…」の窓で同じ物を使う。
/// </summary>
internal static class ExclusionLifting
{
    public const string ExcludedRowTip = "未確定の一覧に戻します。元の場所に無いファイルは戻りません。";

    public const string DetachedRowTip = "次の取り込みで、読み取った情報が指す商品にまた紐付きます。";

    /// <summary>除外の記録の一覧（新しい順に読んだ物をそのまま）。</summary>
    public static ExclusionListViewModel ForExcluded(AppServiceContainer services, Func<Task> reload)
        => new(
            "解除",
            "選んだ {0} 件の除外を解除",
            ExcludedRowTip,
            async hashes =>
            {
                // まとめて1回の命令にする（数千件でも記録の書き込みは1回。UiCommand.RestoreExcluded）
                var result = await services.Commands.ExecuteAsync(new UiCommand.RestoreExcluded(hashes));
                await reload();
                return Note(result);
            });

    /// <summary>外した記録の一覧（商品名の順。外した日時は持たない）。</summary>
    public static ExclusionListViewModel ForDetached(AppServiceContainer services, Func<Task> reload)
        => new(
            "戻す",
            "選んだ {0} 件を戻す",
            DetachedRowTip,
            async keys =>
            {
                var records = keys.Select(DetachedKeyOf).OfType<DetachedKey>().ToList();
                await services.Commands.ExecuteAsync(new UiCommand.ForgetDetached(records));
                await reload();
                return records.Count == 1
                    ? "外した記録を消しました。次の取り込みで、読み取った情報が指すならまたその商品に紐付きます。"
                    : $"{records.Count} 件の外した記録を消しました。次の取り込みで、読み取った情報が指すならまたその商品に紐付きます。";
            });

    public static IReadOnlyList<ExclusionEntry> Entries(IEnumerable<ExcludedFile> files)
        => files.Select(file => new ExclusionEntry(file.Hash, file.Path, file.Reason ?? string.Empty)).ToList();

    public static IReadOnlyList<ExclusionEntry> Entries(IEnumerable<DetachedRecord> records)
        => records.Select(record => new ExclusionEntry(
                record.Hash + ":" + record.ItemId, record.Path, $"「{record.ItemName}」から外しました"))
            .ToList();

    /// <summary>行の鍵「ハッシュ:商品ID」を戻す。商品IDに「:」は入らない（数字か local-）ので、最後の「:」で分ける。</summary>
    private static DetachedKey? DetachedKeyOf(string key)
        => key.LastIndexOf(':') is var at and > 0 ? new DetachedKey(key[..at], key[(at + 1)..]) : null;

    /// <summary>
    /// 解除した結果の1行。解除するとその場で未確定に戻す（ユーザ判断 2026-10-05）。
    /// 戻らなかった物は、どこにも出ない訳を言う（元の場所に無い・同じファイルを商品が持つ）。
    /// </summary>
    internal static string Note(CommandResult result)
    {
        if (result is not CommandResult.ExclusionLifted { Outcomes: var outcomes })
        {
            return "除外を解除できませんでした。";
        }

        int Count(ExclusionLiftOutcome outcome) => outcomes.Values.Count(value => value == outcome);
        var back = Count(ExclusionLiftOutcome.BackInUnresolved);
        var missing = Count(ExclusionLiftOutcome.FileNotFound);
        var owned = Count(ExclusionLiftOutcome.OwnedByItem);
        var lifted = back + missing + owned;
        if (lifted == 0)
        {
            return "除外は解除済みでした。";
        }

        if (lifted == 1)
        {
            return back == 1 ? "除外を解除しました。未確定の一覧に戻しました。"
                : missing == 1 ? "除外を解除しました。元の場所にファイルが見つからないので、未確定には戻していません。"
                : "除外を解除しました。同じファイルが商品に登録されています。";
        }

        var parts = new List<string> { $"{lifted} 件の除外を解除しました。" };
        if (back > 0)
        {
            parts.Add($"{back} 件を未確定の一覧に戻しました。");
        }

        if (missing > 0)
        {
            parts.Add($"{missing} 件は元の場所に見つからないので、未確定には戻していません。");
        }

        if (owned > 0)
        {
            parts.Add($"{owned} 件は同じファイルが商品に登録されています。");
        }

        return string.Concat(parts);
    }
}
