using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 長い作業の帯の進み具合の棒（ユーザ判断 2026-10-02）と、バックアップから戻すの「中止」。
/// 棒は一時展開の帯と同じで、件数が分かるまでは流れる棒、分かったら割合を出す。
/// </summary>
public class LongJobProgressTests
{
    [Fact]
    public Task 件数が来る前は流れる棒で_来たら割合になり_畳むと消える() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        using var stop = new CancellationTokenSource();

        Assert.False(main.HasLongJobProgress);

        var job = main.BeginLongJob("バックアップを書き出しています", "書き出しが終わるまで、保存は待たされます。見ることはできます。", stop);
        Assert.NotNull(job);

        // 件数が分かるまで（合計 0）は流れる棒
        Assert.False(main.HasLongJobProgress);
        main.ReportLongJob("バックアップを書き出しています… 0/0", 0, 0);
        Assert.False(main.HasLongJobProgress);
        Assert.Equal(0, main.LongJobProgress);

        main.ReportLongJob("バックアップを書き出しています… 25/100", 25, 100);
        Assert.True(main.HasLongJobProgress);
        Assert.Equal(0.25, main.LongJobProgress, 6);

        // 数え間違いで済んだ件数が合計を超えても、棒は溢れない
        main.ReportLongJob("x", 120, 100);
        Assert.Equal(1, main.LongJobProgress);

        job.Dispose();

        Assert.False(main.HasLongJobProgress);
        Assert.Equal(0, main.LongJobProgress);

        // 畳んだ後に裏から遅れて届いた分で、棒が帯に残らない
        main.ReportLongJob("遅れて届いた", 50, 100);
        Assert.False(main.HasLongJobProgress);
        Assert.Equal(string.Empty, main.LongJobText);
    });

    [Fact]
    public Task 次の作業を始めると_前の作業の棒は残らない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var job = main.BeginLongJob("対応アバターを検出しています", "n", first)!;
        main.ReportLongJob("a", 5, 10);
        job.Dispose();

        using var next = main.BeginLongJob("候補を検索しています", "n", second)!;

        Assert.False(main.HasLongJobProgress);
        Assert.Equal(0, main.LongJobProgress);
    });

    /// <summary>書き出しの途中の件数が、設定の画面の1行だけでなく帯の棒にも渡る。</summary>
    [Fact]
    public Task 書き出しの進み具合が_帯の棒へ渡る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        var dir = Path.Combine(app.Root, "backup-out");
        Directory.CreateDirectory(dir);

        var seen = new List<(bool Has, double Value)>();
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.LongJobProgress))
            {
                seen.Add((main.HasLongJobProgress, main.LongJobProgress));
            }
        };

        await settings.ExportBackupToAsync(Path.Combine(dir, "backup.zip"), withImages: false);
        await app.SettleAsync();

        // 件数が分かってから割合が出て、最後は畳んで 0 に戻る
        Assert.Contains(seen, step => step.Has && step.Value > 0);
        Assert.Equal((false, 0d), seen[^1]);
    });

    /// <summary>
    /// 戻すの中止。窓で聞く所は試験で出せないので、聞き終えた後の本体を呼ぶ。
    /// 門を試験が先に持ち、戻すを門の前で待たせてから止める（書き出しの中止の試験と同じ組み立て）。
    /// 成功させない（戻せると保存先の場所を書き換えて開き直す）ので、止めた後に保存先が変わっていないことで見る
    /// </summary>
    [Fact]
    public Task 戻すを中止すると_保存先は変わらず_門が開き_戻す先は空で_帯で知らせる() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        var rootBefore = app.Services.Paths.Root;
        var destination = Path.Combine(app.Root, "restore-to");
        Directory.CreateDirectory(destination);

        Task restore;
        using (await StoreWriteGate.HoldAsync())
        {
            restore = settings.RestoreBackupToAsync(Path.Combine(app.Root, "any.zip"), destination);
            main.ShowSearchCommand.Execute(null);

            Assert.True(main.CanStopLongJob);
            Assert.True(main.StopLongJobCommand.CanExecute(null));
            main.StopLongJobCommand.Execute(null);
            await restore;
        }

        Assert.False(main.IsLongJobRunning);
        Assert.False(main.HasLongJobProgress);
        Assert.Equal(StoreJobKind.None, main.StoreJob);
        Assert.Equal("バックアップから戻すのを中止しました。戻す先は空のままです。", main.StoreJobNoticeText);
        Assert.Equal(rootBefore, app.Services.Paths.Root);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

        // 成功のときだけ閉じたまま返す。止めたら開いている
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using (await StoreWriteGate.HoldAsync(timeout.Token))
        {
        }

        Assert.True(OpenSettings(main).ExportBackupCommand.CanExecute(null));

        // 元の保存先はそのまま使える：画面からの書き込みが今の保存先へ届く
        var before = File.ReadAllText(app.Services.Paths.SettingsFile);
        var changed = await app.Services.Commands.ExecuteAsync(
            new Chmonos.Core.Commands.UiCommand.ChangeSettings(settings => settings with { SaveImages = !settings.SaveImages }));
        Assert.IsNotType<Chmonos.Core.Commands.CommandResult.Failed>(changed);
        Assert.Equal(rootBefore, app.Services.Paths.Root);
        Assert.NotEqual(before, File.ReadAllText(Path.Combine(rootBefore, "settings.json")));
    });

    private static SettingsViewModel OpenSettings(MainViewModel main)
    {
        main.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
    }
}
