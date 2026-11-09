using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 使っている人に促す「中身の更新」か（docs/plan.md の 7）。足した行が対応アバターの追加だけなら促さない。迷えば促す。
/// アバターの名前は作り物。
/// </summary>
public class UsedUpdateKindTests
{
    private static readonly string[] Names = ["アオイ", "Célia", "ルリ"];

    private static NotificationDiff History(params string[] added) => new()
    {
        Field = "更新履歴",
        Lines = added.Select(text => new NotificationLine { Kind = NotificationLineKind.Added, Text = text }).ToList(),
    };

    private static bool Prompts(params string[] added) => UsedUpdateKind.ShouldPrompt([History(added)], Names);

    [Theory]
    [InlineData("・2026/11/01 ver1.2 アオイ用Prefabを追加しました")]
    [InlineData("2026.11.1 「ルリ」に対応しました")]
    [InlineData("v1.0.3: Added support for Célia")]
    [InlineData("Supports Celia")]
    [InlineData("새로운 아바타 대응 완료")]
    [InlineData("もちふぃったー用プロファイルを公開")]
    [InlineData("<ルリ用>順変換プロファイル Ver.1.0.0を公開")]
    [InlineData("9/16 Lumi対応")]
    [InlineData("✧ アオイ対応")]
    [InlineData("-ルリちゃん対応リリース")]
    public void 対応アバターの追加だけなら促さない(string line)
        => Assert.False(Prompts(line));

    [Fact]
    public void 見出しの行と名前を並べただけの行も追加に数える()
        => Assert.False(Prompts("2026/11/01", "対応アバターを追加しました", "・アオイ、ルリ", "・CELIA ちゃん"));

    [Theory]
    [InlineData("・2026/11/01 ver1.2 一部のテクスチャを修正")]
    [InlineData("アオイ Ver1.3.0 に対応")]
    [InlineData("Modular Avatarに対応しました")]
    [InlineData("Fixed a bug in the skirt")]
    [InlineData("아오이 수정")]
    [InlineData("アオイ用の新しいテクスチャ")]
    [InlineData("アオイ用の新しいテクスチャを追加")]
    [InlineData("ルリ Ver 1.2.0 のバージョン検出に対応しました")]
    [InlineData("同じ共通素体のアバターも、商品ページの対応アバターに表示するようにしました")]
    [InlineData("新しい色を追加しました")]
    public void 修正や機能やアバターの新しい版への対応は促す(string line)
        => Assert.True(Prompts(line));

    [Fact]
    public void 追加と修正が混ざれば促す()
        => Assert.True(Prompts("アオイに対応しました", "PhysBoneを調整しました"));

    [Fact]
    public void 日付と版だけの行しか無ければ言い切れないので促す()
        => Assert.True(Prompts("2026/11/01", "ver1.2"));

    [Fact]
    public void 足した行が上限で切れていたら促す()
    {
        var diff = History("アオイに対応しました");
        var cut = new NotificationDiff { Field = diff.Field, Lines = diff.Lines, MoreAdded = 3 };

        Assert.True(UsedUpdateKind.ShouldPrompt([cut], Names));
    }

    [Fact]
    public void 行の差を持たない古い形の知らせは促す()
        => Assert.True(UsedUpdateKind.ShouldPrompt([new NotificationDiff { Field = "更新履歴", Before = "a", After = "b" }], Names));

    [Fact]
    public void 更新履歴が動いていなければ促さない()
        => Assert.False(UsedUpdateKind.ShouldPrompt(
            [new NotificationDiff { Field = BoothChanges.PriceField, Before = "¥ 500", After = "¥ 400" }], Names));

    [Fact]
    public void 消した行は見ない()
    {
        var diff = new NotificationDiff
        {
            Field = "更新履歴",
            Lines =
            [
                new NotificationLine { Kind = NotificationLineKind.Removed, Text = "不具合を修正" },
                new NotificationLine { Kind = NotificationLineKind.Added, Text = "アオイに対応しました" },
            ],
        };

        Assert.False(UsedUpdateKind.ShouldPrompt([diff], Names));
    }
}
