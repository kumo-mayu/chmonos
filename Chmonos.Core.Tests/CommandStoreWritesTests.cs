using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 画面が直に書いていた小さな書き込みを <see cref="UiCommand"/> に通した分
/// （取り込みの続きを捨てる・要確認の参照切れを探す・ドライブ文字の組を控える）。
/// 直に書くと、保存先を運んでいる間の門（StoreWriteGate）を通らない。
/// </summary>
public sealed class CommandStoreWritesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-cmdwrites-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public CommandStoreWritesTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
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

    private sealed class FakeReader(params MountedVolume[] volumes) : IVolumeReader
    {
        public IReadOnlyList<MountedVolume> Mounted() => volumes;
    }

    // 取り込みと商品の処理は、ここで試す命令では使わない
    private CommandHandler Create(VolumeTable? volumes = null, INotificationService? notifications = null)
        => new(null!, null!, notifications: notifications, volumes: volumes, importState: _store.ImportState);

    [Fact]
    public async Task 取り込みの続きを捨てると記録が空になる()
    {
        await _store.ImportState.SaveAsync(new ImportState { Done = 3, Total = 10, Targets = [@"D:\BOOTH"] });

        var result = await Create().ExecuteAsync(new UiCommand.DiscardInterruptedImport());

        Assert.IsType<CommandResult.Done>(result);
        var state = _store.ImportState.Load();
        Assert.False(state.HasProgress);
        Assert.Empty(state.Targets);
    }

    [Fact]
    public async Task ドライブ文字の組を確かめると読み替えを返す()
    {
        await _store.Volumes.SaveAsync([new VolumeRecord { Letter = "E:", Serial = "AAAA0001" }]);
        var volumes = new VolumeTable(_store, new FakeReader(new MountedVolume("F:", "AAAA0001", null)));

        var result = await Create(volumes).ExecuteAsync(new UiCommand.ObserveVolumes([@"E:\BOOTH\a.zip"]));

        var observed = Assert.IsType<CommandResult.VolumesObserved>(result);
        Assert.Equal("F:", observed.Remap["E:"]);
    }

    [Fact]
    public async Task 参照切れを探すと見つけた件数を返す()
    {
        var result = await Create(notifications: new NotificationService(_store))
            .ExecuteAsync(new UiCommand.DetectOrphanReferences());

        // 商品が無いので0件。数として返ってくること（画面は件数で「n件見つけた」を出す）
        Assert.Equal(0, Assert.IsType<CommandResult.Counted>(result).Count);
    }

    /// <summary>組み立てで渡し忘れたら、画面に意味の取れない文を出さずに落とす（E5）。</summary>
    [Fact]
    public async Task 渡し忘れは不具合として落とす()
    {
        var handler = new CommandHandler(null!, null!);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new UiCommand.DiscardInterruptedImport()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new UiCommand.ObserveVolumes([])));
    }
}
