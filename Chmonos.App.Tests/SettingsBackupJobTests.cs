using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「バックアップを書き出す」（公開前の点検 2026-10-01）。
///
/// 設定の画面は開くたびに作り直すので、走っている状態を画面に持つと、途中で別の画面へ移って戻ったときに
/// 進み具合が消え、ボタンがまた押せ、終わった知らせもどこにも出なかった。書き出しの間は保存が止まるのに、帯も中止の口も無かった。
///
/// 書き出しを途中で止めておくのに、書き込みの門（<see cref="StoreWriteGate"/>）を試験が先に持つ。
/// 書き出しは門の前で待つので「走っている」間の画面を見られる。門は1つなので、試験の終わりに必ず開ける
/// </summary>
public class SettingsBackupJobTests
{
    private static SettingsViewModel OpenSettings(MainViewModel main)
    {
        main.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
    }

    private static string ZipPath(TestApp app)
    {
        var dir = Path.Combine(app.Root, "backup-out");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "backup.zip");
    }

    [Fact]
    public Task 書き出しの途中で画面を移って戻っても_進み具合が見え_押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        var zip = ZipPath(app);

        Task export;
        using (await StoreWriteGate.HoldAsync())
        {
            export = settings.ExportBackupToAsync(zip, withImages: false);

            Assert.True(main.IsLongJobRunning);
            Assert.Equal("書き出しが終わるまで、保存は待たされます。見ることはできます。", main.LongJobNote);

            main.ShowSearchCommand.Execute(null);
            var reopened = OpenSettings(main);

            Assert.NotSame(settings, reopened);
            Assert.True(reopened.IsBackingUp);
            Assert.StartsWith("バックアップを書き出しています", reopened.Status);
            Assert.False(reopened.ExportBackupCommand.CanExecute(null));
            Assert.False(reopened.RestoreBackupCommand.CanExecute(null));
            Assert.False(reopened.CanChangeRoot);
            settings = reopened;
        }

        await export;

        Assert.False(settings.IsBackingUp);
        Assert.False(main.IsLongJobRunning);
        Assert.StartsWith("バックアップに ", settings.Status);
        Assert.EndsWith("を書き出しました。", settings.Status);
        Assert.True(settings.ExportBackupCommand.CanExecute(null));
        Assert.False(main.HasStoreJobNotice);
        Assert.True(File.Exists(zip));
    });

    [Fact]
    public Task 設定の画面を離れている間に終わると_帯で知らせ_設定を開くとその1行へ移る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);

        Task export;
        using (await StoreWriteGate.HoldAsync())
        {
            export = settings.ExportBackupToAsync(ZipPath(app), withImages: false);
            main.ShowSearchCommand.Execute(null);
            Assert.False(main.HasStoreJobNotice);
        }

        await export;

        Assert.True(main.HasStoreJobNotice);
        Assert.StartsWith("バックアップに ", main.StoreJobNoticeText);
        var notice = main.StoreJobNoticeText;

        var reopened = OpenSettings(main);

        Assert.Equal(notice, reopened.Status);
        Assert.False(main.HasStoreJobNotice);
    });

    /// <summary>
    /// 書き出せたら、帯にも設定の画面にも「エクスプローラで開く」を出す（ユーザ判断 2026-10-01）。開くのは zip の中ではなく、zip を選んだ状態。
    /// 試験は本物のエクスプローラを開かず、渡された道を控える（<see cref="TestApp.Revealed"/>）
    /// </summary>
    [Fact]
    public Task 書き出せたら_帯のエクスプローラで開くが書き出したzipを渡し_設定を開いた後もその1行の間だけ出る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        var zip = ZipPath(app);

        Task export;
        using (await StoreWriteGate.HoldAsync())
        {
            export = settings.ExportBackupToAsync(zip, withImages: false);
            main.ShowSearchCommand.Execute(null);
            Assert.False(main.HasStoreJobNoticeZip);
        }

        await export;

        Assert.True(main.HasStoreJobNoticeZip);
        Assert.True(main.RevealStoreJobZipCommand.CanExecute(null));
        main.RevealStoreJobZipCommand.Execute(null);
        Assert.Equal([zip], app.Revealed);

        // 開いた後も知らせは残す（結果は読める）。設定を開くと、その1行の横へ移る
        Assert.True(main.HasStoreJobNotice);
        var reopened = OpenSettings(main);
        Assert.False(main.HasStoreJobNoticeZip);
        Assert.True(reopened.HasExportedZip);
        reopened.RevealExportedZipCommand.Execute(null);
        Assert.Equal([zip, zip], app.Revealed);

        // 1行が別の知らせに替わったら引っ込める（次の書き出しを始めた）
        File.Delete(zip);
        using (await StoreWriteGate.HoldAsync())
        {
            export = reopened.ExportBackupToAsync(zip, withImages: false);
            Assert.False(reopened.HasExportedZip);
            Assert.False(reopened.RevealExportedZipCommand.CanExecute(null));
        }

        await export;
        Assert.True(reopened.HasExportedZip);
    });

    [Fact]
    public Task 設定の画面にいる間に書き出せたら_その1行の横にエクスプローラで開くが出る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        var zip = ZipPath(app);

        await settings.ExportBackupToAsync(zip, withImages: false);

        Assert.StartsWith("バックアップに ", settings.Status);
        Assert.True(settings.HasExportedZip);
        Assert.False(main.HasStoreJobNotice);
        settings.RevealExportedZipCommand.Execute(null);
        Assert.Equal([zip], app.Revealed);
    });

    [Fact]
    public Task 帯の中止で止めると_zipを残さず_保存先の中身も変わらない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        await app.SettleAsync();
        var zip = ZipPath(app);
        var before = Snapshot(app.Services.Paths.Root);

        Task export;
        using (await StoreWriteGate.HoldAsync())
        {
            export = settings.ExportBackupToAsync(zip, withImages: true);
            main.ShowSearchCommand.Execute(null);

            Assert.True(main.StopLongJobCommand.CanExecute(null));
            main.StopLongJobCommand.Execute(null);
            await export;
        }

        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(zip)!));
        Assert.Equal(before, Snapshot(app.Services.Paths.Root));
        Assert.False(main.IsLongJobRunning);
        Assert.Equal(StoreJobKind.None, main.StoreJob);
        Assert.Equal("バックアップの書き出しを中止しました。zipは作っていません。", main.StoreJobNoticeText);

        // zip が無いので「エクスプローラで開く」は出さない
        Assert.False(main.HasStoreJobNoticeZip);
        Assert.False(main.RevealStoreJobZipCommand.CanExecute(null));
        Assert.True(OpenSettings(main).ExportBackupCommand.CanExecute(null));
    });

    /// <summary>
    /// 移動と戻すも同じ所に持つ。始めるまでの窓は試験で出せないので、主画面の状態から開き直した画面を見る。
    /// 試験は環境変数で保存先を決めているので、「場所を変える」と「戻す」はいつも押せない（ここでは書き出しで見る）
    /// </summary>
    [Theory]
    [InlineData(StoreJobKind.Move, "引っ越しています… 3/10")]
    [InlineData(StoreJobKind.Restore, "バックアップから戻しています… 3/10")]
    public Task 移動と戻すの途中も_開き直した設定の画面に進み具合が出て_書き出しを押せない(StoreJobKind kind, string line)
        => TestApp.Run(async app =>
        {
            var main = await app.StartAsync();
            main.BeginStoreJob(kind, "始めています…");
            main.ReportStoreJob(line);

            var settings = OpenSettings(main);

            Assert.Equal(line, settings.Status);
            Assert.Equal(kind == StoreJobKind.Move, settings.IsMovingStore);
            Assert.Equal(kind == StoreJobKind.Restore, settings.IsBackingUp);
            Assert.False(settings.ExportBackupCommand.CanExecute(null));

            main.EndStoreJob(string.Empty);

            Assert.True(settings.ExportBackupCommand.CanExecute(null));
            Assert.False(main.HasStoreJobNotice);
        });

    /// <summary>保存先の中身（ログは試験の途中で書かれ得るので除く。実行中の錠はアプリが掴んでいて読めないので、在ることだけ見る）。</summary>
    private static Dictionary<string, string> Snapshot(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).StartsWith("logs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => path,
                path => Path.GetFileName(path) == "app.lock" ? "（錠）" : Convert.ToBase64String(File.ReadAllBytes(path)));
}
