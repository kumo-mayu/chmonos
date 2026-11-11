using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 動いている Chmonos の一覧（docs/spec/running-instances.md）。置き場はいつも一時フォルダで、本物の置き場には書かない。
/// 保存先のパスは作り物。
/// </summary>
public sealed class RunningInstancesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "chmonos-running-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>番号として使われない大きな数（Windows のプロセスの番号は4の倍数で、この大きさまで行かない）。</summary>
    private const int MissingProcessId = int.MaxValue - 3;

    private static RunningInstanceRecord Record(int processId, string store, int? port = null) => new()
    {
        ProcessId = processId,
        StartedAt = processId == Environment.ProcessId ? RunningInstances.CurrentProcessStartedAt() : new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(9)),
        StoreRoot = store,
        Port = port,
        ApiFile = Path.Combine(store, "api.json"),
        ExePath = @"C:\作り物\Chmonos.exe",
        Version = "9.9.9",
    };

    [Fact]
    public void 試験と道具では_環境変数が無ければ置き場が無く_書かない()
    {
        // アプリ本体として起動していない（AllowsUserStore が立たない）ので、本物の %LOCALAPPDATA% の置き場を返さない
        if (Environment.GetEnvironmentVariable(RunningInstances.DirectoryVariable) is not null)
        {
            return;
        }

        // 置き場は1回だけ読み、書く側には null を渡す（読んだ後に印が変わっても、本物の置き場へ書く窓を作らない）
        var directory = RunningInstances.Directory;
        Assert.Null(directory);
        Assert.Null(RunningInstanceRecorder.Start(null, Record(Environment.ProcessId, @"D:\作り物\保存先")));
    }

    [Fact]
    public void 札にはトークンを書かず_ポートが無いときも欄を省かない()
    {
        using var recorder = RunningInstanceRecorder.Start(_directory, Record(Environment.ProcessId, @"D:\作り物\保存先") with { Headless = true })!;

        var text = File.ReadAllText(RunningInstances.FileFor(_directory, Environment.ProcessId));
        Assert.Contains("\"port\": null", text);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"storeRoot\": \"D:\\\\作り物\\\\保存先\"", text);
        Assert.Contains("\"apiFile\": \"D:\\\\作り物\\\\保存先\\\\api.json\"", text);
        Assert.Contains("\"headless\": true", text);

        recorder.UpdatePort(() => 41234);
        var record = Assert.Single(RunningInstances.ReadAll(_directory));
        Assert.Equal(41234, record.Port);
        Assert.Equal(Environment.ProcessId, record.ProcessId);
        Assert.Equal("9.9.9", record.Version);
        Assert.Equal(@"C:\作り物\Chmonos.exe", record.ExePath);
        Assert.True(RunningInstances.IsAlive(record.ProcessId, record.StartedAt));
    }

    [Fact]
    public void 二本が別の保存先で同時に並び_閉じた方だけが消える()
    {
        // 2本目の番号は作り物（生きているとみなす）。同じ置き場に別のファイルで並ぶ
        const int other = 4_000_000;
        bool? Alive(int processId, DateTimeOffset? startedAt) => true;
        var first = RunningInstanceRecorder.Start(_directory, Record(Environment.ProcessId, @"D:\作り物\普段", 39847), Alive)!;
        var second = RunningInstanceRecorder.Start(_directory, Record(other, @"D:\作り物\写し"), Alive)!;

        var all = RunningInstances.ReadAll(_directory).OrderBy(record => record.StoreRoot, StringComparer.Ordinal).ToList();
        Assert.Equal([@"D:\作り物\写し", @"D:\作り物\普段"], all.Select(record => record.StoreRoot));
        Assert.Equal([null, 39847], all.Select(record => record.Port));

        first.Dispose();
        Assert.False(File.Exists(RunningInstances.FileFor(_directory, Environment.ProcessId)));
        Assert.Equal(@"D:\作り物\写し", Assert.Single(RunningInstances.ReadAll(_directory)).StoreRoot);

        // 閉じた後は書き直さない（止まる知らせが後から来ても札を戻さない）
        first.UpdatePort(() => 40000);
        Assert.False(File.Exists(RunningInstances.FileFor(_directory, Environment.ProcessId)));

        second.Dispose();
        Assert.Empty(RunningInstances.ReadAll(_directory));
    }

    [Fact]
    public void 落ちて残った札を_次の起動が片付ける()
    {
        RunningInstances.Write(_directory, Record(MissingProcessId, @"D:\作り物\落ちた"));

        // 番号が別のプログラムに使い回された：このプロセスの番号で、始まりの時刻が違う札（自分の番号は片付けの対象外なので、判定を直に見る）
        Assert.False(RunningInstances.IsAlive(Environment.ProcessId, RunningInstances.CurrentProcessStartedAt().AddHours(-1)));
        Assert.True(RunningInstances.IsAlive(Environment.ProcessId, RunningInstances.CurrentProcessStartedAt()));
        Assert.False(RunningInstances.IsAlive(MissingProcessId, null));

        // 壊れた札（プロセスが無い）・確かめられない札・ほかのファイル
        File.WriteAllText(RunningInstances.FileFor(_directory, 4_000_004), "{ 壊れている");
        RunningInstances.Write(_directory, Record(4_000_008, @"D:\作り物\分からない"));
        RunningInstances.Write(_directory, Record(4_000_012, @"D:\作り物\別のプログラム"));
        File.WriteAllText(Path.Combine(_directory, "readme.json"), "{}");

        bool? Judge(int processId, DateTimeOffset? startedAt) => processId switch
        {
            4_000_008 => null,
            4_000_012 => false,
            _ => RunningInstances.IsAlive(processId, startedAt),
        };

        using var recorder = RunningInstanceRecorder.Start(_directory, Record(Environment.ProcessId, @"D:\作り物\今"), Judge)!;

        Assert.False(File.Exists(RunningInstances.FileFor(_directory, MissingProcessId)));
        Assert.False(File.Exists(RunningInstances.FileFor(_directory, 4_000_004)));
        Assert.False(File.Exists(RunningInstances.FileFor(_directory, 4_000_012)));
        Assert.True(File.Exists(RunningInstances.FileFor(_directory, 4_000_008)));
        Assert.True(File.Exists(Path.Combine(_directory, "readme.json")));
        Assert.Equal([@"D:\作り物\今", @"D:\作り物\分からない"], RunningInstances.ReadAll(_directory).Select(record => record.StoreRoot).Order(StringComparer.Ordinal));
    }
}
