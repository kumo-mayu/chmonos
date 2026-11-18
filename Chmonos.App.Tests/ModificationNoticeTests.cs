using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面と改変の詳細の、操作の結果の知らせの置き場（メモ20-③）。
/// 押した欄・ボタン・行のすぐ下へ出し、上の帯・左の欄の下の帯（Status）には出さない。
/// 自動保存の成功は出さない。
/// </summary>
public class ModificationNoticeTests
{
    private static async Task<ModificationViewModel> OpenDetailAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の衣装"));
        var main = await app.StartAsync();

        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1000001", "夏の改変")));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => modification.ItemSuggestions.Count > 0, "候補が出る");
        return modification;
    }

    [Fact]
    public Task 名前を自動で保存できたときは_何も言わない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.NameInput = "冬の改変";
        await modification.FlushPendingWritesAsync();

        Assert.Equal("冬の改変", modification.Record.Name);
        Assert.Equal(string.Empty, modification.NameNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task メモを自動で保存できたときは_何も言わない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.MemoInput = "貫通は袖だけ直す";
        await modification.FlushPendingWritesAsync();

        Assert.Equal("貫通は袖だけ直す", modification.Record.Memo);
        Assert.Equal(string.Empty, modification.MemoNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 候補に無い名前で足そうとすると_足す欄の下に警告を出し_上の帯には出さない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.AddMemberCommand.Execute("どこにも無い商品");
        await UiThread.Until(() => modification.AddNotice.HasText, "足す欄の下に出る");

        Assert.True(modification.AddNotice.IsWarning);
        Assert.Contains("手元に見つかりません", modification.AddNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 使ったものを足すと_足す欄の下に済んだことを出す() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => modification.Members.Count == 1, "足される");

        Assert.False(modification.AddNotice.IsWarning);
        Assert.Equal("「作り物の衣装」を追加しました。", modification.AddNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 行を外すと_使ったものの一覧の見出しの近くに出す() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);
        modification.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => modification.Members.Count == 1, "足される");

        modification.RemoveMemberCommand.Execute(modification.Members.Single());
        await UiThread.Until(() => modification.MembersNotice.Text.Contains("外しました"), "一覧の近くに出る");

        Assert.False(modification.MembersNotice.IsWarning);
        Assert.Equal(string.Empty, modification.Status);
    });
}
