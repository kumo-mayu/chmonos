using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 連続で送るとき、取り込み画面を閉じた後に「次を出してよいか」を決める（#69・§11-3）。
/// 時刻と行は、実機で記録した並び（撫で音オイルオーブ・BlendShare・Cancel）を写している。
/// </summary>
public sealed class UnityImportWatchTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 17, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    private const string Completion =
        "Asset Pipeline Refresh (id=d6685d192bb108c4f9d442947a28a739): Total: 0.133 seconds - Initiated by RefreshV2(ForceSynchronousImport)";

    private static UnityImportWatch Watch() => new(["Assets/nHaruka/PenSystem/Pen.asset", "Assets/nHaruka/PenSystem"]);

    [Fact]
    public void 閉じる前は何があっても待つ()
    {
        // 利用者が取り込み画面を眺めている間は、何も決めない
        var watch = Watch();
        watch.LogLine("Start importing Assets/nHaruka/PenSystem/Pen.asset using Guid(abc)", At(0));
        watch.Windows(anyNewWindow: false, At(0));

        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(60)));
    }

    [Fact]
    public void 何も動かないまま5秒でCancel()
    {
        // Cancel では Unity は1行も書かず、窓も出ない（実機で12秒見た）
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.Windows(false, At(0.1));

        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(4.9)));
        Assert.Equal(UnityImportState.Cancelled, watch.Evaluate(At(5)));
    }

    [Fact]
    public void 関係のない行はCancelの判断を妨げない()
    {
        // Cancel の直後に出た行。これを「動いた」と数えて、来ない完了の行を待ち続けていた
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.LogLine("<RI> Initialized touch support.", At(0.3));
        watch.LogLine("TrimDiskCacheJob: Current cache size 4mb", At(0.3));
        watch.Windows(false, At(0.3));

        Assert.Equal(UnityImportState.Cancelled, watch.Evaluate(At(5.1)));
    }

    [Fact]
    public void 送った物のパスが出たら完了の行まで待ってImport()
    {
        var watch = Watch();
        watch.DialogClosed(At(0.28));
        watch.Windows(false, At(0.28));
        watch.LogLine("Start importing Assets/nHaruka/PenSystem/Pen.asset using Guid(57bf78b0) Importer(-1,0)", At(0.43));

        // 完了の行が出るまでは、何秒たっても決めない（静かになっても待つ）
        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(20)));

        watch.LogLine(Completion, At(20.1));
        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(20.5)));
        Assert.Equal(UnityImportState.Imported, watch.Evaluate(At(21.2)));
        Assert.True(watch.SawOwnImportLine);
    }

    [Fact]
    public void 動く前の完了の行は数えない()
    {
        // 別のエディタの取り込みの完了の行が、たまたま混じったとき
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.Windows(false, At(0));
        watch.LogLine(Completion, At(0.5));

        Assert.Equal(UnityImportState.Cancelled, watch.Evaluate(At(5)));
    }

    [Fact]
    public void 別の物のパスは動いたと数えない()
    {
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.Windows(false, At(0));
        watch.LogLine("Start importing Assets/FUKA/Addon/Other.asset using Guid(fe37)", At(0.5));

        Assert.Equal(UnityImportState.Cancelled, watch.Evaluate(At(5)));
        Assert.False(watch.SawOwnImportLine);
    }

    [Fact]
    public void Packagesに入る物は窓で動きを知り後処理の完了を待つ()
    {
        // BlendShare：閉じてからログが2秒止まるが、Package Manager の窓は0.7秒に出る。
        // その後コンパイル・読み込み直しを経て4.86秒に完了の行
        var watch = Watch();
        watch.DialogClosed(At(0.25));
        watch.Windows(false, At(0.25));
        watch.Windows(true, At(0.69));
        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(5.5)));

        watch.LogLine("[Package Manager] Done resolving packages in 2.00 seconds", At(2.28));
        watch.Windows(true, At(3.4));
        watch.LogLine(Completion, At(4.86));
        watch.Windows(true, At(4.9));
        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(5.3)));

        watch.Windows(false, At(5.32));
        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(6.0)));
        Assert.Equal(UnityImportState.Imported, watch.Evaluate(At(6.4)));
    }

    [Fact]
    public void ログが1行も来なければ窓が静かになってから終わりとみなす()
    {
        // Unity 6.5 からログはプロジェクトごと。全体のログを見ていても行が来ない
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.Windows(true, At(0.7));
        watch.Windows(false, At(3));

        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(7.9)));
        Assert.Equal(UnityImportState.Imported, watch.Evaluate(At(8)));
    }

    [Fact]
    public void 確認の窓が出ている間は待ち続ける()
    {
        // VPM の自動インストーラが「Confirm」を出した。利用者が答えるまで次へ進まない
        var watch = Watch();
        watch.DialogClosed(At(0));
        watch.LogLine("Start importing Assets/nHaruka/PenSystem using Guid(ab2b)", At(0.5));
        watch.LogLine(Completion, At(4));
        watch.Windows(true, At(4.1));

        Assert.Equal(UnityImportState.Waiting, watch.Evaluate(At(120)));
    }
}
