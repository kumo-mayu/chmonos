using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

public sealed class SearchHistoryTests
{
    [Fact]
    public void 何も絞っていなければ残す価値が無い()
        => Assert.True(new SearchHistoryEntry().IsEmpty);

    [Fact]
    public void 空白だけの文字列は条件にしない()
        => Assert.True(new SearchHistoryEntry { Text = "   " }.IsEmpty);

    [Fact]
    public void 探す範囲だけを変えても条件にはしない()
    {
        // 「本文も探す」を入れただけでは何も絞っていない。履歴に残すものではない
        Assert.True(new SearchHistoryEntry { SearchBody = true }.IsEmpty);
    }

    [Fact]
    public void 文字列があれば条件になる()
        => Assert.False(new SearchHistoryEntry { Text = "衣装" }.IsEmpty);

    [Fact]
    public void 積んだ順が違うだけなら同じ検索とみなす()
    {
        var a = new SearchHistoryEntry { Text = "衣装", BoothTags = ["夏", "冬"] };
        var b = new SearchHistoryEntry { Text = "衣装", BoothTags = ["冬", "夏"] };

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 時刻と名前は同一性に含めない()
    {
        var a = new SearchHistoryEntry { Text = "衣装", UsedAt = DateTimeOffset.Now };
        var b = new SearchHistoryEntry
        {
            Text = "衣装",
            UsedAt = DateTimeOffset.Now.AddDays(-3),
            Name = "夏物を探すとき",
        };

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 探す範囲が違えば別の検索()
    {
        // 結果が変わるので、同じ文字列でも別物
        var a = new SearchHistoryEntry { Text = "衣装" };
        var b = new SearchHistoryEntry { Text = "衣装", SearchBody = true };

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 表示順が違えば別の検索()
    {
        // 結果の中身は同じだが、戻したい状態の一部
        var a = new SearchHistoryEntry { Text = "衣装", Sort = "新しい順" };
        var b = new SearchHistoryEntry { Text = "衣装", Sort = "高い順" };

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 属性の幅が違えば別の検索()
    {
        var a = new SearchHistoryEntry { Attributes = [new AttributeRange("かわいい", 60, 100)] };
        var b = new SearchHistoryEntry { Attributes = [new AttributeRange("かわいい", 40, 100)] };

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 文字列とカテゴリの境目を取り違えない()
    {
        // 区切りに使える字が本文にも入れられると、この2つが同じ指紋になりうる
        var a = new SearchHistoryEntry { Text = "衣装", Category = "夏" };
        var b = new SearchHistoryEntry { Text = "衣装夏" };

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 全開の属性は条件になっていない()
    {
        Assert.True(new AttributeRange("かわいい", 0, 100).IsOpen);
        Assert.False(new AttributeRange("かわいい", 1, 100).IsOpen);
        Assert.False(new AttributeRange("かわいい", 0, 99).IsOpen);
    }

    [Fact]
    public void 一覧の1行は条件を思い出せる形になる()
    {
        var entry = new SearchHistoryEntry
        {
            Text = "衣装",
            Category = "3D衣装",
            OwnedOnly = true,
            AvatarName = "くうた",
            BoothTags = ["夏"],
            Attributes = [new AttributeRange("かわいい", 60, 100)],
            SearchBody = true,
        };

        Assert.Equal(
            "衣装 / 3D衣装 / 所持のみ / くうた / #夏 / かわいい 60〜100 / 本文も",
            entry.Summary);
    }

    [Fact]
    public void 素体を含むアバターの絞り込みが分かる()
    {
        var entry = new SearchHistoryEntry { AvatarName = "くうた", AvatarHasBase = true };

        Assert.Equal("くうた（素体を含む）", entry.Summary);
    }

    [Fact]
    public void 名前を付けたらそれを出す()
    {
        var entry = new SearchHistoryEntry { Text = "衣装", OwnedOnly = true, Name = "夏物" };

        Assert.Equal("夏物", entry.Summary);
        Assert.True(entry.IsNamed);
    }

    [Fact]
    public void 空白だけの名前は名前として扱わない()
    {
        var entry = new SearchHistoryEntry { Text = "衣装", Name = "  " };

        Assert.False(entry.IsNamed);
        Assert.Equal("衣装", entry.Summary);
    }

    [Fact]
    public void 条件が無ければそう言う()
        => Assert.Equal("条件なし", new SearchHistoryEntry().Summary);
}
