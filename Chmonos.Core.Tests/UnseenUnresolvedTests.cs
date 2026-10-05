using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定に出ていた物を、今回の取り込みで「見ていない」なら片付いたとして落とさない（2026-10-05・見つからない・移動の点検 4）。
///
/// 見ていないのは、オンラインのみになった・中を読めなかったフォルダの下・ハッシュを取れなかった物。
/// 前は取り込み元を走査したというだけで「見つからなかった＝片付いた」と落とし、控えに載っているので監視も拾い直さず、
/// どこにも出なくなっていた（外付けを外していた物を残すのと同じ考え）。
/// </summary>
public sealed class UnseenUnresolvedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-unseen-unresolved-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public UnseenUnresolvedTests()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        var settings = new AppSettings { SaveImages = false };
        var client = new OffUiThreadTests.OfflineClient();
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, paths, settings), settings);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>手掛かりの無いファイルを置いて取り込み、未確定に出す。</summary>
    private async Task<string> UnresolvedFileAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "loose.png");
        File.WriteAllText(path, "作り物の画像");
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        Assert.Contains(_store.Unresolved.Load(), file => file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase));
        return path;
    }

    private void AssertStillUnresolved(string path)
        => Assert.Contains(_store.Unresolved.Load(), file => file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task オンラインのみになった未確定は取り込み直しても残る()
    {
        var path = await UnresolvedFileAsync(_source);

        // 手元で作れるオンラインのみの印は Offline だけ（ImportWriteBackTests と同じ）
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
        try
        {
            var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

            Assert.Equal(1, summary.FilesOnlineOnly);
            AssertStillUnresolved(path);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task ハッシュを取れなかった未確定は取り込み直しても残る()
    {
        var path = await UnresolvedFileAsync(_source);

        // 控えと合うと読まずに済むので、日時を変えて読み直させ、ほかのアプリが開いている状態にする
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        ImportSummary summary;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));
        }

        Assert.Equal(1, summary.FilesUnreadable);
        AssertStillUnresolved(path);
    }

    [Fact]
    public async Task 読めなくなったフォルダの下の未確定は取り込み直しても残る()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sub = new DirectoryInfo(Path.Combine(_source, "sub"));
        var path = await UnresolvedFileAsync(sub.FullName);

        var security = sub.GetAccessControl();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(rule);
        sub.SetAccessControl(security);
        try
        {
            var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

            Assert.Equal(1, summary.FoldersUnreadable);
            AssertStillUnresolved(path);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            sub.SetAccessControl(security);
        }
    }

    /// <summary>見た上で無くなった物は、今までどおり片付いたとして落とす（残すのは見ていない物だけ）。</summary>
    [Fact]
    public async Task 消した未確定は取り込み直すと落ちる()
    {
        var path = await UnresolvedFileAsync(_source);
        File.Delete(path);

        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.DoesNotContain(_store.Unresolved.Load(), file => file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase));
    }
}
