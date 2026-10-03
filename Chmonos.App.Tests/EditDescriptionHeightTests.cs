using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の商品説明の欄の高さ（ユーザ指摘 2026-10-02 メモ4）。
/// 決め打ちの 360 だったのを、欄の下の縁のつまみ（<see cref="HeightGrip"/>）で変え、画面の幅と同じく ui-state.json に覚え、ダブルクリックで戻す
/// </summary>
public class EditDescriptionHeightTests
{
    private static async Task<EditViewModel> OpenEditAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        return Assert.IsType<EditViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task 既定は前と同じ360で_変えた高さは覚えて_次に開いた画面も同じ高さで出る() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);
        Assert.Equal(360, edit.DescriptionHeight);

        edit.DescriptionHeight = 520;
        Assert.Equal(520, edit.DescriptionHeight);

        // 止まってから少し待って ui-state.json に書く（画面の幅と同じ）
        await UiThread.Until(
            () => app.Services.UiState.PaneWidths.TryGetValue("edit.description", out var height) && height == 520,
            "説明の高さが ui-state に書かれる");

        app.Main.ShowSearchCommand.Execute(null);
        app.Main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(520, Assert.IsType<EditViewModel>(app.Main.CurrentViewModel).DescriptionHeight);
    });

    [Fact]
    public Task 範囲の外まで引いた高さは_端で止める() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);

        edit.DescriptionHeight = 40;
        Assert.Equal(edit.DescriptionMinHeight, edit.DescriptionHeight);

        edit.DescriptionHeight = 9000;
        Assert.Equal(edit.DescriptionMaxHeight, edit.DescriptionHeight);
    });

    [Fact]
    public Task ダブルクリックの戻すで_既定の360に戻る() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);
        edit.DescriptionHeight = 700;

        edit.ResetDescriptionHeightCommand.Execute(null);
        Assert.Equal(360, edit.DescriptionHeight);
        await UiThread.Until(() => !app.Services.UiState.PaneWidths.ContainsKey("edit.description"), "覚えた高さが消える");
    });

    [Fact]
    public Task つまみは_今の欄の高さから測り_範囲の外へ引いた分も数えて_引き戻すとマウスに付いてくる() => UiThread.Run(() =>
    {
        // 短い説明：上限 360 に対して欄は 200 の高さで止まっている
        var box = new Border { Height = 200 };
        var grip = new HeightGrip { Target = box, Length = 360, MinLength = 120, MaxLength = 1600 };
        var panel = new StackPanel { Children = { box, grip } };
        panel.Measure(new Size(300, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));

        grip.BeginResize();

        // 上限の 360 からでなく、見えている 200 から縮む
        grip.Resize(-30);
        Assert.Equal(170, grip.Length);

        // 最小の 120 を割る所まで引いても 120 で止まる
        grip.Resize(-100);
        Assert.Equal(120, grip.Length);

        // 引いた分（-130）を戻していく途中は 120 のまま、越えたらマウスの所
        grip.Resize(60);
        Assert.Equal(130, grip.Length);
    });

    [Fact]
    public Task つまみが外側の見える範囲の下へ出たら_出た分だけ外側を流す() => UiThread.Run(() =>
    {
        var box = new Border { Height = 200 };
        var grip = new HeightGrip { Target = box };
        var scroller = new ScrollViewer
        {
            Height = 300,
            Width = 300,
            Content = new StackPanel { Children = { box, grip } },
        };
        Layout(scroller);

        // 収まっているうちは流さない
        grip.FollowIntoView();
        Assert.Equal(0, scroller.VerticalOffset);

        // 欄を伸ばして、つまみ（欄の下 8）の下端が 508 になる。見える高さ 300 を 208 越えた
        box.Height = 500;
        Layout(scroller);
        grip.FollowIntoView();
        Layout(scroller);
        Assert.Equal(208, scroller.VerticalOffset);
        Assert.Equal(0, HeightGrip.OverflowBelow(300, 300));
    });

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(300, 300));
        root.Arrange(new Rect(0, 0, 300, 300));
        root.UpdateLayout();
    }
}
