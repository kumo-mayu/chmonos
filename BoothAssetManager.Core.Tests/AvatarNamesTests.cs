using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 名簿の表示名（#54）。商品名は作り物で、実データの型（試験データ392体で測った失敗の型）だけを写している。
/// 実データの数字は experiments/AvatarNameBench が出す。
/// </summary>
public sealed class AvatarNamesTests
{
    [Theory]
    // 名前ではない括弧を飛ばす（以前は「VRChat対応3Dモデル」が名前になっていた）
    [InlineData("【VRChat対応3Dモデル】ミモザ", "ミモザ")]
    [InlineData("【オリジナル3D男性モデル】ハルトくん【VRC】", "ハルトくん")]
    [InlineData("オリジナル3Dモデル【Chika】", "Chika")]
    [InlineData("【無料】Pino ピノ【VRChatアバター】", "Pino ピノ")]
    // 一般的な語だけを名前にしない（以前は「天使」が名前になっていた）
    [InlineData("【3Dmodel】天使ハルカ【VRChat avatar】", "天使ハルカ")]
    // 括られた名前は1文字でも採る。版の表記は落とす
    [InlineData("オリジナル3Dモデル「萌」Ver.1.0", "萌")]
    // タグだけの商品名
    [InlineData("#Tomo3D", "Tomo")]
    // 「/」の後ろは別の語で、読みではない
    [InlineData("クロム/chrome【オリジナル3Dモデル/Mobile対応】", "クロム")]
    [InlineData("ARMA_アルマ/ZEPTO002", "ARMA_アルマ")]
    public void 正式名の形から名前を切り出す(string boothName, string expected)
        => Assert.Equal(expected, AvatarText.DisplayNameFrom(boothName));

    [Theory]
    // 名前が2通りで書かれていれば、先に書かれた方を前に、もう一方を括弧に入れる（ユーザ判断 c）
    [InlineData("ルル -Ruru- / オリジナル3Dモデル", "ルル（Ruru）")]
    [InlineData("Nova - ノヴァ -【オリジナル3Dモデル】", "Nova（ノヴァ）")]
    [InlineData("【オリジナル3Dモデル】Lumi〈ルミ〉", "Lumi（ルミ）")]
    [InlineData("【オリジナル3Dモデル】Sora - ソラ", "Sora（ソラ）")]
    public void 名前が2通りなら両方並べる(string boothName, string expected)
        => Assert.Equal(expected, AvatarText.DisplayNameFrom(boothName));

    [Fact]
    public void 名前の後ろのコラボ先やショップの括弧は読みにしない()
        // 文字の種類が混ざった括弧は、名前のもう一つの書き方ではない
        => Assert.Equal("姫乃", AvatarText.DisplayNameFrom("【VRC用アバター】姫乃【黒猫工房×StudioX】"));

    [Fact]
    public void 長すぎるなら名前だけにする()
        => Assert.Equal("Aquamarine", AvatarText.DisplayNameFrom("Aquamarine -アクアマリンアクアマリンアクアマリン-"));

    [Fact]
    public void 手で付けた名前はそのまま出す()
    {
        var entry = new AvatarRegistryEntry { ItemId = "1", BoothName = "【VRChat対応3Dモデル】ミモザ", DisplayName = "みもちゃん" };

        Assert.Equal("みもちゃん", AvatarNames.ManualName(entry));
        Assert.Equal("みもちゃん", AvatarNames.ShownName(entry));
    }

    [Fact]
    public void 以前の版が自動で付けた名前は手で付けた物とみなさない()
    {
        // 以前の付け方の名前（ここでは括弧の中身）が保存されていても、新しい付け方で出す
        var booth = "【VRChat対応3Dモデル】ミモザ";
        var entry = new AvatarRegistryEntry { ItemId = "1", BoothName = booth, DisplayName = AvatarText.ShortenName(booth) };

        Assert.Null(AvatarNames.ManualName(entry));
        Assert.Equal("ミモザ", AvatarNames.ShownName(entry));
    }

    [Fact]
    public void 付けた時刻があれば以前の自動の名前と同じ形でも手で付けた物()
    {
        // 正式名に含まれる呼び名そのもの（以前の付け方は最短の呼び名を採った）を選んだ人の名前。
        // 時刻が無いと以前の自動の名前と見分けられず、保存しても読むたびに自動の扱いへ戻っていた
        var legacy = new AvatarRegistryEntry
        {
            ItemId = "1",
            BoothName = "【VRChat対応3Dモデル】ミモザ -V2.0",
            Aliases = [new AvatarAlias { Text = "ミモ" }],
            DisplayName = "ミモ",
        };
        Assert.Null(AvatarNames.ManualName(legacy));

        var named = legacy with { DisplayNameSetAt = DateTimeOffset.Now };
        Assert.Equal("ミモ", AvatarNames.ManualName(named));
        Assert.Equal("ミモ", AvatarNames.ShownName(named));
    }

    [Theory]
    // 以前の付け方が選んだ「名前ではない語」。呼び名の一覧が後で変わると、やり直しでは一致しない
    [InlineData("VRChat対応3Dモデル")]
    [InlineData("Mobile対応")]
    [InlineData("VR")]
    [InlineData("男性")]
    [InlineData("標準版")]
    public void 名前ではない語だけの名前は手で付けた物とみなさない(string stored)
    {
        var entry = new AvatarRegistryEntry { ItemId = "1", BoothName = "【VRChat対応3Dモデル】ミモザ【Mobile対応】", DisplayName = stored };

        Assert.Null(AvatarNames.ManualName(entry));
        Assert.Equal("ミモザ", AvatarNames.ShownName(entry));
    }

    [Fact]
    public void 正式名が引けないものは入っていた名前か商品ID()
    {
        Assert.Equal("手掛かりの名前", AvatarNames.ShownName(new AvatarRegistryEntry { ItemId = "9", DisplayName = "手掛かりの名前" }));
        Assert.Equal("9", AvatarNames.ShownName(new AvatarRegistryEntry { ItemId = "9" }));
    }

    [Fact]
    public void 同じ名前が2体以上あればショップ名を付ける()
    {
        var names = AvatarNames.Map(
        [
            new AvatarRegistryEntry { ItemId = "a", BoothName = "オリジナル3Dモデル「レン」", ShopName = "青工房" },
            new AvatarRegistryEntry { ItemId = "b", BoothName = "オリジナル3Dモデル『レン』", ShopName = "赤工房" },
            new AvatarRegistryEntry { ItemId = "c", BoothName = "オリジナル3Dモデル「ミオ」", ShopName = "青工房" },
        ]);

        Assert.Equal("レン（青工房）", names["a"]);
        Assert.Equal("レン（赤工房）", names["b"]);
        // 同じ名前の相手がいなければ付けない
        Assert.Equal("ミオ", names["c"]);
    }

    [Fact]
    public void アバターでない物と名前が重なってもショップ名を付けない()
    {
        // そのアバター向けのテクスチャは名簿にアバターでない物として残り、切り出すと同じ名前になる
        var names = AvatarNames.Map(
        [
            new AvatarRegistryEntry { ItemId = "a", BoothName = "オリジナル3Dモデル「レン」", ShopName = "青工房", Category = "3Dキャラクター" },
            new AvatarRegistryEntry { ItemId = "b", BoothName = "レン / 夕焼けメイク", ShopName = "赤工房", Category = "3Dテクスチャ" },
        ]);

        Assert.Equal("レン", names["a"]);
    }

    [Fact]
    public void ショップ名が分からない物にはそのまま()
    {
        var names = AvatarNames.Map(
        [
            new AvatarRegistryEntry { ItemId = "a", BoothName = "オリジナル3Dモデル「レン」", ShopName = "青工房" },
            new AvatarRegistryEntry { ItemId = "b", BoothName = "オリジナル3Dモデル『レン』" },
        ]);

        Assert.Equal("レン（青工房）", names["a"]);
        Assert.Equal("レン", names["b"]);
    }

    [Fact]
    public void 照合には両方の書き方を渡す()
        => Assert.Equal(["Nova", "ノヴァ"], AvatarNames.Parts("Nova（ノヴァ）"));
}
