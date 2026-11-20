using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「変換扱い」の欄（<c>docs/spec/conversion.md</c>「画面」）。
/// プロファイルの商品は、中に作り物のプロファイル2つ（fakeaoi は順方向、fakebase は逆方向）を持つ。名前・ID は作り物。
/// </summary>
public class ItemConversionSettingTests
{
    private const string ProfileId = "9900871";
    private const string Hash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static readonly ConversionTarget SuggestedBase = new() { BaseName = "作り物の素体", Reverse = true, Slug = "fakebase" };

    private static async Task<ItemViewModel> OpenAsync(TestApp app, ConversionSetting? conversion, bool withFiles = true, bool isAvatar = false)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター", BaseName = "作り物の素体" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体" }],
        });

        if (withFiles)
        {
            new UnityPackagePathStore(app.Services.Paths).Add(Hash, "Profile.unitypackage",
            [
                new UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2fakeaoi.json"),
                new UnityPackageAsset("00000000000000000000000000000002", "Assets/OutfitRetargetingSystem/Editor/config_fakebase2template.json"),
            ]);
        }

        var item = Make.Item(ProfileId, "作り物のプロファイル");
        if (isAvatar)
        {
            // アバター本体の商品は、中のプロファイルのつなぎ先をその商品自身と推す（手掛かり (a)）
            item = item with { Booth = item.Booth! with { Category = new BoothCategory { Id = 1, Name = "3Dキャラクター" } } };
        }

        item = item with
        {
            Local = item.Local with
            {
                LocalFiles = withFiles
                    ?
                    [
                        new LocalFileRecord
                        {
                            Hash = Hash,
                            Paths = [@"D:\fake\profile.zip"],
                            SizeBytes = 1,
                            Contents = ["Profile.unitypackage"],
                            UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                        },
                    ]
                    : [],
                Conversion = conversion,
            },
        };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    private static async Task<ConversionSetting?> SavedAsync(TestApp app)
        => (await app.Store.Items.LoadAsync(ProfileId))!.Local.Conversion;

    [Fact]
    public Task プロファイルを持たない商品には_欄を出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, conversion: null, withFiles: false);

        Assert.False(page.Conversion.ShowsConversion);
        Assert.Empty(page.Conversion.Rows);
    });

    [Fact]
    public Task 中のプロファイルごとに行を出し_推した物は確認待ち_決まっていない物は選ぶ欄() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        Assert.True(page.Conversion.ShowsConversion);
        Assert.Equal("今の扱い：プロファイル（自動の判定）", page.Conversion.StateText);
        Assert.True(page.Conversion.IsAuto);

        var open = Assert.Single(page.Conversion.Rows, row => row.ProfileText == "fakeaoi");
        Assert.False(open.HasTarget);
        Assert.Equal("順方向", open.DirectionText);
        // どの行も同じファイルの物なので、見出しの下に1回だけ出し、行ごとには出さない
        Assert.Equal("入っているファイル：profile.zip の Profile.unitypackage", page.Conversion.CommonSourceText);
        Assert.All(page.Conversion.Rows, row => Assert.False(row.HasSource));

        var suggested = Assert.Single(page.Conversion.Rows, row => row.ProfileText == "fakebase");
        Assert.Equal("作り物の素体（共通素体）", suggested.TargetText);
        Assert.Equal("逆方向", suggested.DirectionText);
        Assert.True(suggested.IsUnconfirmed);

        // 推した理由は商品ページには出さない（アバターの管理の「変換」だけ）
        Assert.All(page.Conversion.Rows, row => Assert.False(row.HasReason));

        Assert.Contains("アオイ", page.Conversion.TargetSuggestions);
        Assert.Contains("作り物の素体（共通素体）", page.Conversion.TargetSuggestions);
    });

    /// <summary>
    /// つなぎ先を選ぶ欄は、商品ページの「対応アバターを追加」の欄と同じ作り（群・絵。ユーザ指示 2026-11-18）。
    /// 共通素体の絵は素体の絵（付けた画像）で、札に乗せた時も同じ絵。仲間のアバターの絵には代えない
    /// </summary>
    [Fact]
    public Task つなぎ先の候補は群に分かれ_共通素体の候補と札には素体の絵を出す() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });
        const string BaseText = "作り物の素体（共通素体）";

        Assert.Equal(AvatarSuggestionText.BaseGroup, page.Conversion.TargetInfoSelector(BaseText)!.Group);
        Assert.Equal(AvatarSuggestionText.OtherGroup, page.Conversion.TargetInfoSelector("アオイ")!.Group);
        Assert.Equal(AvatarSuggestionText.Headings, page.Conversion.TargetGroupHeadings);

        // 画像も素体の商品も無い素体は頭の字（仲間のアバター「アオイ」の絵にもしない）
        Assert.Null(page.Conversion.TargetIconSelector(BaseText));
        Assert.Null(page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").IconFactory!());

        // 札の頭の絵（ユーザ判断 2026-11-20・②）。絵が無ければ頭の字の升で、字は「（共通素体）」を添える前の名前から。
        // つなぎ先の無い行は持たない
        var chip = page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").TargetIcon!;
        Assert.False(chip.HasIcon);
        Assert.Equal(Chmonos.Core.Services.AvatarText.InitialOf("作り物の素体"), chip.Initial);
        Assert.All(page.Conversion.Rows.Where(row => !row.HasTarget), row => Assert.Null(row.TargetIcon));

        await app.Services.Avatars.SetBaseImageAsync("作り物の素体", Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        Assert.NotNull(page.Conversion.TargetIconSelector(BaseText));
        Assert.NotNull(page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").IconFactory!());

        // 読み直しで残した行でも、画像を付けた後は札の絵が出る（頭の字のまま残らない）
        page.Conversion.Show(page.Item);
        Assert.True(page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").TargetIcon!.HasIcon);
    });

    /// <summary>持っているアバター（商品を持つ）は所持の群。商品ページでも手元の商品を引く道を渡している（外れると全部が未所持になる）</summary>
    [Fact]
    public Task つなぎ先の候補で_持っているアバターは所持の群() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9800401", "作り物のアバター「アオイ」"));
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        Assert.Equal(AvatarSuggestionText.OwnedGroup, page.Conversion.TargetInfoSelector("アオイ")!.Group);
    });

    [Fact]
    public Task 確認済みにすると_確認したつなぎ先として保存する() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").ConfirmCommand!.Execute(null);

        await app.SettleAsync();

        Assert.True((await SavedAsync(app))!.Targets.Single().Confirmed);
        Assert.False(page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").IsUnconfirmed);
    });

    /// <summary>
    /// ユーザ判断 2026-11-20・③：プロファイル数百の商品で、1つ確認済みにするたびに全部の行を作り直していた。中身の変わらない行は同じ物のまま残す
    /// （画面は残った行の部品を作り直さない）。変わった行だけ新しくする
    /// </summary>
    [Fact]
    public Task 確認済みにしても_中身の変わらない行は同じ物のまま残る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });
        var rows = page.Conversion.Rows;
        var unset = rows.Single(row => row.ProfileText == "fakeaoi");
        var confirmed = rows.Single(row => row.ProfileText == "fakebase");

        confirmed.ConfirmCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => !page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").IsUnconfirmed, "確認済みが入る");

        Assert.Same(rows, page.Conversion.Rows);
        Assert.Same(unset, page.Conversion.Rows.Single(row => row.ProfileText == "fakeaoi"));
        Assert.NotSame(confirmed, page.Conversion.Rows.Single(row => row.ProfileText == "fakebase"));
    });

    [Fact]
    public Task 選ぶ欄でアバターを選ぶと_そのプロファイルのつなぎ先を確認済みで書く() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, conversion: null);

        page.Conversion.Rows.Single(row => row.ProfileText == "fakeaoi").ChooseCommand!.Execute("アオイ");

        await app.SettleAsync();

        var saved = Assert.Single((await SavedAsync(app))!.Targets);
        Assert.Equal(new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeaoi", Confirmed = true }, saved);
    });

    [Fact]
    public Task 外すと_外した印を付けて残し_その行は選ぶ欄に戻る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").RemoveCommand!.Execute(null);

        await app.SettleAsync();

        Assert.True(Assert.Single((await SavedAsync(app))!.Targets).Rejected);
        var row = Assert.Single(page.Conversion.Rows, row => row.ProfileText == "fakebase");
        Assert.False(row.HasTarget);

        // 外した物は畳んだ一覧に出て、「戻す」で確認済みに戻る
        Assert.Equal("外したつなぎ先（1）", page.Conversion.RejectedHeader);
        var rejected = Assert.Single(page.Conversion.RejectedRows);
        Assert.Equal("作り物の素体（共通素体）", rejected.TargetText);
        rejected.RestoreCommand!.Execute(null);
        await app.SettleAsync();

        var restored = Assert.Single((await SavedAsync(app))!.Targets);
        Assert.False(restored.Rejected);
        Assert.True(restored.Confirmed);
        Assert.False(page.Conversion.HasRejected);
    });

    [Fact]
    public Task 向きを右クリックで変えると_確認済みで保存し_印が移る() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });
        var before = page.Conversion.Rows.Single(row => row.ProfileText == "fakebase");
        Assert.True(before.IsReverse);

        before.SetDirectionCommand!.Execute("both");
        await app.SettleAsync();

        var saved = Assert.Single((await SavedAsync(app))!.Targets);
        Assert.True(saved.Forward && saved.Reverse && saved.Confirmed);
        var after = page.Conversion.Rows.Single(row => row.ProfileText == "fakebase");
        Assert.True(after.IsBoth);
        Assert.Equal("両方向", after.DirectionText);
    });

    [Fact]
    public Task 右クリックの項目は既定の向きに印を付け_変えた時だけ既定に戻すを出す() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });
        var before = page.Conversion.Rows.Single(row => row.ProfileText == "fakebase");

        // fakebase は中のファイルの名前で逆方向
        Assert.Equal(["順方向", "逆方向（既定）", "両方向"], [before.ForwardHeader, before.ReverseHeader, before.BothHeader]);
        Assert.False(before.IsDirectionChanged);

        before.SetDirectionCommand!.Execute("both");
        await app.SettleAsync();
        var changed = page.Conversion.Rows.Single(row => row.ProfileText == "fakebase");
        Assert.True(changed.IsDirectionChanged);
        Assert.Equal("逆方向（既定）", changed.ReverseHeader);

        // 「既定に戻す」は既定の向き（FileDirection）を引数に同じ命令を押す
        changed.SetDirectionCommand!.Execute(changed.FileDirection);
        await app.SettleAsync();

        var saved = Assert.Single((await SavedAsync(app))!.Targets);
        Assert.True(!saved.Forward && saved.Reverse && saved.Confirmed);
        Assert.False(page.Conversion.Rows.Single(row => row.ProfileText == "fakebase").IsDirectionChanged);
    });

    [Fact]
    public Task つなぎ先の無い行は_向きを変えられない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, conversion: null);

        var open = page.Conversion.Rows.Single(row => row.ProfileText == "fakeaoi");
        Assert.False(open.HasTarget);
        Assert.Null(open.SetDirectionCommand);
    });

    [Fact]
    public Task 扱わないにしても_つなぎ先は残し_文で言う() => TestApp.Run(async app =>
    {
        var confirmed = SuggestedBase with { Confirmed = true };
        var page = await OpenAsync(app, new ConversionSetting { Targets = [confirmed] });

        page.Conversion.SetModeCommand.Execute("notProfile");

        await app.SettleAsync();

        Assert.Equal(ConversionMode.NotProfile, (await SavedAsync(app))!.Mode);
        Assert.Equal([confirmed], (await SavedAsync(app))!.Targets);
        Assert.True(page.Conversion.IsNotProfile);
        Assert.Equal("今の扱い：プロファイルとして扱わない（手動で指定）", page.Conversion.StateText);
    });

    [Fact]
    public Task 起動時の見回りが推した値は_検索の写しに届き_開いた商品ページに確認待ちで出る() => TestApp.Run(async app =>
    {
        // 検索は見回りより先に全件を読む。見回りが書いた後に知らせないと、開いたページは推す前の記録で組まれ、全部が選ぶ欄に見えた（実データで 2026-11-09）
        await OpenAsync(app, conversion: null, isAvatar: true);
        var main = app.Main;
        Assert.Null(main.Search.ListItems.Single(card => card.Item.Id == ProfileId).Item.Local.Conversion);

        main.StartMissingMarksSweep();
        await app.SettleAsync();
        await UiThread.Until(
            () => main.Search.ListItems.Single(card => card.Item.Id == ProfileId).Item.Local.Conversion is not null,
            "見回りの推した値が検索の写しに入る");

        var page = new ItemViewModel(main.Search.ListItems.Single(card => card.Item.Id == ProfileId).Item, app.Services, main, main.Thumbnails);
        Assert.All(page.Conversion.Rows, row => Assert.True(row.HasTarget && row.IsUnconfirmed));
        Assert.Equal(2, page.Conversion.Rows.Count);
    });

    [Fact]
    public void 入っているファイルは_unitypackageをそのまま置いた物は名前1つ_何個もあれば1つ目とほかの数()
    {
        Assert.Equal(
            "入っているファイル：Profile.unitypackage",
            ItemConversionEditor.SourceText([new ProfileSource("fake", "Profile.unitypackage", "Profile.unitypackage")]));
        Assert.Equal(
            "入っているファイル：a.zip の A.unitypackage ほか 2 個",
            ItemConversionEditor.SourceText(
            [
                new ProfileSource("fake", "A.unitypackage", "a.zip"),
                new ProfileSource("fake", "B.unitypackage", "a.zip"),
                new ProfileSource("fake", "C.unitypackage", "a.zip"),
            ]));
    }

    [Fact]
    public void 推した理由の文は_FBXの場所の手掛かりも言う()
    {
        var names = new Dictionary<string, string> { ["9800401"] = "アオイ" };

        var text = ItemConversionEditor.CluesText(
            [new ConversionClue(ConversionClueKind.FbxAvatar, "9800401", null), new ConversionClue(ConversionClueKind.DetectedBase, null, "作り物の素体")],
            names);

        Assert.Equal("FBX の場所が「アオイ」と同じ・商品の対応素体「作り物の素体」", text);
    }
}
