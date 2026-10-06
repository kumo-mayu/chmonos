using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// ViewModel の中の、値から文を作るだけの小さな関数。どれも画面に出る文で、前は画面で読むしかなかった。
/// </summary>
public class SmallTextTests
{
    // ---- アバターの候補の行「名前（商品ID）」----

    [Fact]
    public void アバターの候補は_名前と商品IDを1行にし_読み戻せる()
    {
        var entry = AvatarSuggestionText.Format("作り物のアバター", "1000001");

        Assert.Equal("作り物のアバター（1000001）", entry);
        Assert.Equal("1000001", AvatarSuggestionText.IdOf(entry));
        Assert.Equal("作り物のアバター", AvatarSuggestionText.NameOf(entry));
    }

    [Fact]
    public void 名前に括弧が入っていても_最後の括弧を商品IDとして読む()
    {
        var entry = AvatarSuggestionText.Format("作り物のアバター（改）", "1000001");

        Assert.Equal("1000001", AvatarSuggestionText.IdOf(entry));
        Assert.Equal("作り物のアバター（改）", AvatarSuggestionText.NameOf(entry));
    }

    [Theory]
    [InlineData("作り物のアバター")]
    [InlineData("作り物のアバター（）")]
    [InlineData("作り物のアバター（改）")]
    [InlineData("作り物のアバター（100a001）")]
    [InlineData("")]
    public void 形が違う行は_商品IDとして読まない(string entry)
        // 打ち間違いをそのままIDとして扱わない
        => Assert.Null(AvatarSuggestionText.IdOf(entry));

    [Fact]
    public void 括弧が無い行の名前は_行そのもの()
        => Assert.Equal("作り物のアバター", AvatarSuggestionText.NameOf("作り物のアバター"));

    // ---- カードの下のユーザータグの1行 ----

    private static readonly UserTagAssignment[] Tags =
    [
        new() { Top = "衣装", Subs = ["ワンピース", "夏"] },
        new() { Top = "お気に入り" },
    ];

    [Fact]
    public void ユーザータグの行は_小分類を出すなら大分類と並べる()
        // 小分類だけだと、どの大分類の下の物かがカードから分からない
        => Assert.Equal("衣装：ワンピース・夏 / お気に入り", ItemCardViewModel.UserTagLine(Tags, withSubs: true));

    [Fact]
    public void ユーザータグの行は_小分類を出さないなら大分類だけ()
        => Assert.Equal("衣装 / お気に入り", ItemCardViewModel.UserTagLine(Tags, withSubs: false));

    [Fact]
    public void ユーザータグが無ければ_行は空()
        => Assert.Equal(string.Empty, ItemCardViewModel.UserTagLine([], withSubs: true));

    // ---- unitypackage を選ぶ一覧の行 ----

    [Fact]
    public void unitypackageの行は_名前と_どのzipのどこに入っているかを言う()
    {
        var rows = ItemFileActions.PackageLabels(
        [
            new UnityPackageEntry(@"D:\files\costume.zip", "costume/Costume_v1.unitypackage", 100),
            new UnityPackageEntry(@"D:\files\costume.zip", "Shader.unitypackage", 100),
        ]);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Equal("Costume_v1", row.Label);
                Assert.Equal("costume.zip の中の costume", row.Detail);
            },
            row =>
            {
                // zip の直下にある物は、フォルダを言わない
                Assert.Equal("Shader", row.Label);
                Assert.Equal("costume.zip", row.Detail);
            });
    }

    // ---- 改変の「使ったもの」----

    [Fact]
    public void 使ったもののファイルは_空欄の意味を言い分ける()
    {
        var sent = new ModificationMember { ItemId = "1000001", FileHash = Make.HashOf("a"), Package = "costume/Costume_v1.unitypackage" };
        var sentWithoutName = new ModificationMember { ItemId = "1000001", FileHash = Make.HashOf("a") };
        var byHand = new ModificationMember { ItemId = "1000001" };

        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(@"D:\files\a.zip"));
        var pickedFile = new ModificationMember { ItemId = "1000001", FileHash = Make.HashOf(@"D:\files\a.zip") };

        Assert.Equal("Costume_v1.unitypackage", ModificationRowBuilder.FileTextOf(sent, item));
        // zip を手元の一覧から引けるときは「パッケージ名 (zip名)」（メモ59）
        var sentFromZip = new ModificationMember { ItemId = "1000001", FileHash = Make.HashOf(@"D:\files\a.zip"), Package = "costume/Costume_v1.unitypackage" };
        Assert.Equal("Costume_v1.unitypackage (a.zip)", ModificationRowBuilder.FileTextOf(sentFromZip, item));
        // ファイルだけ記録した行（unitypackage の無いファイルを窓で選んだ・メモ26-②）は、手元のファイルの名前を出す
        Assert.Equal("a.zip", ModificationRowBuilder.FileTextOf(pickedFile, item));
        // 名前を引けないとき（手元の一覧に無い）は、記録があることだけ言う。送ったか選んだかは言い分けない
        Assert.Equal("使ったファイルの記録あり", ModificationRowBuilder.FileTextOf(sentWithoutName, item));
        Assert.Equal("どのファイルを使ったかは分かりません", ModificationRowBuilder.FileTextOf(byHand, item));
    }

    [Fact]
    public void 使ったものの種類は_記録の番号を名前に直す()
    {
        var item = Make.Item("1000001", "作り物の衣装") with
        {
            Booth = new BoothBlock
            {
                Variations = [new BoothVariation { Id = 11, Name = "フルセット" }, new BoothVariation { Id = 12, Name = null }],
            },
        };

        Assert.Equal("フルセット", ModificationViewModel.VariationLabel(new ModificationMember { ItemId = "1000001", VariationId = 11 }, item));
        Assert.Equal(
            DisplayText.VariationName(null),
            ModificationViewModel.VariationLabel(new ModificationMember { ItemId = "1000001", VariationId = 12 }, item));
    }

    [Fact]
    public void 種類を記録していなければ空_BOOTHから消えた種類は分からないと言う()
    {
        var item = Make.Item("1000001", "作り物の衣装");

        Assert.Equal(string.Empty, ModificationViewModel.VariationLabel(new ModificationMember { ItemId = "1000001" }, item));
        Assert.Equal(
            "バリエーションの名前が分かりません",
            ModificationViewModel.VariationLabel(new ModificationMember { ItemId = "1000001", VariationId = 99 }, item));
        Assert.Equal(
            "バリエーションの名前が分かりません",
            ModificationViewModel.VariationLabel(new ModificationMember { ItemId = "1000001", VariationId = 99 }, item: null));
    }

    // ---- Unity のプロジェクトを開いた結果 ----

    [Theory]
    [InlineData(UnityOpenResult.BroughtToFront, "「SampleProject」は既に開いています。そのUnityを手前に出しました。")]
    [InlineData(
        UnityOpenResult.AlreadyOpenNotFront,
        "「SampleProject」は既に開いています。手前に出せなかったので、タスクバーのUnityを押して切り替えてください。")]
    [InlineData(UnityOpenResult.Launched, "「SampleProject」をUnityで開いています。少し時間がかかります。")]
    [InlineData(
        UnityOpenResult.HandedToHub,
        "このプロジェクトのUnityが手元に無いので、Unity Hubに渡しました。Hubが入れるか聞いてくれます。")]
    [InlineData(UnityOpenResult.Missing, "「SampleProject」のフォルダが見つかりません。")]
    public void プロジェクトを開いた結果を_必ず言う(UnityOpenResult result, string expected)
        // 開いていたら手前に出るだけで、何も言わないと何も起きなかったように見える
        => Assert.Equal(expected, UnityOpenText.For(result, "SampleProject", hasHub: true));

    // ---- 商品ページのフォルダの行 ----

    [Fact]
    public void フォルダの行の吹き出しは_在るかで押した先を言い分ける()
    {
        var present = new LocalFolderRow { Path = @"D:\files\costume", Name = "costume", SummaryText = "3 件" };
        var missing = new LocalFolderRow { Path = @"D:\files\costume", Name = "costume", SummaryText = "3 件", IsMissing = true };

        Assert.Equal("D:\\files\\costume\n押すと、エクスプローラでこのフォルダを開きます。", present.PathToolTip);
        Assert.Equal("D:\\files\\costume\nフォルダが見つかりません。押すと近くのフォルダを開きます。", missing.PathToolTip);
    }

    // ---- 商品ページの対応アバターの札 ----

    [Fact]
    public void 対応アバターの吹き出しは_どこから読んだかと_確認済みかを言う()
    {
        var confirmed = new AvatarRow { ItemId = "1000001", Name = "作り物のアバター", SourceText = "商品の説明" };
        var unconfirmedOwned = new AvatarRow
        {
            ItemId = "1000001", Name = "作り物のアバター", SourceText = "商品の名前", IsUnconfirmed = true, IsOwned = true,
        };

        Assert.Equal("商品の説明から読み取りました。押すとこのアバターを開きます", confirmed.SourceTooltip);

        // 所持は色だけに頼らず、言葉でも言う
        Assert.Equal(
            "持っているアバターです。商品の名前から読み取りました（未確認）。押すとこのアバターを開きます",
            unconfirmedOwned.SourceTooltip);
    }

    [Fact]
    public void 手で足したか確認済みにした対応アバターの吹き出しは_読み取ったとは言わず_手で確認したと言う()
    {
        // 確認済みにすると出どころを手入力に付け替える。「手入力から読み取りました」では、確かめた物が読み取った物に聞こえる
        var manual = new AvatarRow { ItemId = "1000001", Name = "作り物のアバター", SourceText = "手入力", IsManual = true };
        Assert.Equal("手で確認しました。押すとこのアバターを開きます", manual.SourceTooltip);
    }

    [Fact]
    public void 共通素体の候補の吹き出しは_一覧にも足すかを言い分ける()
    {
        Assert.Equal("共通素体の一覧とこの商品に追加します。", new BaseMentionRow { Name = "作り物の素体", IsNew = true }.AddTooltip);
        Assert.Equal("この商品の共通素体に追加します。", new BaseMentionRow { Name = "作り物の素体" }.AddTooltip);
    }

    // ---- 商品ページの「使った改変」の行 ----

    [Fact]
    public void 使った改変の行は_同じ商品が複数回入っていれば回数を言う()
    {
        var record = new ModificationRecord { Id = "mod-0000a001", AvatarItemId = "1000001", Name = "夏の改変" };

        var once = new UsedInModificationRowViewModel { Record = record, AvatarText = "作り物のアバター", UseCount = 1 };
        var twice = new UsedInModificationRowViewModel { Record = record, AvatarText = "作り物のアバター", UseCount = 2 };

        Assert.Equal("夏の改変", once.Name);
        Assert.Equal("作り物のアバター", once.Detail);
        Assert.Equal("作り物のアバター　この商品は 2 回入っています", twice.Detail);
    }

    // ---- 検索の選択肢の件数 ----

    [Fact]
    public void 選択肢は_数え終わるまで件数を出さない()
    {
        var option = new ChoiceOption("owned", "所持している");
        Assert.Equal("所持している", option.Display);

        option.Count = 0;
        Assert.Equal("所持している（0）", option.Display);

        option.Count = 12;
        Assert.Equal("所持している（12）", option.Display);
    }
}
