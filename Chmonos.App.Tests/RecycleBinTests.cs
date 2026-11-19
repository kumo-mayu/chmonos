using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// ごみ箱へ送る部品（外部の点検 2026-10-07）。Windows の削除そのものは差し替えて、渡す旗と受け取り方を確かめる。
/// 前は Microsoft.VisualBasic の削除で、ネットワークのドライブやごみ箱に入りきらない物を黙って完全に消していた
/// </summary>
public sealed class RecycleBinTests
{
    [Fact]
    public void ごみ箱へ送る旗で呼び_完全に消すときは聞かせる()
    {
        string? from = null;
        ushort flags = 0;

        RecycleBin.Send(@"C:\作り物\展開先", (list, given) =>
        {
            from = list;
            flags = given;
            return (0, false);
        });

        Assert.Equal(@"C:\作り物\展開先" + "\0\0", from);
        Assert.NotEqual(0, flags & RecycleBin.FofAllowUndo);
        Assert.NotEqual(0, flags & RecycleBin.FofWantNukeWarning);
    }

    /// <summary>完全に消すかを聞かれて「いいえ」なら、消さなかった理由にする。</summary>
    [Fact]
    public void 完全に消すのをやめたら_ごみ箱へ送れないとして返す()
    {
        var refused = Assert.Throws<NotRecyclableException>(() => RecycleBin.Send(@"C:\作り物\展開先", (_, _) => (0, true)));

        Assert.Contains("削除をやめました", refused.Message);
    }

    [Fact]
    public void Windowsが失敗を返したら_入出力の失敗にする()
    {
        var failed = Assert.Throws<IOException>(() => RecycleBin.Send(@"C:\作り物\展開先", (_, _) => (5, false)));

        Assert.IsNotType<NotRecyclableException>(failed);
        Assert.Equal(5, failed.HResult & 0xFFFF);
    }

    /// <summary>ネットワーク（UNC）と、無いドライブにはごみ箱が無いとみなす（そこで消すと完全に消える）。</summary>
    [Fact]
    public void ネットワークと無いドライブにはごみ箱が無いとみなす()
    {
        var used = DriveInfo.GetDrives().Select(drive => drive.Name[0]).ToHashSet();
        var unused = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter));

        Assert.False(RecycleBin.HasRecycleBin(@"\\server\share\展開先"));
        Assert.False(RecycleBin.HasRecycleBin($@"{unused}:\展開先"));
    }
    /// <summary>まとめて送るときは、二重の NUL で終わる一覧で1回だけ渡す（写しかけの片付けで、数千のファイルを1つずつ送ると遅い）</summary>
    [Fact]
    public void まとめて送るときは_一覧を1回で渡す()
    {
        var calls = new List<string>();

        RecycleBin.SendAll([@"C:\作り物\a.png", @"C:\作り物\b.json"], (list, _) =>
        {
            calls.Add(list);
            return (0, false);
        });

        Assert.Equal([@"C:\作り物\a.png" + "\0" + @"C:\作り物\b.json" + "\0\0"], calls);
    }

    /// <summary>
    /// 起動の片付けは、前のバックアップの書きかけをごみ箱へ送る（点検29・ユーザ判断 2026-11-20）。
    /// 書きかけの場所は手で直せる記録から読むので、名前の形が合うだけの使う人のファイルを指され得る。前は完全に消していた
    /// </summary>
    [Fact]
    public void バックアップの書きかけの片付けは_ごみ箱へ送る()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-backup-writing-" + Guid.NewGuid().ToString("N"));
        var store = Path.Combine(root, "store");
        var bin = Path.Combine(root, "ごみ箱");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(bin);
        try
        {
            var victim = Path.Combine(root, "使う人の.zip.tmp");
            File.WriteAllText(victim, "大事");
            Chmonos.Core.Storage.BackupWritingRecord.Begin(store, victim);
            var sent = new List<(string From, ushort Flags)>();

            // ごみ箱へ送った物は、ごみ箱から戻せる。作り物のごみ箱へ移して、消えていないことを見る
            var cleaned = Chmonos.App.AppServiceContainer.CleanUpBackupWriting(store, (list, flags) =>
            {
                sent.Add((list, flags));
                File.Move(list.TrimEnd('\0'), Path.Combine(bin, Path.GetFileName(list.TrimEnd('\0'))));
                return (0, false);
            });

            Assert.True(cleaned);
            var (from, given) = Assert.Single(sent);
            Assert.Equal(victim + "\0\0", from);
            Assert.NotEqual(0, given & RecycleBin.FofAllowUndo);
            Assert.Equal("大事", File.ReadAllText(Path.Combine(bin, "使う人の.zip.tmp")));
            Assert.False(File.Exists(Path.Combine(store, Chmonos.Core.Storage.BackupWritingRecord.FileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void まとめて送る物が無ければ_何も呼ばない()
    {
        var called = false;

        RecycleBin.SendAll([], (_, _) =>
        {
            called = true;
            return (0, false);
        });

        Assert.False(called);
    }
}
