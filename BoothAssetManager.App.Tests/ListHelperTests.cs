using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BoothAssetManager.App.Services;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 一覧を組み直す小さな道具：差分で寄せる・カードを行に切る・まとめて差し替える・段に切る。
///
/// どれも「残った物には触らない」ことに意味がある（触ると画面の部品が作り直されて固まる。`docs/dev/wpf.md`）。
/// 結果の並びだけでなく、**何回知らせが飛んだか・同じ物のままか**を確かめる。画面では固まりとしてしか見えなかった。
/// </summary>
public class ListHelperTests
{
    private sealed class Card(string name)
    {
        public string Name { get; } = name;

        public override string ToString() => Name;
    }

    private sealed class Row
    {
        public ObservableCollection<Card> Cards { get; } = [];
    }

    private static List<Card> Cards(int count) => Enumerable.Range(1, count).Select(number => new Card($"c{number}")).ToList();

    // ---- CollectionSync ----

    [Fact]
    public void 差分で寄せると_目当ての並びになる()
    {
        var all = Cards(5);
        var list = new ObservableCollection<Card>([all[0], all[1], all[2]]);

        CollectionSync.Apply(list, [all[2], all[3], all[0], all[4]]);

        Assert.Equal([all[2], all[3], all[0], all[4]], list);
    }

    [Fact]
    public void 並びが同じなら_一覧に何も知らせない()
    {
        var all = Cards(3);
        var list = new ObservableCollection<Card>(all);
        var changes = 0;
        list.CollectionChanged += (_, _) => changes++;

        CollectionSync.Apply(list, all);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void 末尾に1件足すだけなら_足す知らせが1回だけ()
    {
        // 丸ごと差し替えると、残った行の部品まで全部作り直される
        var all = Cards(4);
        var list = new ObservableCollection<Card>([all[0], all[1], all[2]]);
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, args) => actions.Add(args.Action);

        CollectionSync.Apply(list, all);

        Assert.Equal([NotifyCollectionChangedAction.Add], actions);
    }

    [Fact]
    public void 途中の1件を抜くだけなら_抜く知らせが1回だけ()
    {
        var all = Cards(4);
        var list = new ObservableCollection<Card>(all);
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, args) => actions.Add(args.Action);

        CollectionSync.Apply(list, [all[0], all[2], all[3]]);

        Assert.Equal([NotifyCollectionChangedAction.Remove], actions);
    }

    [Fact]
    public void 空へ寄せると_全部を抜く()
    {
        var list = new ObservableCollection<Card>(Cards(3));

        CollectionSync.Apply(list, []);

        Assert.Empty(list);
    }

    // ---- CardRowLayout ----

    private static void Layout(ObservableCollection<Row> rows, IReadOnlyList<Card> cards, int columns)
        => CardRowLayout.Apply(rows, cards, columns, () => new Row(), row => row.Cards);

    [Fact]
    public void カードを列の数で行に切る()
    {
        var cards = Cards(7);
        var rows = new ObservableCollection<Row>();

        Layout(rows, cards, columns: 3);

        Assert.Collection(
            rows,
            row => Assert.Equal([cards[0], cards[1], cards[2]], row.Cards),
            row => Assert.Equal([cards[3], cards[4], cards[5]], row.Cards),
            row => Assert.Equal([cards[6]], row.Cards));
    }

    [Fact]
    public void 列が増えても_並びの合っている行とカードには触らない()
    {
        var cards = Cards(6);
        var rows = new ObservableCollection<Row>();
        Layout(rows, cards, columns: 2);
        var firstRow = rows[0];
        var firstRowChanges = new List<NotifyCollectionChangedAction>();
        firstRow.Cards.CollectionChanged += (_, args) => firstRowChanges.Add(args.Action);

        Layout(rows, cards, columns: 3);

        Assert.Equal(2, rows.Count);
        Assert.Same(firstRow, rows[0]);
        Assert.Equal([cards[0], cards[1], cards[2]], rows[0].Cards);
        Assert.Equal([cards[3], cards[4], cards[5]], rows[1].Cards);

        // 1行目は、合っている2枚をそのままにして、3枚目を足すだけ
        Assert.Equal([NotifyCollectionChangedAction.Add], firstRowChanges);
    }

    [Fact]
    public void 同じ並びを同じ列の数で切り直しても_何も知らせない()
    {
        var cards = Cards(5);
        var rows = new ObservableCollection<Row>();
        Layout(rows, cards, columns: 2);
        var changes = 0;
        rows.CollectionChanged += (_, _) => changes++;
        foreach (var row in rows)
        {
            row.Cards.CollectionChanged += (_, _) => changes++;
        }

        Layout(rows, cards, columns: 2);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void カードが減ると_余った行を後ろから抜く()
    {
        var cards = Cards(6);
        var rows = new ObservableCollection<Row>();
        Layout(rows, cards, columns: 2);

        Layout(rows, cards.Take(3).ToList(), columns: 2);

        Assert.Collection(
            rows,
            row => Assert.Equal([cards[0], cards[1]], row.Cards),
            row => Assert.Equal([cards[2]], row.Cards));
    }

    [Fact]
    public void 列の数が0以下でも_1列として切る()
    {
        // 画面が出る前は幅が0で、列の数が0になる
        var cards = Cards(2);
        var rows = new ObservableCollection<Row>();

        Layout(rows, cards, columns: 0);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void カードが無ければ_行も無い()
    {
        var rows = new ObservableCollection<Row>();
        Layout(rows, Cards(3), columns: 2);

        Layout(rows, [], columns: 2);

        Assert.Empty(rows);
    }

    // ---- RangeObservableCollection ----

    [Fact]
    public void まとめて差し替えると_知らせは1回だけ()
    {
        // 1行ずつ足すと、1行ごとに一覧へ知らせが飛ぶ（1つのフォルダに1000本で1000回）
        var list = new RangeObservableCollection<string> { "前の行" };
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, args) => actions.Add(args.Action);

        list.ReplaceAll(Enumerable.Range(1, 1000).Select(number => $"行{number}"));

        Assert.Equal(1000, list.Count);
        Assert.Equal("行1", list[0]);
        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
    }

    // ---- ManageItemLayout（タグ・属性の管理の商品の段）----

    private static List<TagItemRow> Items(int count) => Enumerable.Range(1, count)
        .Select(number => new TagItemRow { ItemId = $"100000{number}", Name = $"作り物の商品{number}", ShopName = "sample-shop" })
        .ToList();

    [Theory]
    [InlineData(1000, 248, 4)]
    [InlineData(992, 248, 4)]
    [InlineData(991, 248, 3)]
    [InlineData(100, 248, 1)]
    [InlineData(0, 248, 1)]
    [InlineData(500, 0, 1)]
    public void 幅に入る数を_はみ出す手前までで数える(double width, double slot, int expected)
        => Assert.Equal(expected, ManageItemLayout.ColumnsFor(width, slot));

    [Fact]
    public void 横に並べるときは_左から右へ流して次の段へ()
    {
        var items = Items(5);

        var lines = ManageItemLayout.Split(items, columns: 2, vertical: false);

        Assert.Collection(
            lines,
            line => Assert.Equal([items[0], items[1]], line),
            line => Assert.Equal([items[2], items[3]], line),
            line => Assert.Equal([items[4]], line));
    }

    [Fact]
    public void 縦に並べるときは_上から下へ流して次の列へ()
    {
        var items = Items(5);

        var lines = ManageItemLayout.Split(items, columns: 2, vertical: true);

        // 3段。1列目に 1・2・3、2列目に 4・5
        Assert.Collection(
            lines,
            line => Assert.Equal([items[0], items[3]], line),
            line => Assert.Equal([items[1], items[4]], line),
            line => Assert.Equal([items[2]], line));
    }

    [Fact]
    public void 商品が無ければ段も無く_列が多すぎても1段に収まる()
    {
        Assert.Empty(ManageItemLayout.Split([], columns: 3, vertical: false));

        var items = Items(2);
        Assert.Equal([items[0], items[1]], Assert.Single(ManageItemLayout.Split(items, columns: 10, vertical: false)));
    }

    [Fact]
    public void 中身が同じ段は_前の段をそのまま使う()
    {
        // 段を作り直すと、見えている段の部品（カード）が全部作り直される
        var items = Items(4);
        var first = new HashSet<ManageItemLine>();
        var before = ManageItemLayout.Line([items[0], items[1]], card: true, [], first);
        var previous = ManageItemLayout.IndexByFirst([before]);

        var same = ManageItemLayout.Line([items[0], items[1]], card: true, previous, []);
        var different = ManageItemLayout.Line([items[0], items[2]], card: true, previous, []);
        var otherKind = ManageItemLayout.Line([items[0], items[1]], card: false, previous, []);

        Assert.Same(before, same);
        Assert.NotSame(before, different);
        Assert.NotSame(before, otherKind);
    }

    [Fact]
    public void 同じ段を_一覧に2回は置かない()
    {
        // 同じ商品が2つの小分類に入っていると、中身の同じ段ができる
        var items = Items(2);
        var before = ManageItemLayout.Line(items, card: true, [], []);
        var previous = ManageItemLayout.IndexByFirst([before]);
        var used = new HashSet<ManageItemLine>();

        var first = ManageItemLayout.Line(items, card: true, previous, used);
        var second = ManageItemLayout.Line(items, card: true, previous, used);

        Assert.Same(before, first);
        Assert.NotSame(first, second);
    }

    // ---- カードの大きさ ----

    [Theory]
    [InlineData(228, 228)]
    [InlineData(10, CardMetrics.MinWidth)]
    [InlineData(5000, CardMetrics.MaxWidth)]
    [InlineData(200.4, 200)]
    [InlineData(double.NaN, CardMetrics.DefaultWidth)]
    public void カードの幅は_決めた範囲に収める(double width, double expected)
        // 手で直した設定（範囲の外・数でない値）でも、一覧が割れない幅にする
        => Assert.Equal(expected, CardMetrics.Clamp(width));
}
