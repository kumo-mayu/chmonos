using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// メモ32（2026-10-04）：並べ替えのドラッグで端まで来たら流す・改変を選ぶ窓のアバターの候補を「持っている」で分ける・
/// リストの見方のときは小分類を縦1列にする。計算で決まる所を試験にし、見た目は ViewShot で見る
/// </summary>
public class ManageDragAndPickCandidatesTests
{
    // ----- ドラッグで端に来たら流す（DragEdgeScroll） -----

    [Fact]
    public void 端の帯の外では流れず_上の帯では上へ_下の帯では下へ流れる()
    {
        const double extent = 400;
        Assert.Equal(0, DragEdgeScroll.Velocity(200, extent));
        Assert.Equal(0, DragEdgeScroll.Velocity(DragEdgeScroll.ZoneHeight, extent));
        Assert.True(DragEdgeScroll.Velocity(20, extent) < 0);
        Assert.True(DragEdgeScroll.Velocity(extent - 20, extent) > 0);
    }

    [Fact]
    public void 端に近いほど速く_端の外まで出ても最速で頭打ち()
    {
        const double extent = 400;
        Assert.True(Math.Abs(DragEdgeScroll.Velocity(5, extent)) > Math.Abs(DragEdgeScroll.Velocity(30, extent)));
        Assert.Equal(-DragEdgeScroll.MaxSpeed, DragEdgeScroll.Velocity(0, extent));
        Assert.Equal(-DragEdgeScroll.MaxSpeed, DragEdgeScroll.Velocity(-500, extent));
        Assert.Equal(DragEdgeScroll.MaxSpeed, DragEdgeScroll.Velocity(extent + 500, extent));
    }

    [Fact]
    public void 小さな欄では帯が高さの3分の1までで_真ん中は流れない()
    {
        const double extent = 60;
        Assert.Equal(0, DragEdgeScroll.Velocity(30, extent));
        Assert.True(DragEdgeScroll.Velocity(10, extent) < 0);
        Assert.Equal(0, DragEdgeScroll.Velocity(10, 0));
    }

    private static ScrollViewer Tall(double contentHeight = 1000)
    {
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = contentHeight, Width = 100 },
        };
        viewer.Measure(new Size(200, 200));
        viewer.Arrange(new Rect(0, 0, 200, 200));
        viewer.UpdateLayout();
        return viewer;
    }

    [Fact]
    public Task 流す量は範囲の中でだけ動き_動いた量を返す() => UiThread.Run(() =>
    {
        var viewer = Tall();
        Assert.Equal(30, DragEdgeScroll.Apply(viewer, 30));
        viewer.UpdateLayout(); // 位置は次の配置で反映される（実際の流れでは DragOver の間に配置が挟まる）
        Assert.Equal(30, viewer.VerticalOffset);
        Assert.Equal(-30, DragEdgeScroll.Apply(viewer, -500));
        viewer.UpdateLayout();
        Assert.Equal(0, viewer.VerticalOffset);
        Assert.Equal(viewer.ScrollableHeight, DragEdgeScroll.Apply(viewer, 99999));
        viewer.UpdateLayout();
        Assert.Equal(viewer.ScrollableHeight, viewer.VerticalOffset);

        // 流せない欄（中身が収まっている）は動かさない
        var fits = Tall(100);
        Assert.Equal(0, DragEdgeScroll.Apply(fits, 50));
    });

    [Fact]
    public Task 流す欄は_並びの欄の中の一番外側の流し枠を選び_無ければ外の枠を選ぶ() => UiThread.Run(() =>
    {
        var inner = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 80, Content = new Border { Height = 400, Width = 50 } };
        var outer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { inner, new Border { Height = 900 } } } };
        var list = new ContentControl { Content = outer, Height = 200 }; // 高さを縛らないと外側の枠は流せない（中身の高さまで伸びる）
        var page = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = 2000, Child = list } };
        // 内側の小さな枠の上に点があっても、並びを持つ外側を選ぶ
        page.Measure(new Size(300, 300));
        page.Arrange(new Rect(0, 0, 300, 300));
        page.UpdateLayout();

        Assert.Same(outer, DragEdgeScroll.FindScroller(inner, list));

        // 並びの欄（scope）の中に流し枠が無いときは、その外の枠
        var plain = new Border { Height = 50 };
        var holder = new ContentControl { Content = plain };
        var wrapper = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = 1000, Child = holder } };
        wrapper.Measure(new Size(300, 300));
        wrapper.Arrange(new Rect(0, 0, 300, 300));
        wrapper.UpdateLayout();
        Assert.Same(wrapper, DragEdgeScroll.FindScroller(plain, holder));
    });

    [Fact]
    public Task 流す時計は_最初の1回では動かさない() => UiThread.Run(() =>
    {
        var viewer = Tall();
        var edge = new DragEdgeScroll();

        // 押し直した直後の1回目は経過時間が無いので動かない（間の時間で飛ばない）
        Assert.Equal(0, edge.Update(viewer, 0));
        Assert.Equal(0, viewer.VerticalOffset);
    });

    // ----- 候補を「持っている」で分ける（SuggestBox.Arrange・改変を選ぶ窓） -----

    private static readonly string[] Avatars = ["B持ち", "A持ち", "C持ち", "Bほか", "Aほか"];

    [Fact]
    public void 候補は先の群が先に来て_群の境目の上に区切りが付く()
    {
        var all = new[] { "持1", "持2", "ほか1", "ほか2" };

        var arranged = SuggestBox.Arrange(all, "", primaryCount: 2);

        Assert.Equal(["持1", "持2", "ほか1", "ほか2"], arranged.Select(pair => pair.Entry));
        Assert.Equal([false, false, true, false], arranged.Select(pair => pair.DividerAbove));
    }

    [Fact]
    public void 絞り込んでも群の順は崩れず_片方の群しか残らなければ区切りは出ない()
    {
        // 部分一致の「持」が前方一致の「ほか持」より先に来る（群が先。前方一致は群の中での並び）
        var all = new[] { "A持", "持B", "ほか持", "持C" };
        var arranged = SuggestBox.Arrange(all, "持", primaryCount: 2);

        // 先の群（A持・持B）の中で前方一致（持B）が先、その下に区切りがあって、ほかの群（持C・ほか持）
        Assert.Equal(["持B", "A持", "持C", "ほか持"], arranged.Select(pair => pair.Entry));
        Assert.Equal([false, false, true, false], arranged.Select(pair => pair.DividerAbove));

        // ほかの群だけが当たるときは線を引かない（先に出す物が無いのに線だけ出ると、上に何かあるように見える）
        Assert.DoesNotContain(SuggestBox.Arrange(all, "ほか", primaryCount: 2), pair => pair.DividerAbove);
        // 先の群だけが当たるときも同じ
        Assert.DoesNotContain(SuggestBox.Arrange(all, "B", primaryCount: 2), pair => pair.DividerAbove);
    }

    [Fact]
    public void 群を分けない指定では今までどおりの並びで_区切りは出ない()
    {
        var arranged = SuggestBox.Arrange(["か", "あ", "あか"], "あ", primaryCount: 0);

        Assert.Equal(["あ", "あか"], arranged.Select(pair => pair.Entry));
        Assert.DoesNotContain(arranged, pair => pair.DividerAbove);
    }

    [Fact]
    public Task 改変を選ぶ窓のアバターの候補は_持っているアバターが先で_件数が区切りの位置になる() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバターB（持っていない）", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のアバターC（買った）", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000003", BoothName = "作り物のアバターA（手で持っている）", AvatarOverride = true, IsOwnedManually = true },
                new AvatarRegistryEntry { ItemId = "2000004", BoothName = "作り物のアバターD（持っていない）", AvatarOverride = true },
            ],
        });
        await app.StartAsync();

        var model = ModificationPicking.BuildDialog(
            app.Services, "題", "見出し", "", [], "既存", "決定", "空",
            ownedItemIds: new HashSet<string> { "2000002" });

        Assert.Equal(2, model.OwnedAvatarCount);
        // 持っている2つ（名前順）→ 持っていない2つ（名前順）
        Assert.Contains("アバターA", model.AvatarNames[0]);
        Assert.Contains("アバターC", model.AvatarNames[1]);
        Assert.Contains("アバターB", model.AvatarNames[2]);
        Assert.Contains("アバターD", model.AvatarNames[3]);
    });

    [Fact]
    public Task 持っているアバターが無いときは_件数0で区切りを出さない() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバター", AvatarOverride = true }],
        });
        await app.StartAsync();

        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [], "既存", "決定", "空");

        Assert.Equal(0, model.OwnedAvatarCount);
        Assert.Single(model.AvatarNames);
    });

    // ----- リストの見方のときは小分類を縦1列に（ColumnsPanel.SingleColumn） -----

    private static double HeightWith(bool single)
    {
        var panel = new ColumnsPanel { MinColumnWidth = 300, SingleColumn = single };
        for (var i = 0; i < 4; i++)
        {
            panel.Children.Add(new Border { Height = 40 });
        }

        panel.Measure(new Size(1300, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1300, panel.DesiredSize.Height));
        return panel.DesiredSize.Height;
    }

    [Fact]
    public Task 幅が足りていても_1列の指定なら縦に並ぶ() => UiThread.Run(() =>
    {
        // 幅1300・最小300 → 4列に並んで1段（高さ40）。1列の指定では4段（高さ160）
        Assert.Equal(40, HeightWith(single: false));
        Assert.Equal(160, HeightWith(single: true));
    });
}
