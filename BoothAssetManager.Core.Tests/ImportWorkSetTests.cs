using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの対象集合。走らせている最中に足せることが要点。
/// </summary>
public class ImportWorkSetTests
{
    private static string Path(params string[] parts)
        => string.Join(System.IO.Path.DirectorySeparatorChar, parts);

    [Fact]
    public void HandsOutWhatWasPutIn()
    {
        var work = new ImportWorkSet([Path("D:", "a"), Path("D:", "b")]);

        Assert.True(work.HasPending);
        Assert.Equal(2, work.TakePending().Count);
        Assert.False(work.HasPending);
    }

    /// <summary>取り出した後に足したものは、次の周回で出てくる。</summary>
    [Fact]
    public void KeepsFoldersAddedAfterTheFirstRound()
    {
        var work = new ImportWorkSet([Path("D:", "a")]);
        work.TakePending();

        Assert.Equal(1, work.Add([Path("D:", "b")]));

        Assert.True(work.HasPending);
        Assert.Equal([Path("D:", "b")], work.TakePending());
    }

    /// <summary>同じフォルダを二度積んでも、走査は一度きり。</summary>
    [Fact]
    public void RefusesAFolderItHasAlreadyAccepted()
    {
        var work = new ImportWorkSet([Path("D:", "a")]);

        Assert.Equal(0, work.Add([Path("D:", "a")]));
        Assert.Equal(0, work.Add([Path("D:", "a") + System.IO.Path.DirectorySeparatorChar]));
        Assert.Single(work.TakePending());
    }

    /// <summary>大文字小文字と末尾の区切りは同じものとして扱う（Windowsのパスなので）。</summary>
    [Fact]
    public void TreatsCaseAndTrailingSeparatorAsTheSameFolder()
    {
        var work = new ImportWorkSet([Path("D:", "Assets")]);

        Assert.Equal(0, work.Add([Path("D:", "assets")]));
        Assert.Single(work.TakePending());
    }

    /// <summary>
    /// 既に入っているフォルダの**配下**は足さない。
    /// 親を走査すれば配下も見るので、入れると同じファイルを二度ハッシュすることになる。
    /// </summary>
    [Fact]
    public void SkipsAFolderInsideOneItAlreadyHas()
    {
        var work = new ImportWorkSet([Path("D:", "assets")]);

        Assert.Equal(0, work.Add([Path("D:", "assets", "vrchat")]));
        Assert.Single(work.TakePending());
    }

    /// <summary>逆に親を足すのは受ける。外側にまだ見ていない範囲がある。</summary>
    [Fact]
    public void AcceptsTheParentOfAFolderItAlreadyHas()
    {
        var work = new ImportWorkSet([Path("D:", "assets", "vrchat")]);

        Assert.Equal(1, work.Add([Path("D:", "assets")]));
        Assert.Equal(2, work.TakePending().Count);
    }

    /// <summary>
    /// まだ順番が来ていなければ取り下げられる。
    /// 「積んだ直後に取り消したい」への答えがこれで、取り消し操作を別に作らずに済む。
    /// </summary>
    [Fact]
    public void TakesBackAFolderThatHasNotBeenScannedYet()
    {
        var work = new ImportWorkSet([Path("D:", "a"), Path("D:", "b")]);

        Assert.True(work.Remove(Path("D:", "b")));
        Assert.Equal([Path("D:", "a")], work.TakePending());
    }

    /// <summary>走査済みのものは戻せない。そこは全体の中断で対応する。</summary>
    [Fact]
    public void CannotTakeBackAFolderAlreadyScanned()
    {
        var work = new ImportWorkSet([Path("D:", "a")]);
        work.TakePending();

        Assert.False(work.Remove(Path("D:", "a")));
    }

    /// <summary>取り下げたフォルダは、後からもう一度積める。</summary>
    [Fact]
    public void AllowsAWithdrawnFolderToBeAddedAgain()
    {
        var work = new ImportWorkSet([Path("D:", "a")]);
        work.Remove(Path("D:", "a"));

        Assert.Equal(1, work.Add([Path("D:", "a")]));
    }
}
