using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// ごみ箱の無いドライブに残った、前のバックアップの書きかけを、起動後の帯で知らせる（ユーザ判断 2026-11-20・案C）。
/// 残すだけでログにしか書かず、書きかけが誰にも気付かれず残っていた
/// </summary>
public sealed class LeftBackupWritingTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-left-writing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "store"));
        return root;
    }

    [Fact]
    public void ごみ箱の無いドライブで残したときだけ_残した場所を知らせる()
    {
        var root = NewRoot();
        var store = Path.Combine(root, "store");
        try
        {
            var temporary = Path.Combine(root, "外付け.zip.tmp");
            File.WriteAllText(temporary, "書きかけ");
            BackupWritingRecord.Begin(store, temporary);
            var left = new List<string>();

            AppServiceContainer.CleanUpBackupWriting(store, hasRecycleBin: _ => false, onLeft: left.Add);

            Assert.Equal([temporary], left);
            Assert.True(File.Exists(temporary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ごみ箱へ送れたときは知らせない()
    {
        var root = NewRoot();
        var store = Path.Combine(root, "store");
        try
        {
            var temporary = Path.Combine(root, "手元.zip.tmp");
            File.WriteAllText(temporary, "書きかけ");
            BackupWritingRecord.Begin(store, temporary);
            var left = new List<string>();

            AppServiceContainer.CleanUpBackupWriting(
                store,
                (list, _) =>
                {
                    File.Delete(list.TrimEnd('\0'));
                    return (0, false);
                },
                hasRecycleBin: _ => true,
                onLeft: left.Add);

            Assert.Empty(left);
            Assert.False(File.Exists(temporary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public Task 帯は場所を言い_閉じると消え_窓より先に見つけた答えも受ける() => TestApp.Run(async app =>
    {
        // 窓を作る前に見つけた答え
        app.Services.NoteBackupWritingLeft(@"E:\backup\a.zip.tmp");
        var main = await app.StartAsync();

        Assert.True(main.HasLeftBackupWriting);
        Assert.Contains(@"E:\backup\a.zip.tmp", main.LeftBackupWritingText);
        Assert.Contains("残っています", main.LeftBackupWritingText);

        // 窓を作った後に見つけた答えは、画面のスレッドで受けて入れ替える
        app.Services.NoteBackupWritingLeft(@"F:\backup\b.zip.tmp");
        await UiThread.Until(() => main.LeftBackupWriting == @"F:\backup\b.zip.tmp", "後から来た答え");

        main.DismissLeftBackupWritingCommand.Execute(null);
        Assert.False(main.HasLeftBackupWriting);
        Assert.Equal(string.Empty, main.LeftBackupWritingText);
    });

    [Fact]
    public Task 何も残っていなければ帯は出ない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        Assert.False(main.HasLeftBackupWriting);
    });
}
