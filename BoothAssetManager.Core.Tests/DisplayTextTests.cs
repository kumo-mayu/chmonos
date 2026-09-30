using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 画面に出す文字の作り方。
///
/// **ここに集めたのは、二箇所が食い違ったから。**容量の整形は7つのViewModelに
/// 写され、購入の種類は「自分用」と「購入」に割れ、分類の組み立ては
/// 商品ページと編集画面で別々に書かれて、編集画面だけが自分で入れた値を
/// 見ていなかった。ViewModelにはテストが無いので、ここへ出して初めて守れる。
/// </summary>
public class DisplayTextTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(11860930560, "11 GB")]
    public void FormatsSizes(long bytes, string expected)
        => Assert.Equal(expected, DisplayText.Size(bytes));

    /// <summary>負の値でも壊れない（計算違いで負になっても画面は出る）。</summary>
    [Fact]
    public void SurvivesANegativeSize()
        => Assert.Equal("0 B", DisplayText.Size(-1));

    /// <summary>
    /// 一時展開の帯の1行。合計が分かるまでは数字を付けず、2つ以上を並べて展開している間は数を言い、
    /// 中止を押した後は片付けを待っていることだけを言う。
    /// </summary>
    [Theory]
    [InlineData(1, 0L, 0L, false, "展開しています…")]
    [InlineData(1, 0L, 2469606195L, false, "展開しています… 0 B / 2.3 GB")]
    [InlineData(1, 126353408L, 2469606195L, false, "展開しています… 120.5 MB / 2.3 GB")]
    [InlineData(2, 1536L, 4096L, false, "2 件のzipを展開しています… 1.5 KB / 4 KB")]
    [InlineData(1, 126353408L, 2469606195L, true, "展開を中止しています…")]
    public void WritesTheUnpackingLine(int count, long done, long total, bool stopping, string expected)
        => Assert.Equal(expected, DisplayText.UnpackingLine(count, done, total, stopping));

    /// <summary>Unity へ送る前の取り出しの1行。数と大きさの書き方は一時展開の帯と同じ。</summary>
    [Fact]
    public void WritesTheUnityExtractingLine()
        => Assert.Equal(
            "2/3：「Body.unitypackage」をzipから取り出しています… 120.5 MB / 2.3 GB",
            DisplayText.UnityExtractingLine(2, 3, "Body.unitypackage", 126353408L, 2469606195L));

    /// <summary>
    /// すぐ終わる取り出しでは、送る帯の文を替えない（替えると読めない速さで入れ替わる）。
    /// 1秒経ち、そこまでの速さで残りも1秒以上掛かりそうなときだけ替える。
    /// </summary>
    [Theory]
    // 1秒経つ前は、どれだけ残っていても替えない
    [InlineData(0, 0L, 1000L, false)]
    [InlineData(999, 1L, 1000L, false)]
    // 1秒経って半分以下なら、残りも1秒以上
    [InlineData(1000, 500L, 1000L, true)]
    [InlineData(1000, 100L, 1000L, true)]
    // 1秒経ったが、もうすぐ終わる（残り約0.25秒）
    [InlineData(1000, 800L, 1000L, false)]
    // 途中で遅くなった：3秒で6割なら残りは2秒
    [InlineData(3000, 600L, 1000L, true)]
    // 1バイトも書けないまま1秒経った
    [InlineData(1000, 0L, 1000L, true)]
    // 書き終えた・大きさが分からない（空のファイル）
    [InlineData(5000, 1000L, 1000L, false)]
    [InlineData(5000, 0L, 0L, false)]
    public void ShowsTheUnityExtractingLineOnlyForSlowOnes(int elapsedMilliseconds, long done, long total, bool expected)
        => Assert.Equal(expected, DisplayText.ShowsUnityExtracting(TimeSpan.FromMilliseconds(elapsedMilliseconds), done, total));

    /// <summary>
    /// 名詞の形はJSONに書いてある語と同じにする。
    /// 画面と保存で語が違うと、同じものだと分からなくなる。
    /// </summary>
    [Theory]
    [InlineData(PurchaseKind.ForSelf, "自分用")]
    [InlineData(PurchaseKind.Received, "貰った")]
    [InlineData(PurchaseKind.Given, "贈った")]
    public void LabelsThePurchaseKindLikeTheJson(PurchaseKind kind, string expected)
        => Assert.Equal(expected, DisplayText.PurchaseKindLabel(kind));

    /// <summary>文の中に入る形は3つとも過去形で揃える（「¥480 で買った」）。</summary>
    [Theory]
    [InlineData(PurchaseKind.ForSelf, "買った")]
    [InlineData(PurchaseKind.Received, "貰った")]
    [InlineData(PurchaseKind.Given, "贈った")]
    public void PutsThePurchaseKindIntoASentence(PurchaseKind kind, string expected)
        => Assert.Equal(expected, DisplayText.PurchaseKindVerb(kind));

    /// <summary>
    /// バリエーションの行の名前。**内部の言葉を漏らさない。**
    /// 以前は <c>variation 900</c> を返していて、英語の内部トークンが画面に出ていた。
    /// 番号は人が読んで意味が取れないので、名前が引けないことだけを言う。
    /// </summary>
    [Fact]
    public void NamesTheVariationRow()
    {
        Assert.Equal("名前の分からないバリエーション", DisplayText.VariationLabel(900));
        Assert.Equal("バリエーションを選ばない購入", DisplayText.VariationLabel(null));

        // 番号を漏らさない。どの番号でも同じ文になる
        Assert.Equal(DisplayText.VariationLabel(900), DisplayText.VariationLabel(12345));
    }

    /// <summary>
    /// BOOTH が名前を持たせていないバリエーションは「バリエーション選択なし」と呼ぶ（ユーザ判断 2026-09-29）。
    /// 種類を指さない購入の行とは別の言い方のままにする（混ぜると、どちらの行か分からなくなる）
    /// </summary>
    [Fact]
    public void CallsAnUnnamedVariationNoVariation()
    {
        Assert.Equal("バリエーション選択なし", DisplayText.VariationName(null));
        Assert.Equal("バリエーション選択なし", DisplayText.VariationName(""));
        Assert.Equal("バリエーション選択なし", DisplayText.VariationName("  "));
        Assert.Equal("Aセット", DisplayText.VariationName("Aセット"));
        Assert.NotEqual(DisplayText.VariationName(null), DisplayText.VariationLabel(null));
    }

    // ---- ユーザ入力を優先する決まり ----

    [Fact]
    public void PrefersWhatTheUserEntered()
    {
        Assert.Equal("じぶんの名前", DisplayText.Prefer("じぶんの名前", "BOOTHの名前"));
        Assert.Equal("BOOTHの名前", DisplayText.Prefer(null, "BOOTHの名前"));
        Assert.Null(DisplayText.Prefer(null, null));
    }

    /// <summary>空白だけの入力は「入れていない」。空欄にすれば観測へ戻れる。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TreatsBlankInputAsNotEntered(string blank)
        => Assert.Equal("BOOTHの名前", DisplayText.Prefer(blank, "BOOTHの名前"));

    [Fact]
    public void TrimsWhatTheUserEntered()
        => Assert.Equal("とりさん", DisplayText.Prefer("  とりさん  ", "BOOTHの名前"));

    /// <summary>名前は最後に商品IDへ落ちる。名前の場所を空にしない。</summary>
    [Fact]
    public void FallsBackToTheItemId()
    {
        Assert.Equal("とりさん", DisplayText.ItemName("とりさん", "鳥", "5927710"));
        Assert.Equal("鳥", DisplayText.ItemName(null, "鳥", "5927710"));
        Assert.Equal("local-3f9c1b7e", DisplayText.ItemName(null, null, "local-3f9c1b7e"));
        Assert.Equal("local-3f9c1b7e", DisplayText.ItemName("  ", null, "local-3f9c1b7e"));
    }

    [Fact]
    public void BuildsTheCategoryText()
    {
        Assert.Equal("3Dモデル / 3D衣装", DisplayText.CategoryText("3D衣装", "3Dモデル"));
        Assert.Equal("3D衣装", DisplayText.CategoryText("3D衣装", null));
        Assert.Equal(string.Empty, DisplayText.CategoryText(null, "3Dモデル"));
    }

    // ---- 商品ページと編集画面が同じ式を使っていること ----

    /// <summary>
    /// **これが今回のリファクタの目的。**同じ商品に対して、
    /// 保存済みの値から作っても、入力欄の値から作っても、同じ文字列になる。
    /// </summary>
    [Fact]
    public void GivesTheSameCategoryTextFromBothSides()
    {
        var table = new CategoryTable(
            Path.Combine(AppContext.BaseDirectory, "assets", "booth-categories.json"));

        var observed = new BoothCategory { Id = 209, Name = "3D衣装", ParentName = "3Dモデル" };

        // 商品ページ側（保存済みの Local.Category）と編集画面側（入力欄）
        Assert.Equal(table.TextFor("3Dキャラクター", observed), table.TextFor("3Dキャラクター", observed));
        Assert.Equal("3Dモデル / 3Dキャラクター", table.TextFor("3Dキャラクター", observed));

        // 空欄にすれば、どちらもBOOTHの観測へ戻る
        Assert.Equal("3Dモデル / 3D衣装", table.TextFor(null, observed));
        Assert.Equal("3Dモデル / 3D衣装", table.TextFor("   ", observed));
    }

    /// <summary>商品の表示名は、記録から作っても同じ式を通る。</summary>
    [Fact]
    public void UsesTheSameRuleOnTheRecord()
    {
        var item = new ItemRecord
        {
            Id = "5927710",
            Booth = new BoothBlock { Name = "鳥" },
            Local = new LocalBlock { DisplayName = "とりさん" },
        };

        Assert.Equal(DisplayText.ItemName("とりさん", "鳥", "5927710"), item.DisplayName);

        var cleared = item with { Local = item.Local with { DisplayName = null } };
        Assert.Equal("鳥", cleared.DisplayName);
    }
}
