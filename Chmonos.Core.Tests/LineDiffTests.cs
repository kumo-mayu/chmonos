using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 説明文の見出しの、変わった行だけを取り出す（メモ13-②）。
/// 頭の70字の抜き出しでは、見出しの後ろの方が変わったときに前後が同じに見えていた。
/// </summary>
public class LineDiffTests
{
    private static string Show(IReadOnlyList<NotificationLine> lines)
        => string.Join(" | ", lines.Select(line => $"{(line.Kind == NotificationLineKind.Added ? "+" : "-")}{line.Text}"));

    [Fact]
    public void SaysNothingForTheSameText()
        => Assert.Empty(LineDiff.Compare("v1.0 公開\nv1.1 直しました", "v1.0 公開\nv1.1 直しました"));

    [Fact]
    public void IgnoresSpacingAndEmptyLines()
        => Assert.Empty(LineDiff.Compare("v1.0  公開\n\n　v1.1 直しました ", "v1.0 公開\r\nv1.1 直しました\n\n"));

    [Fact]
    public void FindsALineAddedAtTheEnd()
    {
        // 頭の70字は同じになる長さの本文。後ろに1行足しただけでも、その行が出る
        var head = new string('あ', 80);
        var lines = LineDiff.Compare($"{head}\nv1.0 公開", $"{head}\nv1.0 公開\nv1.1 袖の形を直しました");

        Assert.Equal("+v1.1 袖の形を直しました", Show(lines));
    }

    [Fact]
    public void FindsARemovedLine()
        => Assert.Equal(
            "-旧版は配布を終えました",
            Show(LineDiff.Compare("注意書き\n旧版は配布を終えました\n連絡先", "注意書き\n連絡先")));

    [Fact]
    public void ShowsAReplacedLineAsRemovedThenAdded()
        => Assert.Equal(
            "-価格：500円 | +価格：800円",
            Show(LineDiff.Compare("内容\n価格：500円\n連絡先", "内容\n価格：800円\n連絡先")));

    [Fact]
    public void KeepsTheOrderOfTheNewText()
        => Assert.Equal(
            "+一行目に足した | -真ん中を消した | +最後に足した",
            Show(LineDiff.Compare("A\n真ん中を消した\nB", "一行目に足した\nA\nB\n最後に足した")));

    [Fact]
    public void TreatsAMissingTextAsEmpty()
    {
        Assert.Equal("+足した見出しの本文 | +2行目", Show(LineDiff.Compare(null, "足した見出しの本文\n2行目")));
        Assert.Equal("-消えた見出しの本文", Show(LineDiff.Compare("消えた見出しの本文", "")));
        Assert.Empty(LineDiff.Compare(null, "  \n "));
    }

    [Fact]
    public void CountsRepeatedLines()
        => Assert.Equal("+---", Show(LineDiff.Compare("---\nA\n---", "---\nA\n---\n---")));

    [Fact]
    public void ClipsAVeryLongLine()
    {
        var line = new string('長', LineDiff.MaxLineLength + 50);
        var added = Assert.Single(LineDiff.Compare("", line));

        Assert.Equal(LineDiff.MaxLineLength + 1, added.Text.Length);
        Assert.EndsWith("…", added.Text);
    }

    /// <summary>突き合わせる表が大きすぎる本文でも、固まらずに相手に無い行を拾う。</summary>
    [Fact]
    public void HandlesTextsTooLongToAlign()
    {
        var before = string.Join('\n', Enumerable.Range(0, 1200).Select(index => $"行{index}"));
        var after = string.Join('\n', Enumerable.Range(0, 1200).Select(index => index == 600 ? "差し替えた行" : $"行{index}").Reverse());

        var lines = LineDiff.Compare(before, after);

        Assert.Equal("-行600 | +差し替えた行", Show(lines));
    }

    [Fact]
    public void BoothChangesKeepsTheChangedLinesOfASection()
    {
        var head = new string('あ', 80);
        var diffs = BoothChanges.Describe(
            Block([new H2Section { Heading = "★更新履歴★", Text = $"{head}\nv1.0 公開" }]),
            Block([new H2Section { Heading = "★更新履歴★", Text = $"{head}\nv1.0 公開\nv1.1 直しました" }]));

        var diff = Assert.Single(diffs);
        Assert.Equal(diff.Before, diff.After);
        Assert.Equal("+v1.1 直しました", Show(diff.Lines!));
        Assert.Null(diff.MoreAdded);
    }

    [Fact]
    public void BoothChangesKeepsTheLinesOfAddedAndRemovedSections()
    {
        var diffs = BoothChanges.Describe(
            Block([new H2Section { Heading = "旧版について", Text = "旧版は配布を終えました" }, new H2Section { Heading = "使い方", Text = "本文" }]),
            Block([new H2Section { Heading = "同梱物", Text = "unitypackage\nテクスチャ" }, new H2Section { Heading = "使い方", Text = "本文" }]));

        Assert.Equal("+unitypackage | +テクスチャ", Show(diffs.Single(diff => diff.Field == "同梱物").Lines!));
        Assert.Equal("-旧版は配布を終えました", Show(diffs.Single(diff => diff.Field == "旧版について").Lines!));
    }

    [Fact]
    public void BoothChangesKeepsOnlyTheFirstLinesAndCountsTheRest()
    {
        var text = string.Join('\n', Enumerable.Range(0, LineDiff.MaxLines + 7).Select(index => $"行{index}"));
        var diff = Assert.Single(BoothChanges.Describe(
            Block([new H2Section { Heading = "使い方", Text = "本文" }]),
            Block([new H2Section { Heading = "使い方", Text = "本文" }, new H2Section { Heading = "同梱物", Text = text }])));

        Assert.Equal(LineDiff.MaxLines, diff.Lines!.Count);
        Assert.Equal(7, diff.MoreAdded);
        Assert.Null(diff.MoreRemoved);
    }

    [Fact]
    public void BoothChangesKeepsTheLinesWhenThereAreNoSections()
    {
        var diff = Assert.Single(BoothChanges.Describe(
            Block([], "説明\n対応：A"),
            Block([], "説明\n対応：A\n対応：B")));

        Assert.Equal("+対応：B", Show(diff.Lines!));
    }

    /// <summary>短い値の欄は前 → 後のまま。行の差は持たない（JSON にも書かない）。</summary>
    [Fact]
    public void ShortValuesHaveNoLines()
    {
        var diffs = BoothChanges.Describe(
            Block([], "説明", name: "旧"),
            Block([], "説明", name: "新"));

        Assert.Null(Assert.Single(diffs).Lines);
    }

    /// <summary>JSON は人が読める形（種類は語で、行はそのまま）。差の無い欄には何も足さない。</summary>
    [Fact]
    public void WritesReadableJson()
    {
        var diffs = new[]
        {
            new NotificationDiff
            {
                Field = "更新履歴",
                Lines = [new NotificationLine { Kind = NotificationLineKind.Added, Text = "v1.1 直しました" }],
            },
            new NotificationDiff { Field = "価格", Before = "¥ 1,000", After = "¥ 1,200" },
        };

        var json = JsonSerializer.Serialize(diffs, JsonStore.Options);

        Assert.Contains("\"kind\": \"added\"", json);
        Assert.Contains("\"text\": \"v1.1 直しました\"", json);
        Assert.Equal(2, json.Split("\"lines\"").Length);
        Assert.DoesNotContain("more", json);

        var back = JsonSerializer.Deserialize<NotificationDiff[]>(json, JsonStore.Options)!;
        Assert.Equal("+v1.1 直しました", Show(back[0].Lines!));
        Assert.Null(back[1].Lines);
    }

    private static BoothBlock Block(IReadOnlyList<H2Section> sections, string? description = "説明", string name = "商品")
        => new()
        {
            FetchedAt = DateTimeOffset.Now,
            Name = name,
            PriceText = "¥ 1,000",
            Description = description,
            H2Sections = sections,
        };
}
