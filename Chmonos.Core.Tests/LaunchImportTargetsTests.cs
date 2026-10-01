using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 起動時に自動で取り込む物（ユーザ判断 2026-09-29）：監視フォルダの新着と、前回途中で止まった取り込みの続き。
/// </summary>
public class LaunchImportTargetsTests
{
    private static readonly string[] NoNewFiles = [];

    [Fact]
    public void TakesWatchedNewFilesWhenThereIsNoResume()
    {
        var targets = LaunchImportTargets.Collect([@"D:\watch\a.zip"], state: null);

        Assert.Equal([@"D:\watch\a.zip"], targets);
    }

    /// <summary>
    /// ①の途中で閉じた回は、ファイルが走査の控えに載っていて新着に数えられない。
    /// それでも続きの対象（前回の対象）を積む。前はここで何も始まらなかった
    /// </summary>
    [Fact]
    public void TakesTheInterruptedTargetsEvenWithoutNewFiles()
    {
        var state = new ImportState { Done = 3, Total = 10, Targets = [@"D:\booth"] };

        Assert.Equal([@"D:\booth"], LaunchImportTargets.Collect(NoNewFiles, state));
    }

    /// <summary>BOOTH の不調で取れなかった商品のファイルも、「続きから進む」と同じく積む</summary>
    [Fact]
    public void TakesTheUnfetchedFiles()
    {
        var state = new ImportState
        {
            Unfetched = [new UnfetchedItem { ItemId = "1", Paths = [@"D:\booth\b.zip"] }],
        };

        Assert.Equal([@"D:\booth\b.zip"], LaunchImportTargets.Collect(NoNewFiles, state));
    }

    /// <summary>両方にある物は1つにする（大文字小文字だけ違うパスも同じ）</summary>
    [Fact]
    public void MergesBothWithoutDuplicates()
    {
        var state = new ImportState { Done = 1, Total = 2, Targets = [@"D:\watch\A.zip", @"D:\booth"] };

        var targets = LaunchImportTargets.Collect([@"D:\watch\a.zip"], state);

        Assert.Equal([@"D:\watch\a.zip", @"D:\booth"], targets);
    }

    /// <summary>
    /// 最後まで走った回（取れなかった商品も無い）は、対象の記録が残っていても積まない。
    /// 下の帯も出していない（伝えることが無い）ので、起動時に走り直すと何もしていないのに通信することになる
    /// </summary>
    [Fact]
    public void IgnoresAFinishedRun()
    {
        var state = new ImportState { Done = 5, Total = 5, Targets = [@"D:\booth"] };

        Assert.Empty(LaunchImportTargets.Collect(NoNewFiles, state));
        Assert.Empty(LaunchImportTargets.Collect(NoNewFiles, state: null));
    }
}
