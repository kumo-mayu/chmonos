using System.Security.AccessControl;
using System.Security.Principal;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// リンク（ジャンクション）とフォルダの数え方（外部の点検 2026-10-06）。
///
/// - 保存先の子を直に指すジャンクションを引越し先に選ぶと、文字の祖先しか比べず、内側への引越しを断らなかった
/// - 登録したフォルダの数えが、リンクの先（外・自分・祖先）まで降り、読めないと 0件として記録に書いていた
/// - 設定の保存容量の計測が、リンクの先まで数え、読めないと項目ごと 0 にしていた
///
/// 外には番兵のファイルを置き、数に入らないことを見る。
/// ジャンクションと権限の拒否は Windows の物なので、この一式は Windows でだけ組む。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class LinkCountingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-linkcount-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _links = [];
    private readonly List<(DirectoryInfo Folder, FileSystemAccessRule Rule)> _denied = [];

    private string Outside => Path.Combine(_root, "outside");

    public LinkCountingTests()
    {
        Directory.CreateDirectory(Outside);
        File.WriteAllBytes(Path.Combine(Outside, "sentinel.bin"), new byte[1000]);
    }

    public void Dispose()
    {
        // 拒否を戻さないと後片付けで消せない。リンクは先に外す（中身を辿って消さない）
        foreach (var (folder, rule) in _denied)
        {
            var security = folder.GetAccessControl();
            security.RemoveAccessRule(rule);
            folder.SetAccessControl(security);
        }

        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Link(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.True(TestJunction.TryCreate(link, target), "ジャンクションを作れませんでした");
        _links.Add(link);
        return link;
    }

    /// <summary>自分に「フォルダの一覧」を拒む（拒否は継いだ許可より先に効く）。</summary>
    private void Deny(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        _denied.Add((info, rule));
    }

    private string MakeFolder(string relative, params (string Name, int Size)[] files)
    {
        var folder = Path.Combine(_root, relative);
        Directory.CreateDirectory(folder);
        foreach (var (name, size) in files)
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
        }

        return folder;
    }

    // ---- 1. 子を直に指すジャンクション ----

    [Fact]
    public void 保存先の子を直に指すジャンクションは_保存先の内側と見る()
    {
        var store = MakeFolder("store", ("settings.json", 2), (Path.Combine("child", "a.json"), 2));
        var alias = Link(Path.Combine(_root, "alias"), Path.Combine(store, "child"));

        Assert.True(FolderIdentity.IsSameOrInside(alias, store));
        Assert.True(FolderIdentity.IsSameOrInside(Path.Combine(alias, "まだ無い", "下"), store));
        Assert.False(FolderIdentity.IsSameOrInside(Outside, store));
    }

    [Fact]
    public void 保存先の子を指すジャンクションへの引越しは断り_何も写さない()
    {
        var store = MakeFolder("store", ("settings.json", 2), (Path.Combine("items", "9900001.json"), 2));
        Directory.CreateDirectory(Path.Combine(store, "child"));
        var alias = Link(Path.Combine(_root, "alias"), Path.Combine(store, "child"));

        var result = StoreMover.Move(store, alias);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Copied);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(store, "child")));
        Assert.True(File.Exists(Path.Combine(store, "items", "9900001.json")));
    }

    // ---- 2. 登録したフォルダの数え ----

    [Fact]
    public async Task 登録したフォルダの数えは_外と自分と祖先を指すリンクの先を数えない()
    {
        var folder = MakeFolder("registered", ("a.psd", 100), (Path.Combine("sub", "b.unitypackage"), 50));
        Link(Path.Combine(folder, "外へ"), Outside);
        Link(Path.Combine(folder, "sub", "自分へ"), folder);
        Link(Path.Combine(folder, "祖先へ"), _root);

        // 辿る数え方だと、自分と祖先を指すリンクで輪になって終わらない（戻して確かめたとき、5分で終わらなかった）。
        // 試験の一式を止めないよう、打ち切りを付けて待つ
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var survey = await Task.Run(() => RegisteredFolderSet.Survey(folder, stop.Token)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(survey);
        Assert.Equal(2, survey.FileCount);
        Assert.Equal(150, survey.TotalBytes);
        Assert.Equal(["sub/b.unitypackage"], survey.UnityPackages);
    }

    /// <summary>
    /// 移した先の候補の数え直しは、登録の数え方と同じ数を出す（2026-10-07）。比べて同じフォルダかを見分けるので、
    /// 数え方が違うと移した先が候補に出ない。OneDrive の「必要なときにダウンロード」の物とリンクのファイルは、
    /// 試験で作れない（作るのに管理者の権限か OneDrive が要る）ので、ここではジャンクションを含む木で揃うことを見る
    /// </summary>
    [Fact]
    public async Task 移した先の候補の数え直しは_登録の数えと同じ数を出す()
    {
        var folder = MakeFolder("registered", ("a.psd", 100), (Path.Combine("sub", "b.unitypackage"), 50));
        Link(Path.Combine(folder, "外へ"), Outside);
        Link(Path.Combine(folder, "sub", "自分へ"), folder);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var survey = await Task.Run(() => RegisteredFolderSet.Survey(folder, stop.Token)).WaitAsync(TimeSpan.FromSeconds(30));
        var measured = await Task.Run(() => MovedFolderCandidates.MeasureTree(folder, stop.Token)).WaitAsync(TimeSpan.FromSeconds(30));

        var top = Assert.Single(measured, entry => string.Equals(Path.TrimEndingDirectorySeparator(entry.Path), folder, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(survey!.FileCount, top.FileCount);
        Assert.Equal(survey.TotalBytes, top.TotalBytes);
    }

    [Fact]
    public void 登録したフォルダの数えは取り消しで止まる()
    {
        var folder = MakeFolder("registered", ("a.psd", 1));
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => RegisteredFolderSet.Survey(folder, stop.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => RegisteredFolderSet.Measure(folder, stop.Token));
    }

    [Fact]
    public void 中を読めない登録したフォルダは_0件ではなく読めなかったとして返す()
    {
        var folder = MakeFolder("registered", ("a.psd", 1), (Path.Combine("denied", "b.psd"), 1));
        Deny(Path.Combine(folder, "denied"));

        Assert.Null(RegisteredFolderSet.Survey(folder));
        Assert.Null(RegisteredFolderSet.Measure(folder));
    }

    /// <summary>取り込みの数え直しで中を読めなかったら、前の値を残す（0件で上書きしない）。</summary>
    [Fact]
    public async Task 取り込みの数え直しで読めなければ_前の数を残す()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var folder = MakeFolder("registered", ("a.psd", 1), (Path.Combine("denied", "b.psd"), 1));
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-1",
            Local = new LocalBlock
            {
                LocalFolders = [new LocalFolderRecord { Path = folder, FileCount = 5, TotalBytes = 500, LastSeenAt = DateTimeOffset.Now }],
            },
        });
        Deny(Path.Combine(folder, "denied"));

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(store, client, new ImagePipeline(client, paths, settings), settings);
        await pipeline.RunAsync(new ImportWorkSet([folder]), null);

        var record = (await store.Items.LoadAsync("local-1"))!.Local.LocalFolders.Single();
        Assert.Equal(5, record.FileCount);
        Assert.Equal(500, record.TotalBytes);
        Assert.Equal(0, client.Calls);
    }

    // ---- 3. 設定の保存容量 ----

    private (SettingsService Service, AppPaths Paths) NewSettings()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        return (new SettingsService(new DataStore(paths)), paths);
    }

    [Fact]
    public async Task 保存容量はリンクの先を数えない()
    {
        var (service, paths) = NewSettings();
        Directory.CreateDirectory(Path.Combine(paths.ImagesDir, "123"));
        File.WriteAllBytes(Path.Combine(paths.ImagesDir, "123", "a.webp"), new byte[64]);
        Link(Path.Combine(paths.ImagesDir, "外へ"), Outside);
        Link(Path.Combine(paths.ItemsDir, "根へ"), paths.Root);

        var usage = await service.LoadUsageAsync();

        Assert.Equal(1, usage.ImageCount);
        Assert.Equal(64, usage.ImageBytes);
        Assert.Equal(0, usage.ItemCount);
        Assert.Equal(0, usage.ItemBytes);
    }

    [Fact]
    public async Task 保存容量の計測は取り消しで止まる()
    {
        var (service, paths) = NewSettings();
        File.WriteAllBytes(Path.Combine(paths.ImagesDir, "a.webp"), new byte[1]);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadUsageAsync(stop.Token));
        // 数えの途中でも見る（Task.Run は始める前に取り消しを見るだけで、走り出した後の列挙は止めない）
        Assert.ThrowsAny<OperationCanceledException>(() => SettingsService.Measure(paths.ImagesDir, stop.Token));
    }

    [Fact]
    public async Task 読めない項目は0ではなく分からないとして返し_ほかの項目は数える()
    {
        var (service, paths) = NewSettings();
        Directory.CreateDirectory(Path.Combine(paths.ImagesDir, "denied"));
        File.WriteAllBytes(Path.Combine(paths.ImagesDir, "denied", "a.webp"), new byte[1]);
        File.WriteAllBytes(Path.Combine(paths.ItemsDir, "9900001.json"), new byte[10]);
        Deny(Path.Combine(paths.ImagesDir, "denied"));

        var usage = await service.LoadUsageAsync();

        Assert.Null(usage.ImageCount);
        Assert.Null(usage.ImageBytes);
        Assert.Equal(1, usage.ItemCount);
        Assert.Equal(10, usage.ItemBytes);
    }
}
