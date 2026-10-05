using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// Unity へ送る道のうち、検索の複数選択・改変の画面・「Unityで選択」も、送れなかった zip の「無い」を記録に残す
/// （商品ページの1件の送り方と同じ道。file-lifecycle.md「気になった所」17）。
/// </summary>
public class UnityBatchSendPresenceTests
{
    private const string ItemId = "9900001";

    private static UnityPackageEntry PackageOf(LocalFileRecord file)
        => new(file.Paths[0], "Body.unitypackage", 0) { ZipHash = file.Hash };

    [Fact]
    public Task 送れなかった包みのzipが無ければ_記録に日時が付き_検索へ知らせる() => TestApp.Run(async app =>
    {
        var gone = Make.File(Path.Combine(app.Root, "files", "deleted-by-hand.zip"));
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装").WithFiles(gone));
        await app.StartAsync();
        var package = PackageOf(gone);
        ItemRecord? told = null;

        await FilePresenceNotes.NoteFailedSendsAsync(
            app.Services, [(ItemId, package)], [new UnityQueueOutcome(package, false, "zipが見つかりません")], reloaded => told = reloaded);

        var saved = (await app.Services.Store.Items.LoadAsync(ItemId))!;
        Assert.NotNull(saved.Local.LocalFiles.Single().MissingSince);
        Assert.NotNull(told);
        Assert.True(told!.HasMissingFile);
    });

    [Fact]
    public Task 人が止めた分と_zipが在る分は_記録を書かない() => TestApp.Run(async app =>
    {
        var gone = Make.File(Path.Combine(app.Root, "files", "deleted-by-hand.zip"));
        var present = Make.File(app.NewFile("present.zip"));
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装").WithFiles(gone, present));
        await app.StartAsync();
        var stopped = PackageOf(gone);
        var failedButPresent = PackageOf(present);

        await FilePresenceNotes.NoteFailedSendsAsync(
            app.Services,
            [(ItemId, stopped), (ItemId, failedButPresent)],
            [new UnityQueueOutcome(stopped, false, UnityImportQueue.StoppedMessage), new UnityQueueOutcome(failedButPresent, false, "Unityが応答しません")],
            reloaded => Assert.Fail("書かないはず"));

        Assert.All((await app.Services.Store.Items.LoadAsync(ItemId))!.Local.LocalFiles, file => Assert.Null(file.MissingSince));
    });
}
