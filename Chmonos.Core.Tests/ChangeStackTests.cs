using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未読の更新の知らせに、後から来た差を重ねる（ユーザ判断 2026-10-02「重ねましょう」）。
/// 前は差し替えていて、既読にする前に2回変わると1回目の差が消えていた。
/// </summary>
public class ChangeStackTests
{
    private static NotificationDiff Diff(string field, string? before, string? after, params NotificationLine[] lines)
        => new() { Field = field, Before = before, After = after, Lines = lines.Length == 0 ? null : lines };

    private static NotificationLine Added(string text) => new() { Kind = NotificationLineKind.Added, Text = text };

    private static NotificationLine Removed(string text) => new() { Kind = NotificationLineKind.Removed, Text = text };

    private static string Show(IEnumerable<NotificationLine>? lines)
        => string.Join(" | ", (lines ?? []).Select(line => $"{(line.Kind == NotificationLineKind.Added ? "+" : "-")}{line.Text}"));

    [Fact]
    public void 同じ欄が2回変わったら_最初の前から最後の後にまとめる()
    {
        var stacked = ChangeStack.Stack(
            [Diff(BoothChanges.PriceField, "¥ 500", "¥ 600")],
            [Diff(BoothChanges.PriceField, "¥ 600", "¥ 800")]);

        var price = Assert.Single(stacked);
        Assert.Equal(("¥ 500", "¥ 800"), (price.Before, price.After));
    }

    [Fact]
    public void 違う欄の変化は_前の知らせの順のまま後ろに足す()
    {
        var stacked = ChangeStack.Stack(
            [Diff(BoothChanges.NameField, "旧", "新"), Diff(BoothChanges.PriceField, "¥ 500", "¥ 600")],
            [Diff(BoothChanges.ImagesField, "3 枚", "4 枚")]);

        Assert.Equal(
            [BoothChanges.NameField, BoothChanges.PriceField, BoothChanges.ImagesField],
            stacked.Select(diff => diff.Field));
    }

    [Theory]
    [InlineData(BoothChanges.PriceField, "¥ 500", "¥ 600")]
    [InlineData(BoothChanges.SaleField, "販売中", "販売終了")]
    [InlineData(BoothChanges.VariationsField, "3 件", "4 件")]
    public void 戻って元と同じになった欄は_差から外す(string field, string start, string middle)
    {
        var stacked = ChangeStack.Stack(
            [Diff(BoothChanges.NameField, "旧", "新"), Diff(field, start, middle)],
            [Diff(field, middle, start)]);

        Assert.Equal(BoothChanges.NameField, Assert.Single(stacked).Field);
    }

    [Fact]
    public void 全部戻ったら_空になる()
    {
        var stacked = ChangeStack.Stack(
            [Diff(BoothChanges.PriceField, "¥ 500", "¥ 600")],
            [Diff(BoothChanges.PriceField, "¥ 600", "¥ 500")]);

        Assert.Empty(stacked);
    }

    [Fact]
    public void 足した行を後で消したら_足した側から取り除き_残りは続ける()
    {
        var stacked = ChangeStack.Stack(
            [Diff("更新履歴", "v1.0", "v1.0", Added("v1.1 予告"), Added("v1.1 公開"))],
            [Diff("更新履歴", "v1.0", "v1.0", Removed("v1.1 予告"), Added("v1.2 公開"))]);

        Assert.Equal("+v1.1 公開 | +v1.2 公開", Show(Assert.Single(stacked).Lines));
    }

    [Fact]
    public void 消した行を後で戻したら_消した側から取り除く()
    {
        var stacked = ChangeStack.Stack(
            [Diff("注意事項", "改変可", "改変可", Removed("再配布禁止"), Added("再配布可"))],
            [Diff("注意事項", "改変可", "改変可", Removed("再配布可"), Added("再配布禁止"))]);

        // 頭の抜き出しも同じで、行も打ち消し合ったので、欄ごと外れる
        Assert.Empty(stacked);
    }

    [Fact]
    public void 同じ文の行は1本ずつ打ち消す()
    {
        var stacked = ChangeStack.Stack(
            [Diff("更新履歴", "a", "a", Added("・修正"), Added("・修正"))],
            [Diff("更新履歴", "a", "a", Removed("・修正"))]);

        Assert.Equal("+・修正", Show(Assert.Single(stacked).Lines));
    }

    [Fact]
    public void 足された見出しを後で消したら_欄ごと外す()
    {
        var stacked = ChangeStack.Stack(
            [Diff("お知らせ", null, "セール中", Added("セール中"))],
            [Diff("お知らせ", "セール中", null, Removed("セール中"))]);

        Assert.Empty(stacked);
    }

    [Fact]
    public void 頭の抜き出しが同じでも_行が残っていれば外さない()
    {
        var stacked = ChangeStack.Stack(
            [Diff("更新履歴", "v1.0", "v1.0", Added("v1.1"))],
            [Diff("更新履歴", "v1.0", "v1.0", Added("v1.2"))]);

        Assert.Equal("+v1.1 | +v1.2", Show(Assert.Single(stacked).Lines));
    }

    [Fact]
    public void 重ねて上限を超えた行は_種類ごとに数だけ残す()
    {
        var first = Enumerable.Range(1, 20).Select(index => Added($"前{index}")).ToArray();
        var second = Enumerable.Range(1, 20).Select(index => Added($"後{index}")).ToArray();

        var stacked = Assert.Single(ChangeStack.Stack(
            [new NotificationDiff { Field = "更新履歴", Before = "a", After = "b", Lines = first, MoreRemoved = 2 }],
            [new NotificationDiff { Field = "更新履歴", Before = "b", After = "c", Lines = [.. second, Removed("消1")], MoreAdded = 3 }]));

        Assert.Equal(LineDiff.MaxLines, stacked.Lines!.Count(line => line.Kind == NotificationLineKind.Added));
        Assert.Equal("+前1", Show(stacked.Lines!.Take(1)));
        Assert.Equal(3 + (40 - LineDiff.MaxLines), stacked.MoreAdded);
        Assert.Equal(2, stacked.MoreRemoved);
        Assert.Single(stacked.Lines!, line => line.Kind == NotificationLineKind.Removed);
    }
}
