using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの管理の「変換」（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ判断 2026-11-09）。
/// プロファイルの商品4つ：推した（確認待ち）／決めた／扱わない／つなぎ先が無い。名前・ID は作り物。
/// </summary>
public class AvatarsConversionTests
{
    private static readonly ConversionTarget SuggestedA = new() { AvatarItemId = "9800401", Forward = true, Slug = "fakea" };

    private static async Task SaveProfileAsync(TestApp app, string id, string name, string slug, ConversionSetting? conversion)
    {
        var hash = new string(id[^1], 64);
        new UnityPackagePathStore(app.Services.Paths).Add(hash, "Profile.unitypackage",
        [
            new UnityPackageAsset("00000000000000000000000000000001", $"Assets/OutfitRetargetingSystem/Editor/config_template2{slug}.json"),
        ]);
        var item = Make.Item(id, name);
        await app.AddItemAsync(item with
        {
            Local = item.Local with
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [$@"D:\fake\{id}.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
                Conversion = conversion,
            },
        });
    }

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター" }],
        });
        await SaveProfileAsync(app, "9900861", "作り物のプロファイルA", "fakea", new ConversionSetting { Targets = [SuggestedA] });
        await SaveProfileAsync(app, "9900862", "作り物のプロファイルB", "fakeb",
            new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeb", Confirmed = true }] });
        await SaveProfileAsync(app, "9900863", "作り物のプロファイルC", "fakec", new ConversionSetting { Mode = ConversionMode.NotProfile });
        await SaveProfileAsync(app, "9900864", "作り物のプロファイルD", "faked", conversion: null);

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        return avatars;
    }

    private static IReadOnlyList<ConversionItemRowViewModel> Rows(AvatarsViewModel avatars)
        => avatars.ConversionItems.Cast<ConversionItemRowViewModel>().ToList();

    private static ConversionItemRowViewModel Row(AvatarsViewModel avatars, string id)
        => Rows(avatars).Single(row => row.ItemId == id);

    [Fact]
    public Task 決めていない物を上に群で分け_決めていない数を添える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        Assert.Equal("変換（4）", avatars.ConversionModeText);
        Assert.Equal(["9900861", "9900864", "9900862", "9900863"], Rows(avatars).Select(row => row.ItemId));
        Assert.Equal(["決めていない", "決めていない", "決めた", "扱わない"], Rows(avatars).Select(row => row.Group));
        Assert.Equal("確認待ち 1", Row(avatars, "9900861").BadgeText);
        Assert.Equal("未設定 1", Row(avatars, "9900864").BadgeText);
        Assert.False(Row(avatars, "9900862").HasBadge);
    });

    [Fact]
    public Task 変換を見ると_決めていない最初の商品を選び_アバターの詳細は出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.ShowConversionModeCommand.Execute(null);

        Assert.True(avatars.IsConversionMode);
        Assert.False(avatars.IsAvatarMode);
        Assert.False(avatars.ShowsAvatarDetail);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
        Assert.True(avatars.ShowsConversionDetail);
        Assert.True(Assert.Single(avatars.ConversionEditor.Rows).IsUnconfirmed);

        avatars.ShowAvatarModeCommand.Execute(null);
        Assert.False(avatars.IsConversionMode);
        Assert.True(avatars.IsAvatarMode);
    });

    [Fact]
    public Task 推した通りに決めると_確認済みで保存し_決めた群へ移って_次の決めていない商品へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        avatars.DecideSuggestedCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!;
        Assert.True(Assert.Single(saved.Targets).Confirmed);
        Assert.Equal("決めた", Row(avatars, "9900861").Group);
        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);

        // 検索の写しにも入る（衣装の商品ページの変換経由に使われる）
        Assert.True(app.Main.Search.FindItem("9900861")!.Local.Conversion!.Targets.Single().Confirmed);
    });

    [Fact]
    public Task プロファイルではないにすると_扱わないで保存して次へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.SelectedConversion = Row(avatars, "9900864");

        avatars.MarkNotProfileCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(ConversionMode.NotProfile, (await app.Store.Items.LoadAsync("9900864"))!.Local.Conversion!.Mode);
        Assert.Equal("扱わない", Row(avatars, "9900864").Group);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
    });

    [Fact]
    public Task まとめて決めると_窓でチェックを残した物だけを確認済みにする() => TestApp.Run(async app =>
    {
        // 推したつなぎ先が2つの商品を足す（片方のチェックを外す）
        await SaveProfileAsync(app, "9900865", "作り物のプロファイルE", "fakee", new ConversionSetting
        {
            Targets =
            [
                new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakee" },
                new ConversionTarget { AvatarItemId = "9800401", Reverse = true, Slug = "fakee2" },
            ],
        });
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.Equal("推した通りにまとめて決める（3 件）", avatars.BulkText);

        ConfirmConversionsDialogViewModel? shown = null;
        ConfirmConversionsDialogViewModel.Intercept = model =>
        {
            shown = model;
            model.Sections.Single(section => section.ItemId == "9900865").Rows.Single(row => row.Target.Slug == "fakee2").IsChecked = false;
            return true;
        };
        try
        {
            avatars.BulkConfirmCommand.Execute(null);
            await app.SettleAsync();
        }
        finally
        {
            ConfirmConversionsDialogViewModel.Intercept = null;
        }

        // 窓には扱わない商品を出さず、最初は全部にチェックが付いている
        Assert.Equal(["9900861", "9900865"], shown!.Sections.Select(section => section.ItemId).Order());
        Assert.True((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
        var e = (await app.Store.Items.LoadAsync("9900865"))!.Local.Conversion!.Targets;
        Assert.Equal([true, false], e.Select(target => target.Confirmed));
        Assert.Equal("推した通りにまとめて決める（1 件）", avatars.BulkText);
    });

    [Fact]
    public Task まとめて決める窓でキャンセルすると_何も書かない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        ConfirmConversionsDialogViewModel.Intercept = _ => false;
        try
        {
            avatars.BulkConfirmCommand.Execute(null);
            await app.SettleAsync();
        }
        finally
        {
            ConfirmConversionsDialogViewModel.Intercept = null;
        }

        Assert.False((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
    });

    [Fact]
    public void まとめて決める窓は_チェックが無いと押せず理由を言う()
    {
        var row = new ConfirmConversionsRow { Target = SuggestedA, Text = "fakea（順方向）→ アオイ" };
        var model = new ConfirmConversionsDialogViewModel([new ConfirmConversionsSection { ItemId = "9900861", Name = "作り物", Rows = [row] }]);
        Assert.Equal("確認済みにする（1 件）", model.CommitText);

        row.IsChecked = false;

        Assert.False(model.CanCommit);
        Assert.Equal("チェックが1つもありません。", model.CommitHint);
    }

    [Fact]
    public Task 確認待ちが無い商品では_推した通りに決めて次へは押せない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.True(avatars.DecideSuggestedCommand.CanExecute(null));

        avatars.SelectedConversion = Row(avatars, "9900864");

        Assert.False(avatars.DecideSuggestedCommand.CanExecute(null));
        Assert.True(avatars.LaterCommand.CanExecute(null));
    });

    [Fact]
    public Task 推した理由を行の下に出し_推せなかった行には推していない訳を出す() => TestApp.Run(async app =>
    {
        // 商品の対応素体から推したつなぎ先（見回りと同じ推し方で、手掛かりが1つ）
        await SaveProfileAsync(app, "9900866", "作り物のプロファイルF", "fakef", new ConversionSetting
        {
            Targets = [new ConversionTarget { BaseName = "作り物の素体", Forward = true, Slug = "fakef" }],
        });
        var item = (await app.Store.Items.LoadAsync("9900866"))!;
        await app.Store.Items.SaveAsync(item with
        {
            Local = item.Local with { AvatarBases = [new AvatarBaseLink { BaseName = "作り物の素体", Confirmed = true }] },
        });
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        avatars.SelectedConversion = Row(avatars, "9900866");
        Assert.Equal("推した理由：商品の対応素体「作り物の素体」", Assert.Single(avatars.ConversionEditor.Rows).ReasonText);

        // 外すと選ぶ欄に戻り、推せる手掛かりはあるが外した、と言う（見回りが推し直さない訳）
        Assert.Single(avatars.ConversionEditor.Rows).RemoveCommand!.Execute(null);
        await app.SettleAsync();
        Assert.Equal(
            "外したつなぎ先：作り物の素体（共通素体）・推した理由：商品の対応素体「作り物の素体」",
            Assert.Single(avatars.ConversionEditor.Rows).ReasonText);

        avatars.SelectedConversion = Row(avatars, "9900864");
        Assert.Equal("推していません：推す元になる情報がありません", Assert.Single(avatars.ConversionEditor.Rows).ReasonText);
    });

    [Fact]
    public Task 後では_何も書かずに次の決めていない商品へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        avatars.LaterCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);
        Assert.False((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
    });

    [Fact]
    public Task BOOTHで開くは_選んだ商品があるときだけ押せる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.SelectedConversion = null;
        Assert.False(avatars.OpenConversionBoothCommand.CanExecute(null));

        avatars.SelectedConversion = Row(avatars, "9900862");

        Assert.True(avatars.OpenConversionBoothCommand.CanExecute(null));
    });

    /// <summary>ユーザ指示 2026-11-09：変換から商品ページへ行って戻ると、アバターの見方で開き直していた。</summary>
    [Fact]
    public Task 変換から商品ページを開いて戻ると_変換の見方で同じ商品を選んで開き直す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.SelectedConversion = Row(avatars, "9900862");

        avatars.OpenConversionItemCommand.Execute(null);
        await UiThread.Until(() => app.Main.CurrentViewModel is ItemViewModel, "商品ページが開く");
        Assert.Equal("← アバターの管理に戻る", app.Main.BackButtonText);

        app.Main.GoBack();
        var restored = Assert.IsType<AvatarsViewModel>(app.Main.CurrentViewModel);
        Assert.True(restored.IsConversionMode);
        Assert.False(restored.ShowsAvatarDetail);
        await UiThread.Until(() => restored.SelectedConversion is not null, "変換の一覧ができる");

        Assert.True(restored.IsConversionMode);
        Assert.Equal("9900862", restored.SelectedConversion!.ItemId);
    });

    /// <summary>
    /// 保存に失敗したら次の商品へ進まない（件16）。進むと、失敗の知らせを置いた商品から離れ、
    /// 決めていないのに決めたように見える。書き込みだけを止める（読むのは通る）ので、画面の読み直しも通る。
    /// </summary>
    [Fact]
    public Task 保存に失敗したら_次の商品へ進まず_失敗を言い_直ったらそのまま決められる() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);

        using (new System.IO.FileStream(app.Services.Paths.ItemFile("9900861"), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
        {
            avatars.DecideSuggestedCommand.Execute(null);
            await UiThread.Until(() => avatars.ConversionEditor.Notice.HasText, "失敗が欄の下に出る");
        }

        await app.SettleAsync();
        Assert.True(avatars.ConversionEditor.Notice.IsWarning);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
        Assert.Equal("決めていない", Row(avatars, "9900861").Group);
        Assert.False((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);

        // 書き込みが通るようになれば、同じ商品でそのまま決められ、次へ進む
        avatars.DecideSuggestedCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("決めた", Row(avatars, "9900861").Group);
        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);
    });

    [Fact]
    public Task プロファイルではないにするの保存に失敗したら_次の商品へ進まない() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.SelectedConversion = Row(avatars, "9900864");

        using (new System.IO.FileStream(app.Services.Paths.ItemFile("9900864"), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
        {
            avatars.MarkNotProfileCommand.Execute(null);
            await UiThread.Until(() => avatars.ConversionEditor.Notice.HasText, "失敗が欄の下に出る");
        }

        await app.SettleAsync();
        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);
        Assert.Equal("決めていない", Row(avatars, "9900864").Group);
    });

    /// <summary>保存を待つ間に人が別の商品を選んでいたら、書き終えても次へ動かさない（人の選びを尊重する）。</summary>
    [Fact]
    public Task 保存を待つ間に別の商品を選び直したら_書き終えても動かさない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
        var other = Row(avatars, "9900862");

        // 書いた直後（置き場の Written）に、人が別の商品を選んだ状態にする。画面の更新は画面のスレッドへ回す
        void Reselect(string id)
        {
            if (id == "9900861")
            {
                // 書き終えの続きより前に必ず選び直す（後から回すと順が入れ替わり、守りが無くても通ってしまう）
                var dispatcher = System.Windows.Application.Current.Dispatcher;
                if (dispatcher.CheckAccess())
                {
                    avatars.SelectedConversion = other;
                }
                else
                {
                    dispatcher.Invoke(() => avatars.SelectedConversion = other);
                }
            }
        }

        app.Store.Items.Written += Reselect;
        try
        {
            avatars.DecideSuggestedCommand.Execute(null);
            await UiThread.Until(() => Row(avatars, "9900861").Group == "決めた", "決めた群へ移る");
            await app.SettleAsync();
        }
        finally
        {
            app.Store.Items.Written -= Reselect;
        }

        Assert.Equal("9900862", avatars.SelectedConversion!.ItemId);
    });

    /// <summary>
    /// 推した理由の FBX の索引は、選ぶたびに全部のアバターの中身の控えを画面のスレッドで読み直していた（2026-11-13 の Codex の点検の件3）。
    /// 一覧を作る裏で1回読み、選び直しでは読まない。
    /// </summary>
    [Fact]
    public Task 選び直しても_アバターの中身の控えを読み直さない() => TestApp.Run(async app =>
    {
        const string avatarHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        new UnityPackagePathStore(app.Services.Paths).Add(avatarHash, "Avatar.unitypackage",
            [new UnityPackageAsset("00000000000000000000000000000009", "Assets/Fake/Aoi/Aoi.fbx")]);
        var avatar = Make.Item("9800401", "作り物のアバター「アオイ」");
        await app.AddItemAsync(avatar with
        {
            Local = avatar.Local with
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = avatarHash,
                        Paths = [@"D:\fake\9800401.zip"],
                        SizeBytes = 1,
                        UnityPackages = [new UnityPackageSummary { Entry = "Avatar.unitypackage", Roots = ["Assets/Fake"] }],
                    },
                ],
            },
        });

        // まだ何も選んでいない（変換の見方に切り替える前）うちに、一覧を作る裏で読んである
        var avatars = await OpenAsync(app);
        Assert.Null(avatars.SelectedConversion);
        Assert.True(app.Services.Conversion.FbxLoads >= 1, "一覧を作る裏で先に読む");

        avatars.ShowConversionModeCommand.Execute(null);
        await app.SettleAsync();
        var loads = app.Services.Conversion.FbxLoads;

        avatars.SelectedConversion = Row(avatars, "9900862");
        avatars.SelectedConversion = Row(avatars, "9900864");
        avatars.SelectedConversion = Row(avatars, "9900861");

        Assert.Equal(loads, app.Services.Conversion.FbxLoads);
    });
}
