using Chmonos.App.Views;
using static Chmonos.App.Views.SearchView;

namespace Chmonos.App.Tests;

/// <summary>
/// 絞り込みのつまみは、見た目どおり「条件をクリア」の次・「条件を追加」の前に止まる（ユーザ指示 2026-10-06）。
/// つまみは絞り込みの外の部品なので、前後の部品から手でつなぐ。その行き先を決める関数を見る
/// </summary>
public sealed class FilterToggleTabTests
{
    [Theory]
    [InlineData(TabStop.ClearFilters, false, true, TabStop.Toggle)]
    [InlineData(TabStop.Toggle, false, true, TabStop.AddModule)]
    [InlineData(TabStop.AddModule, true, true, TabStop.Toggle)]
    [InlineData(TabStop.Toggle, true, true, TabStop.ClearFilters)]
    // 条件が無いと「条件をクリア」は押せず止まれないので、「…」の次に置く
    [InlineData(TabStop.FilterMenu, false, false, TabStop.Toggle)]
    [InlineData(TabStop.Toggle, true, false, TabStop.FilterMenu)]
    public void 開いている間は_条件をクリアと条件を追加の間に止まる(TabStop from, bool backward, bool canClear, TabStop expected)
        => Assert.Equal(expected, FilterToggleTabTarget(from, backward, collapsed: false, canClear));

    [Theory]
    [InlineData(TabStop.FilterMenu, false, true)]
    [InlineData(TabStop.ClearFilters, true, true)]
    [InlineData(TabStop.AddModule, false, true)]
    public void それ以外の向きは_いつもの順に任せる(TabStop from, bool backward, bool canClear)
        => Assert.Null(FilterToggleTabTarget(from, backward, collapsed: false, canClear));

    [Fact]
    public void 畳んだ間は_つながない()
        => Assert.Null(FilterToggleTabTarget(TabStop.Toggle, backward: false, collapsed: true, canClear: true));
}
