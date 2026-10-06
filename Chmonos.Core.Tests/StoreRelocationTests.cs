using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 引越し・戻すの最後の「location.json を書き換えて開き直す」（2026-10-06）。
/// 置き場が本番（%LOCALAPPDATA%\Chmonos）に固定で、本番の指す先を変えてしまうので通せていなかった。
/// 既定の場所を <see cref="AppPaths.DefaultRootVariable"/> で一時フォルダへ向けて通す。
/// 「開き直す」は、アプリが起動の最初に呼ぶ <see cref="StoreLocation.Resolve"/> を呼び直して見る（プロセスを立て直すのは試験からはしない）。
///
/// 運ぶ命令は書き込みの門を持つので門の試験と並べない。環境変数はプロセスに1つなので、並べない束の中で入れて外す。
/// </summary>
[Collection(nameof(StoreWriteGateCollection))]
public sealed class StoreRelocationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bam-relocate-{Guid.NewGuid():N}");
    private readonly string? _homeBefore = Environment.GetEnvironmentVariable(AppPaths.RootVariable);
    private readonly string? _defaultBefore = Environment.GetEnvironmentVariable(AppPaths.DefaultRootVariable);
    private readonly bool _allowedBefore = StoreLocation.AllowsUserStore;
    private readonly List<string> _others = [];

    public StoreRelocationTests()
    {
        // CHMONOS_HOME が残っていると location.json を読まずにそこを返すので、外す。印も立てない（アプリ本体ではない）
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, null);
        Environment.SetEnvironmentVariable(AppPaths.DefaultRootVariable, DefaultHome);
        StoreLocation.AllowsUserStore = false;
    }

    private string DefaultHome => Path.Combine(_directory, "default");

    private string OldRoot => Path.Combine(_directory, "old", "Chmonos");

    public void Dispose()
    {
        StoreWriteGate.ReopenAfterRestartForTests();
        AppLog.Use(null);
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, _homeBefore);
        Environment.SetEnvironmentVariable(AppPaths.DefaultRootVariable, _defaultBefore);
        StoreLocation.AllowsUserStore = _allowedBefore;

        foreach (var folder in _others.Prepend(_directory))
        {
            TryDelete(folder);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            // 書けない場面を作るために読み取り専用にした物も消す
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void 既定の場所を差し替えると_location_jsonもそこに置き_印なしで開ける()
    {
        Assert.Equal(Path.Combine(DefaultHome, "location.json"), StoreLocation.LocationFile);
        Assert.Equal(new StoreRoot(DefaultHome, StoreRootSource.Default), StoreLocation.Resolve());

        StoreLocation.Save(OldRoot);

        Assert.Equal(new StoreRoot(OldRoot, StoreRootSource.Configured), StoreLocation.Resolve());
    }

    /// <summary>
    /// 本番の location.json を書いてよいのは、既定の場所を差し替えたときか、アプリ本体が CHMONOS_HOME なしで動いているときだけ。
    /// 引越し・戻すの命令は成功すると Core の中で location.json を書くので、試験や CHMONOS_HOME で開いた写しが命令を通すと本番の指す先が変わる。
    /// 決まりだけを見る（決まりを外して確かめるとき、Save を通すと本番を書いてしまう）
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false)] // 試験・道具で、差し替えていない
    [InlineData(true, false, true, false)]   // アプリ本体でも CHMONOS_HOME の写し（ui-check）
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, true)]   // 普段のアプリ本体
    [InlineData(false, true, false, true)]   // 差し替えた試験
    [InlineData(false, true, true, true)]
    public void 本番の_location_jsonを書けるのはアプリ本体が環境変数なしで動くときと_差し替えたときだけ(
        bool allowsUserStore, bool overridden, bool fromEnvironment, bool expected)
    {
        Assert.Equal(expected, StoreLocation.MayWriteLocationFile(allowsUserStore, overridden, fromEnvironment));
    }

    [Fact]
    public void 差し替えずに書こうとすると止まる()
    {
        // 差し替えを外す前に、本番の場所を書く道が開いていないこと（決まり）を確かめてから呼ぶ。外れていたら本番を書くので、呼ばずに落とす
        Environment.SetEnvironmentVariable(AppPaths.DefaultRootVariable, null);
        Assert.False(StoreLocation.MayWriteLocationFile(StoreLocation.AllowsUserStore, defaultRootOverridden: false, rootFromEnvironment: false));

        Assert.Throws<InvalidOperationException>(() => StoreLocation.Save(OldRoot));
        Assert.Throws<InvalidOperationException>(StoreLocation.Clear);
    }

    public static TheoryData<string> Destinations => new()
    {
        // 同じ一時フォルダの中
        "same",

        // 一時フォルダの別の階層（別のドライブの代わり。運ぶ元の根元とは親を共有しない）
        "other",
    };

    [Theory]
    [MemberData(nameof(Destinations))]
    public async Task 引越すと_location_jsonが運んだ先を指し_開き直すと新しい場所が開き_古い場所は開かない(string where)
    {
        MakeStore(OldRoot);
        StoreLocation.Save(OldRoot);
        var destination = DestinationFor(where);

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.MoveStore(OldRoot, destination, Replace: false));

        var moved = Assert.IsType<CommandResult.StoreMoved>(result).Result;
        Assert.True(moved.Succeeded, moved.Error);
        Assert.True(moved.SourceRemoved);
        Assert.Equal(destination, ReadLocation());

        // 開き直す：アプリは起動の最初にここで保存先を決める
        var reopened = StoreLocation.Resolve();
        Assert.Equal(new StoreRoot(destination, StoreRootSource.Configured), reopened);
        Assert.True(File.Exists(new AppPaths(reopened.Path).SettingsFile));
        Assert.True(File.Exists(Path.Combine(new AppPaths(reopened.Path).ItemsDir, "12345.json")));
        Assert.False(StoreLocation.LooksLikeStore(OldRoot));

        // 開き直すまで門は閉じたまま（命令の作り）
        Assert.True(StoreWriteGate.IsClosedForRestart);
    }

    [Fact]
    public async Task 置き換えても_location_jsonが運んだ先を指す()
    {
        MakeStore(OldRoot);
        StoreLocation.Save(OldRoot);
        var destination = Path.Combine(_directory, "there", "Chmonos");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "settings.json"), "{\"old\":true}");

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.MoveStore(OldRoot, destination, Replace: true));

        var moved = Assert.IsType<CommandResult.StoreMoved>(result).Result;
        Assert.True(moved.Succeeded, moved.Error);
        Assert.Equal(new StoreRoot(destination, StoreRootSource.Configured), StoreLocation.Resolve());
        Assert.True(File.Exists(Path.Combine(moved.ParkedAt!, "settings.json")));
    }

    /// <summary>
    /// **書き換えられなければ、元を消さずに元のまま続ける**（2026-10-06）。
    /// 前は元を消してから画面が書き換えていたので、書けないとデータは新しい場所にあるのに、開き直すと空の古い場所が開いた。
    /// 読み取り専用にした location.json で書けない場面を作る
    /// </summary>
    [Fact]
    public async Task 引越しで書き換えられなければ_元は消えず_運んだ物も残らず_location_jsonは前のまま_門は開く()
    {
        MakeStore(OldRoot);
        StoreLocation.Save(OldRoot);
        File.SetAttributes(StoreLocation.LocationFile, FileAttributes.ReadOnly);
        var destination = Path.Combine(_directory, "new", "Chmonos");

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.MoveStore(OldRoot, destination, Replace: false));

        var moved = Assert.IsType<CommandResult.StoreMoved>(result).Result;
        Assert.False(moved.Succeeded);
        Assert.StartsWith("新しい保存先の場所を記録できませんでした。", moved.Error);
        Assert.Null(moved.LeftoverAt);
        Assert.False(Directory.Exists(destination));
        Assert.True(File.Exists(Path.Combine(new AppPaths(OldRoot).ItemsDir, "12345.json")));
        Assert.Equal(new StoreRoot(OldRoot, StoreRootSource.Configured), StoreLocation.Resolve());
        Assert.False(StoreWriteGate.IsHeld);
        Assert.False(StoreWriteGate.IsClosedForRestart);
    }

    [Theory]
    [MemberData(nameof(Destinations))]
    public async Task 戻すと_location_jsonが戻した場所を指し_開き直すとそこが開く(string where)
    {
        var zip = MakeBackup();
        StoreLocation.Save(OldRoot);
        var destination = DestinationFor(where);

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.RestoreBackup(zip, destination));

        Assert.IsType<CommandResult.BackupRestored>(result);
        Assert.Equal(new StoreRoot(destination, StoreRootSource.Configured), StoreLocation.Resolve());
        Assert.True(File.Exists(Path.Combine(new AppPaths(destination).ItemsDir, "12345.json")));
        Assert.True(StoreWriteGate.IsClosedForRestart);

        // 今までの保存先には手を付けない
        Assert.True(File.Exists(Path.Combine(new AppPaths(OldRoot).ItemsDir, "12345.json")));
    }

    [Fact]
    public async Task 戻すで書き換えられなければ_展開した物を消し_location_jsonは前のまま_門は開く()
    {
        var zip = MakeBackup();
        StoreLocation.Save(OldRoot);
        File.SetAttributes(StoreLocation.LocationFile, FileAttributes.ReadOnly);
        var destination = Path.Combine(_directory, "restored", "Chmonos");

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.RestoreBackup(zip, destination));

        Assert.StartsWith("バックアップから戻せませんでした。", Assert.IsType<CommandResult.Failed>(result).Message);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(new StoreRoot(OldRoot, StoreRootSource.Configured), StoreLocation.Resolve());
        Assert.False(StoreWriteGate.IsHeld);
        Assert.False(StoreWriteGate.IsClosedForRestart);
    }

    private string DestinationFor(string where)
    {
        if (where == "same")
        {
            return Path.Combine(_directory, "new", "Chmonos");
        }

        var other = Path.Combine(Path.GetTempPath(), $"bam-relocate-other-{Guid.NewGuid():N}");
        _others.Add(other);
        return Path.Combine(other, "deep", "Chmonos");
    }

    private static void MakeStore(string root)
    {
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "{}");
        File.WriteAllText(Path.Combine(paths.ItemsDir, "12345.json"), "{\"boothItemId\":\"12345\"}");
    }

    private string MakeBackup()
    {
        MakeStore(OldRoot);
        var zip = Path.Combine(_directory, "backup.zip");
        BackupArchive.Export(OldRoot, zip, includeImages: false);
        return zip;
    }

    private static string? ReadLocation()
        => JsonSerializer.Deserialize<StoreLocationFile>(File.ReadAllText(StoreLocation.LocationFile), JsonStore.Options)?.Root;
}
