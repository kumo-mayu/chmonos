using System.ComponentModel;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 1件だけ Unity へ送る（商品ページ・カードの右クリックの「Unityへ送る」「改変に追加して送る」）を、人が止めたときと本当に失敗したときの言い分け（2026-10-02）。
///
/// 前は人が帯の「中止」を押しても「「〇〇」をUnityへ送れませんでした。」と警告の窓が出て、失敗に読めた。
/// まとめて送るときと同じく「送るのを中止しました。」だけを言い、警告の窓は出さない。
/// Unity は動かさない：送り先にこの試験のプロセスを渡すと、Unity の窓が無いので「Unityが閉じられました」で本当に失敗する。
/// </summary>
public class UnitySendStopTests
{
    private static readonly OpenUnityEditor Editor = new(Environment.ProcessId, "作り物のプロジェクト");

    private static UnityPackageEntry Package(TestApp app)
        => new(app.NewFile("sample_costume.zip", [1, 2, 3]), "sample_costume.unitypackage", 100);

    /// <summary>送り始めた瞬間に、下の帯の「中止」を押す（Unity の窓を探す前に止まる）。</summary>
    private static async Task SendAndStopAsync(TestApp app, Func<Task> send)
    {
        var main = await app.StartAsync();
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSendingToUnity) && main.IsSendingToUnity)
            {
                main.StopUnityCommand.Execute(null);
            }
        };
        main.PropertyChanged += onChanged;
        try
        {
            await send();
            await app.SettleAsync();
        }
        finally
        {
            main.PropertyChanged -= onChanged;
        }
    }

    [Fact]
    public Task 商品ページから送って止めたら_欄の1行に送るのを中止しましたとだけ言い_窓は出さない() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        var lines = new List<(string Text, bool Failed)>();

        await SendAndStopAsync(app, () => ItemUnityActions.SendPickedAsync(
            app.Services, item, Editor, Package(app), "Unityへ送る", null, (text, failed) => lines.Add((text, failed))));

        Assert.Equal([("送るのを中止しました。", false)], lines);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task カードから送って止めたら_送るのを中止しましたを情報の窓で言い_警告にしない() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");

        await SendAndStopAsync(app, () => ItemUnityActions.SendPickedAsync(
            app.Services, item, Editor, Package(app), "Unityへ送る", null, null));

        var notice = Assert.Single(app.Notices);
        Assert.Equal("送るのを中止しました。", notice.Text);
        Assert.Equal(MessageBoxImage.Information, notice.Icon);
    });

    [Fact]
    public Task 止めずに本当に送れなかったら_今まで通り送れませんでしたを警告の窓で言う() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        var lines = new List<(string Text, bool Failed)>();
        await app.StartAsync();

        // 送り先は、決して動いていない PID にする（外部の点検 2026-10-07）。前は試験そのものの PID を使い、
        // 「試験には窓が無いので閉じられたと見る」前提だったが、試験の窓が出ていると取り出しへ進み、別の文で落ちた
        var closed = new OpenUnityEditor(int.MaxValue, "作り物のプロジェクト");
        await ItemUnityActions.SendPickedAsync(
            app.Services, item, closed, Package(app), "Unityへ送る", null, (text, failed) => lines.Add((text, failed)));
        await app.SettleAsync();

        var notice = Assert.Single(app.Notices);
        Assert.Equal("「sample_costume」をUnityへ送れませんでした。\n\nUnityが閉じられました", notice.Text);
        Assert.Equal(MessageBoxImage.Warning, notice.Icon);
        Assert.Empty(lines);
    });

    [Fact]
    public void 改変に追加して送るを止めたら_追加しましたと中止しましたを言い_失敗にしない()
    {
        Assert.Equal(
            ("「普段着」に追加しました。送るのを中止しました。", false),
            ItemUnityActions.AfterRecordText("普段着", new ItemUnityActions.SendResult(false, UnityImportQueue.StoppedBeforeWindowMessage)));
        Assert.Equal(
            ("「普段着」に追加しました。Unityへは送れませんでした。", true),
            ItemUnityActions.AfterRecordText("普段着", new ItemUnityActions.SendResult(false, null)));
        Assert.Equal(
            ("「普段着」に追加して、Unityへ送りました。", false),
            ItemUnityActions.AfterRecordText("普段着", new ItemUnityActions.SendResult(true, null)));
    }
}
