using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 長い作業の帯は1本しか持てない（ユーザ判断 2026-10-01）。
///
/// 前は検出の最中に書き出しや候補の検索を始められ、後から始めた方が帯と「中止」の宛先を上書きし、
/// 先に終わった方が帯ごと消していた。走っている間はほかの長い作業を始める口をすべて押せなくし、押せない理由を出す。
/// バックアップから戻すにも帯を出すが、途中で止めると戻す先が半端に残るので「中止」は出さない
/// </summary>
public class LongJobTests
{
    private const string DetectingNote = "対応アバターを検出しています。終わるか、下の帯で中止してからお試しください。";
    private const string ExportingNote = "バックアップを書き出しています。終わるか、下の帯で中止してからお試しください。";

    private static IDisposable StartDetecting(MainViewModel main, CancellationTokenSource stop)
        => main.BeginLongJob("対応アバターを検出しています", "この間、アバターの編集と取り込みの検出は待たされます", stop)
            ?? throw new InvalidOperationException("始められなかった");

    [Fact]
    public Task 走っている間はほかの長い作業を始めず_後から来た物が先の帯を消さない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var job = StartDetecting(main, first);

        Assert.Null(main.BeginLongJob("候補を検索しています", "この間、BOOTHへの他の問い合わせは順番待ちになります", second));
        Assert.Equal("この間、アバターの編集と取り込みの検出は待たされます", main.LongJobNote);
        Assert.Equal(DetectingNote, main.LongJobBlockedNote);

        // 帯の「中止」は先に始めた作業へ届く
        main.StopLongJobCommand.Execute(null);
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);

        job.Dispose();
        Assert.False(main.IsLongJobRunning);
        Assert.Equal(string.Empty, main.LongJobBlockedNote);

        // 終えた物をもう一度畳んでも、後から始めた作業の帯は消えない
        var next = main.BeginLongJob("候補を検索しています", "この間、BOOTHへの他の問い合わせは順番待ちになります", second);
        Assert.NotNull(next);
        job.Dispose();
        Assert.True(main.IsLongJobRunning);
        Assert.Equal("この間、BOOTHへの他の問い合わせは順番待ちになります", main.LongJobNote);
        next.Dispose();
    });

    [Fact]
    public Task ほかの長い作業の間は_設定の場所を変える_書き出し_戻すが押せず_理由の1行が出る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        using var stop = new CancellationTokenSource();
        var job = StartDetecting(main, stop);

        Assert.False(settings.ExportBackupCommand.CanExecute(null));
        Assert.False(settings.RestoreBackupCommand.CanExecute(null));
        Assert.False(settings.CanChangeRoot);
        Assert.Equal(DetectingNote, settings.RootLockedNote);
        Assert.Contains(nameof(SettingsViewModel.RootLockedNote), changed);

        // 窓を出している間に始まった、の形：押せても始めず、帯は検出のまま
        var zip = Path.Combine(app.Root, "backup.zip");
        await settings.ExportBackupToAsync(zip, withImages: false);

        Assert.Equal(StoreJobKind.None, main.StoreJob);
        Assert.Equal("この間、アバターの編集と取り込みの検出は待たされます", main.LongJobNote);
        Assert.Equal(DetectingNote, settings.Status);
        Assert.False(File.Exists(zip));

        job.Dispose();

        Assert.True(settings.ExportBackupCommand.CanExecute(null));
        Assert.NotEqual(DetectingNote, settings.RootLockedNote);
    });

    [Fact]
    public Task 書き出しの間は_対応アバターの検出と自動検索が押せず_吹き出しが理由に替わる() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        var zip = Path.Combine(app.Root, "backup.zip");

        main.ShowAvatars();
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        Assert.True(avatars.DetectCommand.CanExecute(null));
        Assert.Equal("説明文などから読み取ります。カテゴリ不明の商品はBOOTHに問い合わせます。", avatars.DetectHint);

        Task export;
        using (await StoreWriteGate.HoldAsync())
        {
            export = settings.ExportBackupToAsync(zip, withImages: false);

            Assert.False(avatars.DetectCommand.CanExecute(null));
            Assert.Equal(ExportingNote, avatars.DetectHint);

            main.ShowResolveCommand.Execute(null);
            var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
            Assert.Equal(ExportingNote, resolve.ProposeHint);
            Assert.False(resolve.ProposeCommand.CanExecute(null));
        }

        await export;

        var reopened = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.Equal("このファイル名でBOOTHを検索し、下の「候補」に表示します。", reopened.ProposeHint);
    });

    /// <summary>
    /// 戻すの帯。窓で聞く所は試験で出せないので、聞き終えた後の本体を呼ぶ。
    /// **成功させない**（戻せると保存先の場所を書き換えて開き直す）。無い zip を渡し、門を持っている間に帯を見てから失敗させる
    /// </summary>
    [Fact]
    public Task 戻している間は帯を出し_中止は出さず_ほかの口の理由は開き直すことを言う() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        var destination = Path.Combine(app.Root, "restore-to");

        Task restore;
        using (await StoreWriteGate.HoldAsync())
        {
            restore = settings.RestoreBackupToAsync(Path.Combine(app.Root, "missing.zip"), destination);

            Assert.True(main.IsLongJobRunning);
            Assert.False(main.CanStopLongJob);
            Assert.False(main.StopLongJobCommand.CanExecute(null));
            Assert.Equal("戻し終えるまで、保存は待たされます。終わったら開き直します。", main.LongJobNote);
            Assert.Equal("バックアップから戻しています。終わったら開き直します。", main.LongJobBlockedNote);
            Assert.Equal(StoreJobKind.Restore, main.StoreJob);
            Assert.False(settings.ExportBackupCommand.CanExecute(null));
        }

        await restore;

        Assert.False(main.IsLongJobRunning);
        Assert.Equal(StoreJobKind.None, main.StoreJob);
        Assert.StartsWith("バックアップから戻せませんでした。", settings.Status);
        Assert.False(settings.HasExportedZip);
    });
}
