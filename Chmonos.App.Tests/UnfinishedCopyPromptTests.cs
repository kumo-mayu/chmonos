using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 引越し・戻すの途中で止まった写しかけを、設定の「場所を変える」「バックアップから戻す」で選んだとき（実機の確かめ 2026-10-07）。
/// 前は写しかけを「選んだ場所のライブラリ」として出し、「選んだ場所のデータを使う」で商品の大半が欠けたライブラリへ切り替わった。
///
/// 写しかけは、運ぶ処理を進み具合の知らせから例外で止めて作る（拾われない例外なので片付けを通らず、プロセスが落ちたのと同じ姿が残る）
/// </summary>
public class UnfinishedCopyPromptTests
{
    private sealed class Crash : Exception;

    private sealed class CrashAtTheEnd : IProgress<StoreMoveProgress>
    {
        public void Report(StoreMoveProgress value)
        {
            if (value.Copied == value.Total)
            {
                throw new Crash();
            }
        }
    }

    /// <summary>商品と設定がそろった写しかけを作る（印が無ければライブラリに見える）。選んだフォルダと、その中の番兵を返す。</summary>
    private static (string Folder, string Sentinel) LeaveUnfinishedCopy(TestApp app)
    {
        var source = Path.Combine(app.Root, "old-store");
        Directory.CreateDirectory(Path.Combine(source, "items"));
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(source, "items", "1.json"), "{ \"id\": \"1\" }");

        var folder = Path.Combine(app.Root, "picked", "Chmonos");
        Directory.CreateDirectory(folder);
        var sentinel = Path.Combine(folder, "番兵.txt");
        File.WriteAllText(sentinel, "元からあった物");

        Assert.Throws<Crash>(() => StoreMover.Move(source, folder, new CrashAtTheEnd()));
        Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
        return (folder, sentinel);
    }

    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app)
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task 場所を変えるで写しかけを選ぶと_ライブラリとして比べず_写しかけだと言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);
        var (folder, _) = LeaveUnfinishedCopy(app);

        await settings.ChangeRootToAsync(folder);

        var request = Assert.Single(app.Choices);
        Assert.Equal(UnfinishedCopyPrompt.Title, request.Title);
        Assert.Equal("選んだ場所は、前の引越しの途中で止まったコピーです。", request.Question);
        Assert.Contains("ファイル 2 個", request.Detail);
        Assert.Equal(UnfinishedCopyPrompt.CleanAnswer, request.First);
        Assert.DoesNotContain(app.Choices, choice => choice.Title == "どちらのライブラリを残しますか");

        // やめたら何も変えない
        Assert.True(UnfinishedCopy.IsAt(folder));
        Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.False(settings.HasPendingRoot);
    });

    [Fact]
    public Task 片付けると_写しで作った物だけを消し_元からあった物は残して_続けて聞く() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);
        var (folder, sentinel) = LeaveUnfinishedCopy(app);
        app.Choose = request => request.Title == UnfinishedCopyPrompt.Title
            ? Views.ChoiceDialogResult.First
            : Views.ChoiceDialogResult.Cancel;

        await settings.ChangeRootToAsync(folder);

        Assert.False(UnfinishedCopy.IsAt(folder));
        Assert.False(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(folder, "items")));
        Assert.Equal("元からあった物", File.ReadAllText(sentinel));

        // 片付けた後は、ライブラリでも空でもない場所として扱う（番兵が残るので使えないと言う）
        Assert.Single(app.Choices);
        Assert.Contains(app.Notices, notice => notice.Caption == "この場所は使えません");
        Assert.False(settings.HasPendingRoot);
    });

    [Fact]
    public Task 戻すで写しかけを選ぶと_写しかけだと言い_やめたら戻さない() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);
        var (folder, sentinel) = LeaveUnfinishedCopy(app);

        await settings.RestoreBackupIntoAsync(Path.Combine(app.Root, "none.zip"), folder);

        var request = Assert.Single(app.Choices);
        Assert.Equal(UnfinishedCopyPrompt.Title, request.Title);
        Assert.DoesNotContain(app.Notices, notice => notice.Caption == "この場所には戻せません");
        Assert.True(UnfinishedCopy.IsAt(folder));
        Assert.True(File.Exists(sentinel));
    });

    [Fact]
    public void 起動した保存先が写しかけなら_開かずに何の写しかけかと直し方を言う()
    {
        var marker = new UnfinishedCopyMarker
        {
            Kind = UnfinishedCopyKind.Move,
            StartedAt = DateTimeOffset.Now,
            From = @"C:\old",
        };

        var text = UnfinishedCopyPrompt.StartupText(@"D:\half", marker);

        Assert.StartsWith("保存先が、前の引越しの途中で止まったコピーです。ライブラリとしては開けません。", text);
        Assert.Contains("元のデータは「C:\\old」に残っています。", text);
        Assert.EndsWith("location.jsonの保存先を元の場所に直してから、開き直してください。", text);
    }
}
