using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「保存先を変える（引越し・置き換え）」と「バックアップを書き出す」で、データを失わないための守り（外部の点検 2026-10-06）。
///
/// 引越しは運ぶ前に多重起動の錠を放していたので、その間に2つ目のアプリが元の保存先を開けて書け、
/// その入力は最後に元を消すときに消えた。今は放さない（成功すると開き直すので、試験は失敗する道で見る）。
/// 運ぶ命令を途中で止めておくのに、書き込みの門（<see cref="StoreWriteGate"/>）を試験が先に持つ
/// </summary>
public class SettingsStoreSafetyTests
{
    private static SettingsViewModel OpenSettings(MainViewModel main)
    {
        main.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
    }

    /// <summary>取れたら（＝錠が放されていたら）すぐ放して false を返す。</summary>
    private static bool AnotherAppCanOpen(AppPaths paths)
    {
        using var second = SingleInstanceLock.TryAcquire(paths);
        return second is not null;
    }

    [Fact]
    public Task 運んでいる間も失敗した後も_2つ目のアプリは元の保存先を開けない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        var source = app.Services.Paths.Root;

        Assert.False(AnotherAppCanOpen(app.Services.Paths));

        Task relocate;
        using (await StoreWriteGate.HoldAsync())
        {
            // 運ぶ先は元の中なので、門が開くと Core が断る（失敗の道。開き直さない）
            relocate = settings.RelocateAsync(source, Path.Combine(source, "inside"), replace: false);
            await UiThread.Until(() => main.StoreJob == StoreJobKind.Move, "運ぶ命令が門の前で待つ");

            Assert.False(AnotherAppCanOpen(app.Services.Paths));
        }

        await relocate;

        Assert.Contains(app.Notices, notice => notice.Caption == "引越しに失敗しました");
        Assert.False(AnotherAppCanOpen(app.Services.Paths));
    });

    /// <summary>
    /// 写し始めた後に読めなくなったら、書き出しは失敗にして前の zip を残し、そう言う。
    /// 前は「開けなかったファイルは入れていません」と言って、作りかけの zip で前の zip を上書きしていた
    /// </summary>
    [Fact]
    public Task 書き出しの途中で読めなくなると_前のzipを残したと言い_zipは前のまま() => TestApp.Run(async app =>
    {
        // 読めなくなった失敗はログに残す（後から追えるように）。ここではそれが期待どおり
        app.AllowLoggedFailures = true;
        var main = await app.StartAsync();
        var settings = OpenSettings(main);
        await app.SettleAsync();

        var picture = Path.Combine(app.Services.Paths.Root, "images", "9001", "a.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
        await File.WriteAllBytesAsync(picture, new byte[256]);

        var outDir = Path.Combine(app.Root, "backup-out");
        Directory.CreateDirectory(outDir);
        var zip = Path.Combine(outDir, "backup.zip");
        await settings.ExportBackupToAsync(zip, withImages: true);
        var previous = await File.ReadAllBytesAsync(zip);

        using (var holder = new FileStream(picture, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            // 開けるが読むと落ちる（範囲の錠）。開く前の失敗（飛ばしてよい）とは別の、写し始めた後の失敗
            holder.Lock(0, holder.Length);
            await settings.ExportBackupToAsync(zip, withImages: true);
            holder.Unlock(0, holder.Length);
        }

        Assert.Equal(
            $"バックアップを書き出せませんでした。「{Path.Combine("images", "9001", "a.webp")}」が途中で読めなくなりました。前のzipはそのまま残っています。",
            settings.DataStatus);
        Assert.False(settings.HasExportedZip);
        Assert.Equal(previous, await File.ReadAllBytesAsync(zip));
        Assert.False(File.Exists(zip + ".tmp"));
    });
}
