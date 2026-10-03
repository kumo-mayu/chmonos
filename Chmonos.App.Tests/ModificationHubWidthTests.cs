using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面（一覧と詳細）の幅の配り。詳細が横に送らずに済む幅を先に残し、一覧の方を縮める
/// （幅 1280 の窓でも詳細が横に送られて右の列が切れていた。2026-10-03）。
/// </summary>
public class ModificationHubWidthTests
{
    [Fact]
    public void 詳細が残したい幅は_本文の下限に縦のバーの幅を足した値()
        => Assert.Equal(720 + SystemParameters.VerticalScrollBarWidth, ModificationHubViewModel.WidthWanted(720));

    [Fact]
    public Task 何も選んでいないときは_残したい幅を求めない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowModificationsCommand.Execute(null);
        await app.SettleAsync();

        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        Assert.Equal(0, hub.DetailWidthWanted);
    });

    [Fact]
    public Task 反対側に残したい幅が列の最小より広ければ_一覧の幅はその分だけ頭打ちになる() => TestApp.Run(async app =>
    {
        var pane = new PaneColumn(app.Services.PaneWidths, "modifications.list");
        var grid = new PaneGrid { Pane = pane };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });

        // 覚えた幅の既定は 400。入れ物 1072 で右に 737 を残すと、一覧は 335（最小 300 より広い）まで縮む
        grid.OppositeWidth = 737;
        grid.Measure(new Size(1072, 600));
        Assert.Equal(335, pane.Width.Value, 1);

        // 残したい幅が無ければ、右の列の最小（360）までしか縮めない＝一覧は覚えた幅のまま
        grid.OppositeWidth = 0;
        grid.Measure(new Size(1072, 600));
        Assert.Equal(400, pane.Width.Value, 1);
        await Task.CompletedTask;
    });
}
