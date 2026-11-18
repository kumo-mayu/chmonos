using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// まとめて管理対象から除外する3か所（フォルダビューの「この下の未確定 n 件を…」・未確定の「まとめて除外」・1件／元zipの束）が、
/// 命令を1回だけ呼び、記録を1回で書くこと（2026-10-01）。
///
/// 前は1個ごとに命令を呼び、1個ごとに除外の記録と未確定の記録を丸ごと読み書きしていた（5,000 個で約2分34秒）。
/// 命令の数は、記録を書いた回数（<see cref="Core.Storage.JsonFileStore{T}.WriteCount"/>。1回書くと 2 進む）で見る。
/// 元zipの束は、展開した中身の記録（Zone.Identifier の「すべて展開」の跡）を作り物で置けないので、ここでは1件の道で見る（同じ関数を通る）。
/// </summary>
public class BulkExcludeTests
{
    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    /// <summary>今のファイルの中身どおりのハッシュ・大きさ・日時で作る未確定の記録。</summary>
    private static UnresolvedFile AsIs(string path)
    {
        var info = new System.IO.FileInfo(path);
        return new UnresolvedFile
        {
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))),
            Paths = [path],
            SizeBytes = info.Length,
            ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveAsync(TestApp app, params string[] names)
    {
        // 中身と記録のハッシュを本物に合わせる。除外の解除は今のファイルを読み直し、中身が変わった物は未確定へ戻さない
        await app.Store.Unresolved.SaveAsync([.. names.Select(name => AsIs(app.NewFile(name, System.Text.Encoding.UTF8.GetBytes(name))))]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.True(resolve.IsLoaded);
        return (main, resolve);
    }

    private static string[] Names(IEnumerable<UnresolvedFile> files) => Names(files.Select(file => file.Paths[0]));

    private static string[] Names(IEnumerable<ExcludedEntry> entries) => Names(entries.Select(entry => entry.Paths[0]));

    private static string[] Names(IEnumerable<string> paths)
        => [.. paths.Select(path => System.IO.Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Fact]
    public Task フォルダごと除外は_1回で書き_全部が除外に入って未確定から消える() => TestApp.Run(async app =>
    {
        var names = Enumerable.Range(0, 6).Select(index => $@"materials\sub{index % 2}\part{index}.png").ToArray();
        List<UnresolvedFile> files = [.. names.Select(name => Unresolved(app.NewFile(name)))];
        await app.Store.Unresolved.SaveAsync(files);
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");

        // 未確定の6件を全部含む行（ボリュームか取り込み元）を選ぶ
        FolderViewDetail? detail = null;
        foreach (var row in folders.Rows.ToList())
        {
            folders.Selected = row;
            if (folders.Detail is FolderViewDetail { Unresolved.Count: 6 } found)
            {
                detail = found;
                break;
            }
        }

        Assert.NotNull(detail);
        await app.SettleAsync();
        app.Answer = _ => MessageBoxResult.OK;
        var excludedWrites = app.Store.Excluded.WriteCount;
        var unresolvedWrites = app.Store.Unresolved.WriteCount;

        folders.ExcludeUnresolvedCommand.Execute(detail);
        await app.SettleAsync();

        Assert.Equal(excludedWrites + 2, app.Store.Excluded.WriteCount);
        Assert.Equal(unresolvedWrites + 2, app.Store.Unresolved.WriteCount);
        var excluded = app.Store.Excluded.Load();
        Assert.Equal(files.Select(file => file.Hash).Order(), excluded.Select(entry => entry.Hash).Order());
        Assert.All(excluded, entry => Assert.Equal("フォルダビューからフォルダごと除外", entry.Reason));
        Assert.Empty(app.Store.Unresolved.Load());
        Assert.Equal("6 件を管理対象から除外しました。", folders.Status);
    });

    [Fact]
    public Task まとめて除外は_1回で書き_まとめて戻せる() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip", @"d\fourth.zip");
        foreach (var row in resolve.Files.Where(row => row.FileName != "fourth.zip"))
        {
            row.IsSelected = true;
        }

        app.Answer = _ => MessageBoxResult.OK;
        var excludedWrites = app.Store.Excluded.WriteCount;
        var unresolvedWrites = app.Store.Unresolved.WriteCount;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(excludedWrites + 2, app.Store.Excluded.WriteCount);
        Assert.Equal(unresolvedWrites + 2, app.Store.Unresolved.WriteCount);
        Assert.Equal(["first.zip", "second.zip", "third.zip"], Names(app.Store.Excluded.Load()));
        Assert.Equal(["fourth.zip"], Names(app.Store.Unresolved.Load()));
        Assert.Equal("fourth.zip", Assert.Single(resolve.Files).FileName);
        Assert.True(resolve.HasExcluded);
        await UiThread.Until(() => main.UnresolvedCount == 1, "除外したらナビの未確定の数が減る");

        // 「除外の解除…」の窓で、見えている3件を全部選んでまとめて解除する（open.md 132）
        var excludedBefore = app.Store.Excluded.WriteCount;
        app.InExclusionDialog = async list =>
        {
            Assert.Equal(3, list.Rows.Count);
            list.SelectAllCommand.Execute(null);
            await list.LiftAsync(list.CheckedVisibleKeys());
        };
        resolve.OpenExclusionsCommand.Execute(null);
        await app.SettleAsync();

        Assert.Single(app.ExclusionDialogs);
        Assert.Equal(excludedBefore + 2, app.Store.Excluded.WriteCount);
        Assert.Empty(app.Store.Excluded.Load());
        Assert.Equal(["first.zip", "fourth.zip", "second.zip", "third.zip"], Names(app.Store.Unresolved.Load()));
        await UiThread.Until(() => !resolve.HasExcluded, "除外が無くなれば「除外の解除…」は消える");
        Assert.Equal(4, resolve.Files.Count);
        Assert.Equal("3 件の除外を解除しました。3 件を未確定の一覧に戻しました。", resolve.ListNoticeText);

        // 戻したときもナビの数を合わせる。前は一覧だけ読み直し、次に画面を移るまで減ったままの数が残っていた
        await UiThread.Until(() => main.UnresolvedCount == 4, "戻したらナビの未確定の数も戻る");
    });

    [Fact]
    public Task 選んだ1件の除外も_同じ命令で1回だけ書く() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        app.Answer = _ => MessageBoxResult.OK;
        var excludedWrites = app.Store.Excluded.WriteCount;
        var unresolvedWrites = app.Store.Unresolved.WriteCount;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(excludedWrites + 2, app.Store.Excluded.WriteCount);
        Assert.Equal(unresolvedWrites + 2, app.Store.Unresolved.WriteCount);
        Assert.Equal("未確定画面から除外", Assert.Single(app.Store.Excluded.Load()).Reason);
        Assert.Equal(["second.zip"], Names(app.Store.Unresolved.Load()));
    });

    /// <summary>
    /// 「除外の解除…」は直前に外した物だけでなく、前から除外していた物も並べる（open.md 132：前は直前の物しか戻せなかった）。
    /// 選んだ物だけを解除し、選ばなかった物の記録（日時・理由）は残る。
    /// </summary>
    [Fact]
    public Task 除外の解除は前から除外していた物も並べ_選んだ物だけを解除する() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip");
        var before = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var old = app.Store.Unresolved.Load().Single(file => file.Paths[0].EndsWith("first.zip", StringComparison.Ordinal));
        await app.Store.Excluded.SaveAsync([new ExcludedEntry { Hash = old.Hash, Paths = old.Paths, ExcludedAt = before, Reason = "前に外した" }]);
        resolve.Selected = resolve.Files.Single(row => row.FileName == "second.zip");

        app.Answer = _ => MessageBoxResult.OK;
        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        app.InExclusionDialog = async list =>
        {
            // 新しく除外した物が上
            Assert.Equal(["second.zip", "first.zip"], list.Rows.Select(row => System.IO.Path.GetFileName(row.Path)));
            list.SetChecked(list.Rows[0].Key, true);
            await list.LiftAsync(list.CheckedVisibleKeys());
        };
        resolve.OpenExclusionsCommand.Execute(null);
        await app.SettleAsync();

        var kept = Assert.Single(app.Store.Excluded.Load());
        Assert.Equal(old.Hash, kept.Hash);
        Assert.Equal("前に外した", kept.Reason);
        Assert.Equal(before, kept.ExcludedAt);
        Assert.Contains("second.zip", Names(app.Store.Unresolved.Load()));
        Assert.True(resolve.HasExcluded);
    });

    [Fact]
    public Task 何も解除せずに閉じれば_何も書かない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        app.Answer = _ => MessageBoxResult.OK;
        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();
        var excludedWrites = app.Store.Excluded.WriteCount;
        var unresolvedWrites = app.Store.Unresolved.WriteCount;

        resolve.OpenExclusionsCommand.Execute(null);
        await app.SettleAsync();

        Assert.Single(app.ExclusionDialogs);
        Assert.Equal(excludedWrites, app.Store.Excluded.WriteCount);
        Assert.Equal(unresolvedWrites, app.Store.Unresolved.WriteCount);
        Assert.True(resolve.HasExcluded);
    });
}
