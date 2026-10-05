using System.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「錠の外で読んだ古い写しで書き戻す」を、錠の取り合いで再現する（時計に頼らない）。
/// 別の書き手（取り込みなど）が商品の錠を持ったまま変えかけている間に、試す操作を走らせ、
/// その操作が錠を待つところまで進めてから（＝錠の外で読むなら読み終えてから）別の書き手に書かせる。
/// 操作が錠の中で今の値に当てていれば、別の書き手の変更は残る。
/// </summary>
internal static class ItemLockRace
{
    public static async Task WhileAnotherWriterChangesAsync(
        DataStore store,
        string itemId,
        Func<LocalBlock, LocalBlock> otherChange,
        Func<Task> act)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = Task.Run(() => store.Items.ChangeLocalAsync(
            itemId,
            local =>
            {
                entered.Set();
                release.Wait();
                return otherChange(local);
            },
            LocalOwners.Import));
        entered.Wait();

        var acting = Task.Run(act);

        // 待つ長さは安全の打ち切りだけで、結果は時計に左右されない（錠を待つ人の数で進める）
        var waited = Stopwatch.StartNew();
        while (store.Items.LockUsers(itemId) < 2 && !acting.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(5);
        }

        release.Set();
        await Task.WhenAll(other, acting);
    }

    /// <summary>別の書き手が足すファイル（取り込みが同じ商品へ足した物の代わり）。</summary>
    public static LocalBlock AddFile(LocalBlock local, string hash = "BBBB")
        => local with
        {
            LocalFiles =
            [
                .. local.LocalFiles,
                new LocalFileRecord { Hash = hash, Paths = [$@"C:\作り物\{hash}.zip"], SizeBytes = 1 },
            ],
        };
}
