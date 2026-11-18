using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップ一覧の「対象 ▾」（open.md 31・案C）。ボタンは検索画面と同じ字にし、今の対象は横の小さな字で常に見せる
/// （ユーザ指示 2026-09-16「対象がどちらかが常にわかるようにする」）。対象を変えたら、その字も読み上げの名前も追従する。
/// </summary>
public sealed class ShopsTargetNoteTests
{
    [Fact]
    public Task 今の対象は横の字に出て_対象を切り替えると追従する() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowShopsCommand.Execute(null);
        var shops = Assert.IsType<ShopsViewModel>(main.CurrentViewModel);

        var changed = new List<string?>();
        shops.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("ショップ名・メモ", shops.TargetsNote);
        Assert.Equal("対象：ショップ名・メモ", shops.TargetsText);

        shops.SearchMemos = false;
        Assert.Equal("ショップ名", shops.TargetsNote);
        Assert.Contains(nameof(ShopsViewModel.TargetsNote), changed);

        shops.SearchMemos = true;
        shops.SearchNames = false;
        Assert.Equal("メモ", shops.TargetsNote);

        // 最後の1つは外せない（対象が空にならない）
        shops.SearchMemos = false;
        Assert.Equal("メモ", shops.TargetsNote);
    });
}
