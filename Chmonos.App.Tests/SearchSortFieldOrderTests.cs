using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 表示順のボタンが開くメニュー（ユーザ判断 2026-10-06：「属性」の中に大量の属性が入っている形）。
/// 名前・ショップ・カテゴリ・スキ数／BOOTH価格・払った額／公開日〜取り込み日／容量／属性 ▸。「／」は区切り線、属性は「属性 ▸」の子。
/// メニューの部品は画面の ItemContainerStyle が SortMenu の値から作るので、ここでは並び・印・押せるか・選んだ結果を見る
/// </summary>
public sealed class SearchSortFieldOrderTests
{
    private const string Line = "—";

    private static string[] Labels(IEnumerable<SortMenuEntry> entries)
        => entries.Select(entry => entry.IsSeparator ? Line : entry.Label).ToArray();

    private static SortMenuEntry Entry(SearchViewModel search, string label)
        => search.SortMenu.Single(entry => entry.Label == label);

    private static SortMenuEntry AttributesEntry(SearchViewModel search)
        => search.SortMenu.Single(entry => entry.IsParent);

    private static string[] Checked(SearchViewModel search)
        => search.SortMenu.Concat(search.SortMenu.SelectMany(entry => entry.Children))
            .Where(entry => entry.IsChecked)
            .Select(entry => entry.Label)
            .ToArray();

    private static ItemRecord Acquired(ItemRecord item, int day)
        => item with { Local = item.Local with { AcquiredAt = new DateOnly(2026, 9, day) } };

    private static Task SaveAttributesAsync(TestApp app, params string[] names)
        => app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = names.Select(name => new AttributeDefinition { Name = name }).ToList(),
        });

    [Fact]
    public Task メニューは決めた並びで_まとまりの間に区切り線が入り_最後の属性の子に属性の管理の順で並ぶ() => TestApp.Run(async app =>
    {
        await SaveAttributesAsync(app, "質感", "かわいい");
        var search = (await app.StartAsync()).Search;

        Assert.Equal(
            ["名前", "ショップ", "カテゴリ", "スキ数", Line, "BOOTH価格", "払った額", Line,
             "公開日", "入手日", "商品閲覧日", "Unity送信日", "取り込み日", Line, "容量", Line, "属性"],
            Labels(search.SortMenu));

        var attributes = AttributesEntry(search);
        Assert.Equal(["質感", "かわいい"], Labels(attributes.Children));
        Assert.True(attributes.IsEnabled);
        Assert.Null(attributes.ChooseCommand);

        // 区切りは押せず、区切りごとに別の物（同じ物を一覧に何度も入れると、WPF が項目と部品の対応を取り違える）
        var separators = search.SortMenu.Where(entry => entry.IsSeparator).ToList();
        Assert.All(separators, separator => Assert.False(separator.IsEnabled));
        Assert.Equal(separators.Count, separators.Distinct(ReferenceEqualityComparer.Instance).Count());

        // 一番上の並びには属性を出さない（属性の数だけ伸びて、ほかの項目が埋もれないように）
        Assert.DoesNotContain(search.SortMenu, entry => entry.Label is "質感" or "かわいい");
    });

    [Fact]
    public Task 属性が0個のとき_属性は押せず_吹き出しで属性の管理を案内する() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        var attributes = AttributesEntry(search);
        Assert.Empty(attributes.Children);
        Assert.False(attributes.IsEnabled);
        Assert.Equal("属性の管理で属性を追加すると選べます。", attributes.DisabledHint);
        Assert.False(attributes.IsChecked);
    });

    [Fact]
    public Task 属性があるとき_属性に押せない理由を出さない() => TestApp.Run(async app =>
    {
        await SaveAttributesAsync(app, "質感");
        var search = (await app.StartAsync()).Search;

        Assert.Null(AttributesEntry(search).DisabledHint);
    });

    [Fact]
    public Task 既定は入手日に印が付き_ほかには付かない() => TestApp.Run(async app =>
    {
        await SaveAttributesAsync(app, "質感");
        var search = (await app.StartAsync()).Search;

        // 並べ直したので先頭は名前になった。繋ぎ直しで見つからないときの戻り先を先頭にすると、既定が名前に変わってしまう
        Assert.Equal(SortKind.AcquiredAt, search.SortField.Kind);
        Assert.Equal(["入手日"], Checked(search));
    });

    [Fact]
    public Task 名前を選ぶと_名前の順に並び替わり_印が名前へ移る() => TestApp.Run(async app =>
    {
        // 入手日の新しい順（既定）では い → あ、名前順では あ → い
        await app.AddItemAsync(Acquired(Make.Item("9900001", "あ作り物"), 1));
        await app.AddItemAsync(Acquired(Make.Item("9900002", "い作り物"), 2));
        var search = (await app.StartAsync()).Search;
        Assert.Equal(["9900002", "9900001"], search.DisplayItems.OfType<ItemCardViewModel>().Select(card => card.Item.Id));

        var raised = new List<string?>();
        search.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Entry(search, "名前").ChooseCommand!.Execute(null);
        await app.SettleAsync();

        Assert.Equal(SortKind.Name, search.SortField.Kind);
        Assert.Equal("名前", search.SortField.Label);
        Assert.Equal("名前順", search.Sort.Label);
        Assert.Equal(["9900001", "9900002"], search.DisplayItems.OfType<ItemCardViewModel>().Select(card => card.Item.Id));
        // メニューは開くたびに今の値から作る。替わったことを知らせないと、開いたメニューの印が前の項目に残る
        Assert.Contains(nameof(SearchViewModel.SortMenu), raised);
        Assert.Equal(["名前"], Checked(search));
    });

    [Fact]
    public Task 属性を選ぶと_その属性で並び_属性と子の両方に印が付く() => TestApp.Run(async app =>
    {
        await SaveAttributesAsync(app, "質感", "かわいい");
        var search = (await app.StartAsync()).Search;

        AttributesEntry(search).Children.Single(child => child.Label == "かわいい").ChooseCommand!.Execute(null);
        await app.SettleAsync();

        Assert.Equal(SortKind.Attribute, search.Sort.Kind);
        Assert.Equal("かわいい", search.Sort.AttributeName);
        Assert.Equal("かわいい", search.SortField.Label);
        Assert.Equal(["属性", "かわいい"], Checked(search));
    });

    [Theory]
    [InlineData(Key.Down, ModifierKeys.None, true)]
    [InlineData(Key.Down, ModifierKeys.Alt, true)]
    [InlineData(Key.Down, ModifierKeys.Control, false)]
    [InlineData(Key.Up, ModifierKeys.None, false)]
    [InlineData(Key.Right, ModifierKeys.None, false)]
    public void 表示順のボタンは_下矢印と_Altを足した下矢印で開く(Key key, ModifierKeys modifiers, bool opens)
    {
        // Enter・Space はボタンの Click で開く。上矢印や左右は帯の中の移動に残す
        Assert.Equal(opens, MenuButton.OpensMenu(key, modifiers));
    }
}
