using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「変えた項目だけを持ち主として名乗る」の見分け。
/// 変えていない項目まで名乗ると、開いている間に別の画面が書いた値を古い写しで戻す。
/// </summary>
public class LocalFieldsTests
{
    [Fact]
    public void FindsNothingWhenNothingChanged()
    {
        var local = new LocalBlock { Memo = "袖が貫通する", DisplayName = "ニット" };

        Assert.Empty(LocalFields.Changed(local, local with { }, LocalOwners.EditScreen));
    }

    [Fact]
    public void FindsOnlyTheFieldThatChanged()
    {
        var before = new LocalBlock { Memo = "袖が貫通する", DisplayName = "ニット" };
        var after = before with { DisplayName = "ニットセット" };

        Assert.Equal([LocalField.DisplayName], LocalFields.Changed(before, after, LocalOwners.EditScreen));
    }

    /// <summary>一覧や辞書は参照ではなく中身で比べる（同じ中身を組み直しただけで「変えた」にしない）。</summary>
    [Fact]
    public void ComparesListsAndDictionariesByContent()
    {
        var before = new LocalBlock
        {
            UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["トップス"] }],
            Attributes = new Dictionary<string, int> { ["かわいい"] = 82 },
        };

        var same = before with
        {
            UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["トップス"] }],
            Attributes = new Dictionary<string, int> { ["かわいい"] = 82 },
        };

        Assert.Empty(LocalFields.Changed(before, same, LocalOwners.EditScreen));

        var different = before with { Attributes = new Dictionary<string, int> { ["かわいい"] = 90 } };
        Assert.Equal([LocalField.Attributes], LocalFields.Changed(before, different, LocalOwners.EditScreen));
    }

    /// <summary>空にしたのも変更（名乗らないと、消したはずの値が残る）。</summary>
    [Fact]
    public void TreatsClearingAsAChange()
    {
        var before = new LocalBlock { Memo = "袖が貫通する" };

        Assert.Equal([LocalField.Memo], LocalFields.Changed(before, before with { Memo = null }, LocalOwners.EditScreen));
    }

    /// <summary>
    /// 編集画面が変えていないメモは、名乗らないので商品ページで打った字が残る。
    /// （持ち主が2人いる項目で、古い写しが勝っていた道）
    /// </summary>
    [Fact]
    public void KeepsWhatAnotherScreenWroteForFieldsItDidNotChange()
    {
        var opened = new LocalBlock { Memo = "打つ前", DisplayName = "ニット" };
        var edited = opened with { DisplayName = "ニットセット" };

        var owns = LocalFields.Changed(opened, edited, LocalOwners.EditScreen);

        // 保存の直前に読み直した「今の姿」（商品ページが後からメモを書いた）
        var current = opened with { Memo = "打った後" };
        var merged = LocalFields.Merge(current, edited, owns);

        Assert.Equal("打った後", merged.Memo);
        Assert.Equal("ニットセット", merged.DisplayName);
    }
}
