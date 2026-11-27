using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 起動時に保存先の形式の版を確かめ、古ければ控えを取ってから印を上げる（<c>App.SettleStoreFormat</c>・<c>CheckStoreFormat</c>。docs/spec/data-format.md）。
/// 2026-11-28 の点検で、控えを消しても・新しい版の保存先を開いても落ちる試験が無かった。
/// 確かめる保存先は試験の保存先とは別の空のフォルダ（起動の一式は組まない）。窓は <see cref="TestApp"/> が受け、答えは既定の「いいえ」——
/// 「はい」はブラウザ・エクスプローラを本当に開くので通さない
/// </summary>
public class StoreFormatStartupTests
{
    private static string NewStore(TestApp app)
    {
        var root = Path.Combine(app.Root, "format-startup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "items"));
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(root, "items", "1000001.json"), "{ \"id\": \"1000001\" }");
        return root;
    }

    private static void WriteMarker(string root, int version)
        => File.WriteAllText(Path.Combine(root, StoreFormat.MarkerFileName), $$"""{ "formatVersion": {{version}} }""");

    private static string MarkerText(string root) => File.ReadAllText(Path.Combine(root, StoreFormat.MarkerFileName));

    [Fact]
    public Task 古い版の保存先は_控えを取ってから印を今の版に上げて開く() => TestApp.Run(app =>
    {
        var root = NewStore(app);
        WriteMarker(root, StoreFormat.OldestReadable);

        Assert.True(App.SettleStoreFormat(root));

        // 控えのフォルダ名はその PC の今の時刻で付くので、名前は見込まずに1つだけある事を見る
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.StartsWith($"v{StoreFormat.OldestReadable}-", Path.GetFileName(backup), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(backup, "items", "1000001.json")));
        Assert.True(File.Exists(Path.Combine(backup, "settings.json")));
        Assert.Equal(StoreFormat.Current, StoreFormat.ReadMarker(root));
        Assert.Empty(app.Notices);
        return Task.CompletedTask;
    });

    /// <summary>印の無い保存先（v1.0.0 が使った）は版 1 として、控えを取ってから印を書く</summary>
    [Fact]
    public Task 印の無い保存先は_版1として控えを取って印を書く() => TestApp.Run(app =>
    {
        var root = NewStore(app);

        Assert.True(App.SettleStoreFormat(root));

        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.StartsWith($"v{StoreFormat.Unversioned}-", Path.GetFileName(backup), StringComparison.Ordinal);
        Assert.Equal(StoreFormat.Current, StoreFormat.ReadMarker(root));
        return Task.CompletedTask;
    });

    [Fact]
    public Task 同じ版の保存先は_控えを取らずに開く() => TestApp.Run(app =>
    {
        var root = NewStore(app);
        WriteMarker(root, StoreFormat.Current);

        Assert.True(App.SettleStoreFormat(root));

        Assert.False(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.Equal(StoreFormat.Current, StoreFormat.ReadMarker(root));
        Assert.Empty(app.Notices);
        return Task.CompletedTask;
    });

    /// <summary>
    /// 控えが取れなければ開かず、印も上げない。印を控えより先に書くと、ここで印だけ上がって古いバージョンへ戻せなくなる。
    /// 控えの置き場と同じ名前のファイルを置いて、控えのフォルダを作れなくする
    /// </summary>
    [Fact]
    public Task 控えが取れなければ_印を上げずに開かない() => TestApp.Run(app =>
    {
        app.AllowLoggedFailures = true;
        var root = NewStore(app);
        WriteMarker(root, StoreFormat.OldestReadable);
        var before = MarkerText(root);
        File.WriteAllText(Path.Combine(root, StoreFormat.BackupsDirName), "フォルダを作れなくする");

        Assert.False(App.SettleStoreFormat(root));

        Assert.Equal(before, MarkerText(root));
        var notice = Assert.Single(app.Notices);
        Assert.StartsWith("データの控えを作れなかったため、開けませんでした。", notice.Text, StringComparison.Ordinal);
        return Task.CompletedTask;
    });

    [Fact]
    public Task 新しい版の保存先は_開かず何も書かない() => TestApp.Run(app =>
    {
        var root = NewStore(app);
        WriteMarker(root, StoreFormat.Current + 1);
        var before = MarkerText(root);

        Assert.False(App.CheckStoreFormat(root));
        Assert.False(App.SettleStoreFormat(root));

        Assert.Equal(before, MarkerText(root));
        Assert.False(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.Equal(2, app.Notices.Count);
        Assert.All(app.Notices, notice =>
        {
            Assert.Equal("新しいバージョンのデータです", notice.Caption);
            Assert.Equal(MessageBoxButton.YesNo, notice.Button);
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task 古すぎる版の保存先は_開かず何も書かない() => TestApp.Run(app =>
    {
        var root = NewStore(app);
        WriteMarker(root, StoreFormat.OldestReadable - 1);
        var before = MarkerText(root);

        Assert.False(App.SettleStoreFormat(root));

        Assert.Equal(before, MarkerText(root));
        Assert.False(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.Equal("このデータは古い形式のため、このバージョンでは開けません。", Assert.Single(app.Notices).Text);
        return Task.CompletedTask;
    });

    /// <summary>読めない印を今の版や版 1 とみなすと、新しいバージョンの保存先を止められない（点検26）。開かず、印も消さない</summary>
    [Fact]
    public Task 読めない印の保存先は_開かず印も残す() => TestApp.Run(app =>
    {
        app.AllowLoggedFailures = true;
        var root = NewStore(app);
        File.WriteAllText(Path.Combine(root, StoreFormat.MarkerFileName), "壊れた印");

        // 起動はまず組み立ての前に確かめるだけの方を通る。後の SettleStoreFormat も読み直しで止まるが、
        // 確かめで通すと組み立てが先に走るので、確かめの方だけでも断る事を見る
        Assert.False(App.CheckStoreFormat(root));
        Assert.False(App.SettleStoreFormat(root));

        Assert.Equal("壊れた印", MarkerText(root));
        Assert.False(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));
        Assert.Equal(2, app.Notices.Count);
        Assert.All(app.Notices, notice => Assert.StartsWith($"保存先の {StoreFormat.MarkerFileName} が壊れていて", notice.Text, StringComparison.Ordinal));
        return Task.CompletedTask;
    });
}
