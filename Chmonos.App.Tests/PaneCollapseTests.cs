using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 左右に分かれた画面の左の一覧を、つまみで畳む・広げる（ユーザ指摘 2026-11-18・open.md 51）。
/// 畳んだかは画面ごとに覚えて次に開いたときも引き継ぎ、畳んでもドラッグで決めた幅は消さない（使う人の調整を消さない）。
/// </summary>
public sealed class PaneCollapseTests
{
    [Fact]
    public Task 畳むと細い帯の幅になり_広げるとドラッグで決めた幅に戻る() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var pane = new PaneColumn(app.Services.PaneWidths, "folder.list", remembersCollapse: true);
        pane.Width = new GridLength(512);

        pane.ToggleCollapseCommand.Execute(null);
        Assert.True(pane.IsCollapsed);
        Assert.Equal(PaneColumn.StripWidth, pane.Width.Value);
        Assert.False(pane.CanResize);

        // 畳んでいる間に列の幅が書き戻されても（GridSplitter・列の TwoWay）、覚えた幅は変えない
        pane.Width = new GridLength(PaneColumn.StripWidth);
        Assert.Equal(512, pane.Pixels);

        pane.ToggleCollapseCommand.Execute(null);
        Assert.False(pane.IsCollapsed);
        Assert.Equal(512, pane.Width.Value);
        await app.SettleAsync();
    });

    [Fact]
    public Task 畳んだかは画面ごとに覚え_開き直した画面と次の起動に引き継ぐ() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var folder = new PaneColumn(app.Services.PaneWidths, "folder.list", remembersCollapse: true);
        folder.ToggleCollapseCommand.Execute(null);

        // 画面は開くたびに作り直す。書き込みが済む前に開き直しても畳んだまま
        Assert.True(new PaneColumn(app.Services.PaneWidths, "folder.list", remembersCollapse: true).IsCollapsed);
        Assert.False(new PaneColumn(app.Services.PaneWidths, "tags.list", remembersCollapse: true).IsCollapsed);

        await app.SettleAsync();
        Assert.Equal(["folder.list"], app.Services.UiState.CollapsedPanes);

        new PaneColumn(app.Services.PaneWidths, "folder.list", remembersCollapse: true).ToggleCollapseCommand.Execute(null);
        await app.SettleAsync();
        Assert.Empty(app.Services.UiState.CollapsedPanes ?? []);
    });

    [Fact]
    public Task 覚えない列と決め打ちの列は_畳めず_覚えた畳みも持ち込まない() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        new PaneColumn(app.Services.PaneWidths, "resolve.list", remembersCollapse: true).ToggleCollapseCommand.Execute(null);

        // 組み込んだ未確定（一覧を出さない）は単独の画面と同じ鍵。単独で畳んだ覚えで中身を隠さない
        var embedded = new PaneColumn(app.Services.PaneWidths, "resolve.list", remembersCollapse: true) { Fixed = new GridLength(0) };
        Assert.False(embedded.CanCollapse);
        Assert.False(embedded.IsCollapsed);

        var plain = new PaneColumn(app.Services.PaneWidths, "edit.right");
        Assert.False(plain.CanCollapse);
        Assert.False(plain.ToggleCollapseCommand.CanExecute(null));
        await app.SettleAsync();
    });
}
