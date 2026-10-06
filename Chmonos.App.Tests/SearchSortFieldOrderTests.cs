using System.Windows.Data;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 表示順のプルダウンの並びとまとまり（ユーザ判断 2026-10-06）。
/// まとまりの間に区切り線を引き、属性は見出し「属性」の下に子として並べる。線と見出しは画面の GroupStyle が描くので、ここではまとまりの分け方を見る
/// </summary>
public sealed class SearchSortFieldOrderTests
{
    [Fact]
    public Task 表示順は決めた並びで_まとまりに分かれ_属性は最後のまとまりに属性の管理の順で並ぶ() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "かわいい" }, new AttributeDefinition { Name = "質感" }],
        });
        var search = (await app.StartAsync()).Search;

        Assert.Equal(
            ["名前", "ショップ", "カテゴリ", "スキ数", "BOOTH価格", "払った額", "公開日", "入手日", "商品閲覧日", "Unity送信日", "取り込み日", "容量", "かわいい", "質感"],
            search.SortFields.Select(field => field.Label));

        var groups = search.SortFieldGroups.Groups!.Cast<CollectionViewGroup>()
            .Select(group => (Name: (string)group.Name, Count: group.ItemCount))
            .ToList();
        Assert.Equal([("1", 4), ("2", 2), ("3", 5), ("4", 1), ("属性", 2)], groups);
        Assert.All(search.SortFields.Where(field => field.Group == "属性"), field => Assert.True(field.IsAttribute));
    });

    [Fact]
    public Task 並べ直しても_既定の表示順は入手日のまま() => TestApp.Run(async app =>
    {
        // 並べ直したので先頭は名前になった。繋ぎ直しで見つからないときの戻り先を先頭にすると、既定が名前に変わってしまう
        var search = (await app.StartAsync()).Search;
        Assert.Equal(SortKind.AcquiredAt, search.SortField.Kind);
    });
}
