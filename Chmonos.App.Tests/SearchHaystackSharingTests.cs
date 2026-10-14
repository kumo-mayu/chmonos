using Chmonos.App.Tests.Support;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 画面が作った検索用の文字列を記録の物ごとに覚え、API（同じプロセス）が借りられるようにする（2026-10-14：二重持ちが実物に近い2万件で約185MB）。
/// API が借りる側は Chmonos.Query.Tests の <c>アプリと共有しているときは_画面が覚えた検索用の文字列を借りる</c>
/// </summary>
public sealed class SearchHaystackSharingTests
{
    [Fact]
    public Task 読み込んだ商品も_後から差し替えた商品も_今の記録の物で覚える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("90000001", "作り物のコート"));
        await app.StartAsync();

        // API と同じく、保存先の控えから読んだ記録の物で引く
        var loaded = await app.Store.Items.LoadAsync("90000001");
        var first = SearchText.RememberedFor(loaded!);
        Assert.NotNull(first);

        var changed = loaded! with { Booth = loaded.Booth with { Name = "作り物のコート 改" } };
        await app.AddItemAsync(changed);
        var saved = await app.Store.Items.LoadAsync("90000001");
        app.Main.Search.NoteItemChanged(saved!);

        var second = SearchText.RememberedFor(saved!);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Contains("改", second!.Folded(SearchField.Name));
    });
}
