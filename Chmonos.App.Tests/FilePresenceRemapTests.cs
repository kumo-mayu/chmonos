using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// ドライブ文字が変わった商品を、商品ページもフォルダビュー・検索と同じ読み替えで見る（file-lifecycle.md「気になった所」2）。
/// 前は商品ページだけ記録のパスのまま見て、「取り外しているドライブ」と出て開けなかった。
/// </summary>
public class FilePresenceRemapTests
{
    [Fact]
    public Task 見るのは読み替えた後の場所で_結果の場所は記録のまま() => TestApp.Run(app =>
    {
        var real = app.NewFile("moved.zip");
        var recorded = @"Q:\old\moved.zip";
        var file = Make.File(recorded);

        var asRecorded = FilePresenceNotes.Look([file], path => path);
        var remapped = FilePresenceNotes.Look([file], path => path == recorded ? real : path);

        Assert.Equal(FilePresence.OnDetachedDrive, asRecorded.Single().Presence);
        Assert.Equal(FilePresence.Present, remapped.Single().Presence);
        Assert.Equal([recorded], remapped.Single().Paths);
        return Task.CompletedTask;
    });

    [Fact]
    public Task 商品ページの行は_ドライブ文字が変わっていれば今の場所で在ると見て開ける() => TestApp.Run(async app =>
    {
        // 試験の置き場のドライブを、別の文字（Q:）で記録したことにする
        var real = app.NewFile("moved.zip");
        var drive = Path.GetPathRoot(real)![..2];
        var mounted = new VolumeReader().Mounted().SingleOrDefault(volume => string.Equals(volume.Letter, drive, StringComparison.OrdinalIgnoreCase));
        if (mounted is null || !VolumeTable.IsDistinctive(mounted.Serial)
            || new VolumeReader().Mounted().Any(volume => volume.Letter.Equals("Q:", StringComparison.OrdinalIgnoreCase)))
        {
            return; // この PC では読み替えを作れない（通し番号が無い・Q: が使われている）
        }

        await app.Services.Store.Volumes.SaveAsync([new VolumeRecord { Letter = "Q:", Serial = mounted.Serial }]);
        var recorded = "Q:" + real[2..];
        var item = Make.Item("9900001", "作り物の衣装").WithFiles(Make.File(recorded));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        app.Services.Volumes.RefreshRemap();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await app.SettleAsync();

        var row = page.LocalFiles.Single();
        Assert.Equal([real], row.Paths);
        Assert.True(row.CanReveal);
        Assert.False(row.IsOnDetachedDrive);
        // 記録は書き換えない
        Assert.Equal([recorded], (await app.Services.Store.Items.LoadAsync("9900001"))!.Local.LocalFiles.Single().Paths);
    });

    /// <summary>
    /// 記録が場所のディスクの通し番号を持てば、文字の控え（volumes.json）に無くても、そのディスクが今見えている文字で見る
    /// （2026-10-05・見つからない・移動の点検の3）。控えは1つの文字に1台しか覚えないので、2台の外付けが同じ文字を使うと控えでは追えない。
    /// </summary>
    [Fact]
    public Task 商品ページの行は_記録が持つディスクの番号で今の場所を見る() => TestApp.Run(async app =>
    {
        var real = app.NewFile("moved.zip");
        var drive = Path.GetPathRoot(real)![..2];
        var mounted = new VolumeReader().Mounted().SingleOrDefault(volume => string.Equals(volume.Letter, drive, StringComparison.OrdinalIgnoreCase));
        if (mounted is null || !VolumeTable.IsDistinctive(mounted.Serial)
            || new VolumeReader().Mounted().Any(volume => volume.Letter.Equals("Q:", StringComparison.OrdinalIgnoreCase)))
        {
            return; // この PC では読み替えを作れない（通し番号が無い・Q: が使われている）
        }

        // 文字の控えは書かない。記録の場所にだけ、今その試験の置き場があるディスクの番号を持たせる
        var recorded = "Q:" + real[2..];
        var file = Make.File(recorded) with { Volumes = new Dictionary<string, string> { [recorded] = mounted.Serial } };
        var item = Make.Item("9900002", "作り物の衣装").WithFiles(file);
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        app.Services.Volumes.RefreshRemap();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await app.SettleAsync();

        var row = page.LocalFiles.Single();
        Assert.Equal([real], row.Paths);
        Assert.True(row.CanReveal);
        Assert.False(row.IsOnDetachedDrive);
    });
}
