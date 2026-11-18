using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>除外の一覧の共通部品（open.md 132・171）。</summary>
public class ExclusionListTests
{
    private static ExclusionListViewModel List(List<IReadOnlyList<string>> lifted)
    {
        ExclusionListViewModel? list = null;
        list = new ExclusionListViewModel("解除", "選んだ {0} 件の除外を解除", string.Empty, keys =>
        {
            lifted.Add(keys);
            return Task.FromResult("済み");
        });
        list.SetEntries(
        [
            new ExclusionEntry("c", @"D:\new\third.zip", "未確定画面から除外"),
            new ExclusionEntry("b", @"D:\old\second.psd"),
            new ExclusionEntry("a", @"D:\old\first.zip"),
        ]);
        return list;
    }

    [Fact]
    public Task 開くまで行を作らず_並びは渡した順のまま() => UiThread.Run(() =>
    {
        var list = List([]);

        Assert.Empty(list.Rows);
        Assert.Equal("3 件", list.CountText);

        list.IsActive = true;
        Assert.Equal(["c", "b", "a"], list.Rows.Select(row => row.Key));
        return Task.CompletedTask;
    });

    [Fact]
    public Task 探す欄はパスとファイル名で絞り_空白で区切ると全部を含む物() => UiThread.Run(() =>
    {
        var list = List([]);
        list.IsActive = true;

        list.Query = "OLD";
        Assert.Equal(["b", "a"], list.Rows.Select(row => row.Key));
        list.Query = "old zip";
        Assert.Equal(["a"], list.Rows.Select(row => row.Key));
        list.Query = "無い";
        Assert.True(list.IsFilteredEmpty);
        return Task.CompletedTask;
    });

    /// <summary>絞り込みで隠れた行まで解除すると、何を解除したのか画面から分からない。選んだ物は絞り直しても覚えている。</summary>
    [Fact]
    public Task まとめての解除は_見えている行のうち選んだ物だけに効く() => UiThread.Run(async () =>
    {
        var lifted = new List<IReadOnlyList<string>>();
        var list = List(lifted);
        list.IsActive = true;
        list.SelectAllCommand.Execute(null);
        Assert.Equal("選んだ 3 件の除外を解除", list.LiftCheckedText);

        list.Query = "old";
        Assert.Equal(2, list.CheckedVisibleCount);
        Assert.Equal("選んだ 2 件の除外を解除", list.LiftCheckedText);

        await list.LiftAsync(list.CheckedVisibleKeys());
        Assert.Equal(["b", "a"], Assert.Single(lifted));
        Assert.Equal("済み", list.Note);

        // 絞りを外すと、隠れていた選びも残っている
        list.Query = string.Empty;
        Assert.True(list.Rows.Single(row => row.Key == "c").IsChecked);
    });

    [Fact]
    public Task 行の解除はその1件だけを渡す() => UiThread.Run(async () =>
    {
        var lifted = new List<IReadOnlyList<string>>();
        var list = List(lifted);
        list.IsActive = true;

        list.Rows[1].LiftCommand.Execute(null);
        await UiThread.Until(() => lifted.Count == 1, "解除が呼ばれる");

        Assert.Equal(["b"], lifted[0]);
    });

    [Fact]
    public void 結果の1行は_訳ごとの件数を言う()
    {
        var outcomes = new Dictionary<string, ExclusionLiftOutcome>
        {
            ["a"] = ExclusionLiftOutcome.BackInUnresolved,
            ["b"] = ExclusionLiftOutcome.BackInUnresolved,
            ["c"] = ExclusionLiftOutcome.FileNotFound,
            ["d"] = ExclusionLiftOutcome.OwnedByItem,
            ["e"] = ExclusionLiftOutcome.NotExcluded,
        };

        Assert.Equal(
            "4 件の除外を解除しました。2 件を未確定の一覧に戻しました。1 件は元の場所に見つからないので、未確定には戻していません。1 件は同じファイルが商品に登録されています。",
            ExclusionLifting.Note(new CommandResult.ExclusionLifted(outcomes)));
        Assert.Equal(
            "除外を解除しました。元の場所にファイルが見つからないので、未確定には戻していません。",
            ExclusionLifting.Note(new CommandResult.ExclusionLifted(new Dictionary<string, ExclusionLiftOutcome> { ["c"] = ExclusionLiftOutcome.FileNotFound })));
    }

    /// <summary>
    /// 設定の欄から選んでまとめて解除すると、除外の記録は1回だけ書き、選んでいない物は残り、在る物は未確定に戻る。
    /// </summary>
    [Fact]
    public Task 設定でまとめて解除すると_1回で書き_選んでいない除外は残る() => TestApp.Run(async app =>
    {
        var entries = new List<ExcludedEntry>();
        for (var i = 0; i < 5; i++)
        {
            var path = app.NewFile($"作り物{i}.psd", System.Text.Encoding.UTF8.GetBytes($"中身{i}"));
            var hash = await Chmonos.Core.Scanning.FileHasher.ComputeSha256Async(path);
            entries.Add(new ExcludedEntry { Hash = hash, Paths = [path], ExcludedAt = DateTimeOffset.Now.AddMinutes(i) });
        }

        await app.Store.Excluded.SaveAsync(entries);
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading && settings.ExcludedList.HasAny, "設定を読み終わる");
        settings.IsExcludedExpanded = true;

        // 新しい順（最後に除外した物が上）
        Assert.Equal(entries.Select(entry => entry.Hash).Reverse(), settings.ExcludedList.Rows.Select(row => row.Key));

        settings.ExcludedList.Rows[0].IsChecked = true;
        settings.ExcludedList.Rows[1].IsChecked = true;
        settings.ExcludedList.Rows[2].IsChecked = true;
        var writes = app.Store.Excluded.WriteCount;
        settings.ExcludedList.LiftCheckedCommand.Execute(null);
        await UiThread.Until(() => settings.ExcludedList.Note.Length > 0, "結果が出る");
        await app.SettleAsync();

        // WriteCount は1回の書き込みで 2 進む
        Assert.Equal(writes + 2, app.Store.Excluded.WriteCount);
        Assert.Equal(entries.Take(2).Select(entry => entry.Hash), app.Store.Excluded.Load().Select(entry => entry.Hash));
        Assert.Equal(3, app.Store.Unresolved.Load().Count(file => entries.Skip(2).Any(entry => entry.Hash == file.Hash)));
        Assert.Equal("3 件の除外を解除しました。3 件を未確定の一覧に戻しました。", settings.ExcludedList.Note);
        await UiThread.Until(() => settings.ExcludedList.TotalCount == 2, "一覧を読み直す");
        Assert.Equal(0, settings.ExcludedList.CheckedVisibleCount);
    });
}
