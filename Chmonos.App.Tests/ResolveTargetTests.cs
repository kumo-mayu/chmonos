using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の右の欄の「今の対象」（メモ22・ユーザ判断 2026-10-03「案1」）。
/// 対象は上の帯の1か所で見せて切り替え、「このIDで登録」「この名前で登録する」「除外する」はどれも今の対象に効く。
/// 前は対象の種類ごとに別のボタン（まとめ操作の枠・その他へ飛ぶボタン・「このファイルだけを扱う」のチェック）が並んでいた。
/// </summary>
public class ResolveTargetTests
{
    private static UnresolvedFile Unresolved(string path, string? originZip = null) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        ZoneReferrerUrl = originZip,
    };

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenAsync(TestApp app, params UnresolvedFile[] files)
    {
        await app.Store.Unresolved.SaveAsync([.. files]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.True(resolve.IsLoaded);
        return (main, resolve);
    }

    /// <summary>元のzipを消した（ディスクに無い）展開物3件と、ほかのzip1件。中身を選ぶとzipの単位（束）が立つ。</summary>
    private static Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenBundleAsync(TestApp app)
    {
        var zip = Path.Combine(app.Root, "files", "dl", "costume_set.zip");
        return OpenAsync(app,
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.unitypackage"), zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.psd"), zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\readme.txt"), zip),
            Unresolved(app.NewFile(@"other\shoes.zip")));
    }

    private static UnresolvedRow Row(ResolveViewModel resolve, string fileName) => resolve.Files.Single(row => row.FileName == fileName);

    private static async Task PreviewAsync(TestApp app, ResolveViewModel resolve, string itemId)
    {
        resolve.ItemIdInput = itemId;
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(resolve.HasPreview);
    }

    private static string[] Names(IEnumerable<UnresolvedFile> files) => Names(files.Select(file => file.Paths[0]));

    private static string[] Names(IEnumerable<ExcludedEntry> entries) => Names(entries.Select(entry => entry.Paths[0]));

    private static string[] Names(IEnumerable<string> paths) => [.. paths.Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    // ---- 対象の文と切り替えの出し分け ----

    [Fact]
    public Task 対象が1件なら_帯にファイル名を出し_切り替えは無く_まとめて扱う方法を1行言う() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenAsync(app, Unresolved(app.NewFile(@"a\gift.zip")), Unresolved(app.NewFile(@"b\other.zip")));
        resolve.Selected = Row(resolve, "gift.zip");

        Assert.Equal("gift.zip", resolve.TargetText);
        Assert.False(resolve.HasUnitChoice);
        Assert.False(resolve.HasOriginZipChoice);
        Assert.False(resolve.IsCheckedTarget);
        Assert.True(resolve.IsSingleRowTarget);
        Assert.Equal(string.Empty, resolve.BandNoticeText);
    });

    [Fact]
    public Task zipの中身は_束の件数を帯に出し_このファイルだけに切り替えると1件になる() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");

        Assert.Equal("元zip「costume_set.zip」の中身 3 件", resolve.TargetText);
        Assert.True(resolve.HasUnitChoice);
        Assert.Equal("zipの中身 3 件", resolve.UnitChoiceText);
        Assert.True(resolve.IsUnitTarget);
        Assert.False(resolve.IsSingleRowTarget);

        // 帯の「このファイルだけ」（前は「商品IDを決める」の下のチェックボックス）
        resolve.IsUnitTarget = false;
        Assert.True(resolve.SingleFileOnly);
        Assert.Equal("costume.psd", resolve.TargetText);
        Assert.True(resolve.HasUnitChoice);

        // 選び直すと束に戻る（前と同じ）
        resolve.Selected = Row(resolve, "readme.txt");
        Assert.True(resolve.IsUnitTarget);
        Assert.Equal("元zip「costume_set.zip」の中身 3 件", resolve.TargetText);
    });

    [Fact]
    public Task チェックした物があれば_帯はその件数になり_束の切り替えは出さず_選択を解除で戻る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");

        Row(resolve, "shoes.zip").IsSelected = true;
        Row(resolve, "readme.txt").IsSelected = true;

        Assert.Equal("選択した 2 件", resolve.TargetText);
        Assert.True(resolve.IsCheckedTarget);
        Assert.False(resolve.HasUnitChoice);
        Assert.False(resolve.IsSingleRowTarget);

        resolve.ClearChecksCommand.Execute(null);
        Assert.False(resolve.IsCheckedTarget);
        Assert.Equal("元zip「costume_set.zip」の中身 3 件", resolve.TargetText);
    });

    [Fact]
    public Task 元のzipが残る中身は_帯の右端に元zipとして扱うを出し_未確定にあれば登録を止めて理由を帯に出す() => TestApp.Run(async app =>
    {
        var zip = app.NewFile(@"dl\costume_set.zip");
        var (_, resolve) = await OpenAsync(app,
            Unresolved(zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.unitypackage"), zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.psd"), zip));
        app.Booth.HasItem("1000001", "作り物の衣装");
        resolve.Selected = Row(resolve, "costume.psd");
        await PreviewAsync(app, resolve, "1000001");

        // 左の一覧の「元zipとして扱う」も、元のzipが一覧にあるときだけ出す
        Assert.True(Row(resolve, "costume.psd").CanTreatAsOriginZip);
        Assert.True(resolve.HasOriginZipChoice);
        Assert.True(resolve.IsTargetBlocked);
        Assert.Equal(resolve.BlockedByZipText, resolve.BandNoticeText);
        Assert.True(resolve.IsBandNoticeWarning);
        Assert.False(resolve.AssignCommand.CanExecute(null));
        Assert.False(resolve.RegisterLocalCommand.CanExecute(null));

        // このファイルだけなら止めない
        resolve.SingleFileOnly = true;
        Assert.False(resolve.IsTargetBlocked);
        Assert.Equal(string.Empty, resolve.BandNoticeText);
        Assert.True(resolve.AssignCommand.CanExecute(null));

        // 元zipとして扱うと、一覧のzipの行を選ぶ（右の欄の対象がzipになる）
        resolve.UseOriginZipCommand.Execute(null);
        Assert.Equal("costume_set.zip", resolve.Selected?.FileName);
        Assert.Equal("costume_set.zip", resolve.TargetText);
    });

    [Fact]
    public Task 元のzipが一覧に無いのに元zipとして扱うを押すと_押した帯の中で答え_画面を送らない() => TestApp.Run(async app =>
    {
        // zip はディスクにあるが未確定に無い（取り込んでいない）
        var zip = app.NewFile(@"dl\costume_set.zip");
        var (_, resolve) = await OpenAsync(app,
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.unitypackage"), zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.psd"), zip));
        resolve.Selected = Row(resolve, "costume.psd");

        // ボタンは出さない（押しても選ぶ先が無い）。帯の文で言う
        Assert.False(Row(resolve, "costume.psd").CanTreatAsOriginZip);
        Assert.False(Row(resolve, "costume.unitypackage").CanTreatAsOriginZip);
        // 一覧に無い旨の常時の文は注意の色にしない（押した答えの警告だけが注意の色）
        Assert.False(resolve.IsBandNoticeWarning);
        Assert.False(resolve.HasOriginZipChoice);
        Assert.True(resolve.IsOriginZipUnlisted);
        Assert.Equal("展開元のzip「costume_set.zip」は発見できませんでした。", resolve.BandNoticeText);
        var sent = 0;
        resolve.DecisionFocusRequested += () => sent++;

        resolve.UseOriginZipCommand.Execute(null);

        Assert.Equal("展開元のzip「costume_set.zip」は発見できませんでした。", resolve.BandNoticeText);
        Assert.True(resolve.IsBandNoticeWarning);
        Assert.Equal(string.Empty, resolve.StatusText);
        Assert.Equal(0, sent);
    });

    // ---- 各ボタンが今の対象に効く ----

    [Fact]
    public Task 束を対象に_このIDで登録すると_中身全件を1つの商品にし_結果は一覧の見出しの近くに出す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");
        await PreviewAsync(app, resolve, "1000001");
        Assert.Equal("登録すると：この商品を新しく作って、元zip「costume_set.zip」の中身 3 件を紐付けます", resolve.AssignOutcomeText);

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal(3, item!.Local.LocalFiles.Count);
        Assert.Equal(["shoes.zip"], Names(app.Store.Unresolved.Load()));
        Assert.Equal("3 件を登録しました。", resolve.ListNoticeText);
        Assert.Equal(string.Empty, resolve.StatusText);

        // 束と1件は窓で聞かない（帯と「登録すると：」の行で件数を見て押す。キーで1件ずつ片付ける流れを止めない）
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task このファイルだけを対象に_このIDで登録すると_その1件だけを登録する() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");
        resolve.SingleFileOnly = true;
        await PreviewAsync(app, resolve, "1000001");

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal("costume.psd", Path.GetFileName(Assert.Single(item!.Local.LocalFiles).Paths[0]));
        Assert.Equal(3, app.Store.Unresolved.Load().Count);
    });

    [Fact]
    public Task 束を対象に_BOOTHに無い商品として登録すると_中身全件を1回の命令で1つの仮の商品にする() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");
        resolve.LocalNameInput = "作り物の衣装";
        var lead = resolve.Selected!.File.Hash;
        app.Answer = _ => MessageBoxResult.OK;

        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        Assert.StartsWith("元zip「costume_set.zip」の中身 3 件 を「作り物の衣装」として登録します。", Assert.Single(app.Notices).Text, StringComparison.Ordinal);
        var item = await app.Store.Items.LoadAsync(LocalItemId.For(lead));
        Assert.Equal(3, item!.Local.LocalFiles.Count);
        Assert.Equal(["shoes.zip"], Names(app.Store.Unresolved.Load()));
        Assert.Equal("3 件を登録しました。", resolve.ListNoticeText);
    });

    [Fact]
    public Task 束を対象に_除外すると_中身全件を外す() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");
        app.Answer = _ => MessageBoxResult.OK;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["costume.psd", "costume.unitypackage", "readme.txt"], Names(app.Store.Excluded.Load()));
        Assert.Equal("3 件を管理対象から除外しました。", resolve.ListNoticeText);
    });

    // ---- チェックした物：登録はzipの単位へ広げ、除外は広げない ----

    [Fact]
    public Task チェックした物を_このIDで登録すると_zipの中身はzipの単位まで広げ_窓で聞いてから登録する() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "shoes.zip");
        Row(resolve, "costume.psd").IsSelected = true;
        Row(resolve, "shoes.zip").IsSelected = true;
        await PreviewAsync(app, resolve, "1000001");
        Assert.True(resolve.AssignCommand.CanExecute(null));

        app.Answer = _ => MessageBoxResult.OK;
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var asked = Assert.Single(app.Notices);
        Assert.Equal("このIDで登録", asked.Caption);
        Assert.StartsWith("選択した 4 件 を「作り物の衣装」（ID 1000001）のファイルとして登録します。", asked.Text, StringComparison.Ordinal);
        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal(4, item!.Local.LocalFiles.Count);
        Assert.Empty(app.Store.Unresolved.Load());
        Assert.Equal("4 件を登録しました。", resolve.ListNoticeText);
    });

    [Fact]
    public Task チェックした物を_除外すると_チェックした物だけを外し_zipの仲間は残す() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenBundleAsync(app);
        resolve.Selected = Row(resolve, "costume.psd");
        Row(resolve, "costume.psd").IsSelected = true;
        Row(resolve, "shoes.zip").IsSelected = true;
        app.Answer = _ => MessageBoxResult.OK;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("管理対象から除外する", Assert.Single(app.Notices).Caption);
        Assert.Equal(2, app.Store.Excluded.Load().Count);
        Assert.Equal(["costume.unitypackage", "readme.txt"], Names(app.Store.Unresolved.Load()));
        Assert.Equal("2 件を管理対象から除外しました。", resolve.ListNoticeText);
    });

    [Fact]
    public Task 行の右クリックの除外は_チェックがあっても右クリックした行に効く() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenAsync(app,
            Unresolved(app.NewFile(@"a\first.zip")), Unresolved(app.NewFile(@"b\second.zip")), Unresolved(app.NewFile(@"c\third.zip")));
        Row(resolve, "first.zip").IsSelected = true;
        Row(resolve, "second.zip").IsSelected = true;
        resolve.Selected = Row(resolve, "third.zip");
        app.Answer = _ => MessageBoxResult.OK;

        resolve.ExcludeRowCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(["first.zip", "second.zip"], Names(app.Store.Unresolved.Load()));
    });

    [Fact]
    public Task チェックに元のzipが未確定にある中身が混ざれば_登録を止めて欄の下で理由を言う() => TestApp.Run(async app =>
    {
        var zip = app.NewFile(@"dl\costume_set.zip");
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenAsync(app,
            Unresolved(zip),
            Unresolved(app.NewFile(@"unpacked\costume_set\costume.psd"), zip),
            Unresolved(app.NewFile(@"other\shoes.zip")));
        resolve.Selected = Row(resolve, "shoes.zip");
        Row(resolve, "costume.psd").IsSelected = true;
        await PreviewAsync(app, resolve, "1000001");

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("元のzipが未確定にある中身が含まれています。zipの行を選んで登録してください。", resolve.StatusText);

        resolve.LocalNameInput = "作り物";
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("元のzipが未確定にある中身が含まれています。zipの行を選んで登録してください。", resolve.LocalStatusText);

        Assert.Empty(app.Notices);
        Assert.Equal(3, app.Store.Unresolved.Load().Count);
    });

    // ---- 作り（XAML）：出口は1つずつ、飛ぶボタンは無い ----

    [Fact]
    public void 登録と除外の出口は今の対象に効く1つずつで_まとめ操作の枠とその他へ飛ぶボタンは無い()
    {
        var xaml = XDocument.Load(ResolveViewPath());
        var buttons = xaml.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
        int Count(string command) => buttons.Count(button => (string?)button.Attribute("Command") == $"{{Binding {command}}}");

        Assert.Equal(1, Count("AssignCommand"));
        Assert.Equal(1, Count("RegisterLocalCommand"));
        Assert.Equal(1, Count("ExcludeCommand"));
        Assert.Equal(1, Count("UseOriginZipCommand"));
        Assert.DoesNotContain(xaml.Descendants(), element => element.Attributes().Any(attribute =>
            attribute.Value.Contains("GoToLocal", StringComparison.Ordinal)
            || attribute.Value.Contains("AssignChecked", StringComparison.Ordinal)
            || attribute.Value.Contains("ExcludeChecked", StringComparison.Ordinal)));

        // 「このファイルだけ」はチェックボックスではなく、帯の切り替え
        Assert.DoesNotContain(xaml.Descendants(), element => element.Name.LocalName == "CheckBox"
            && (string?)element.Attribute("IsChecked") == "{Binding SingleFileOnly, Mode=TwoWay}");
        var band = Assert.Single(xaml.Descendants(), element => (string?)element.Attribute("AutomationProperties.AutomationId") == "ResolveTargetBand");
        Assert.Contains(band.Descendants(), element => element.Name.LocalName == "RadioButton"
            && (string?)element.Attribute("IsChecked") == "{Binding SingleFileOnly, Mode=TwoWay}");
    }

    [Fact]
    public void 帯の高さは決まっていて_中身が変わっても下が跳ねない()
    {
        // ユーザ 2026-10-03「上部の変更があるので画面が一瞬で大きくズレるような動作は避ける」。行の高さを決め打ちにし、文は1行に切る
        var xaml = XDocument.Load(ResolveViewPath());
        var band = Assert.Single(xaml.Descendants(), element => (string?)element.Attribute("AutomationProperties.AutomationId") == "ResolveTargetBand");
        var rows = band.Descendants().Where(element => element.Name.LocalName == "RowDefinition").ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.True(double.TryParse((string?)row.Attribute("Height"), out _), "帯の行は数で高さを決める"));
        Assert.All(band.Descendants().Where(element => element.Name.LocalName == "TextBlock" && ((string?)element.Attribute("Text") ?? string.Empty).StartsWith("{Binding", StringComparison.Ordinal)),
            text => Assert.Equal("CharacterEllipsis", (string?)text.Attribute("TextTrimming")));
    }

    [Fact]
    public void 画像は空でも追加の枠を出し_押すと選ぶ窓で_枠への落とし込みも受ける()
    {
        var xaml = XDocument.Load(ResolveViewPath());
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var add = Assert.Single(xaml.Descendants(), element => (string?)element.Attribute("AutomationProperties.AutomationId") == "ResolveLocalImageAdd");
        Assert.Equal("{Binding ChooseLocalImagesCommand}", (string?)add.Attribute("Command"));
        Assert.Null(add.Attribute("Visibility"));

        // 枠は型（LocalImageGallery）にして、BOOTHに無い商品の欄と、見つからなかったIDのまま登録する欄（ユーザ 2026-10-06）の2か所で同じ物を出す。
        // どちらの欄も画像の落とし込みを受ける
        var gallery = add.Ancestors().First(element => (string?)element.Attribute(x + "Key") == "LocalImageGallery");
        Assert.Equal("DataTemplate", gallery.Name.LocalName);
        foreach (var name in new[] { "LocalCard", "UnpublishedCard" })
        {
            var card = Assert.Single(xaml.Descendants(), element => (string?)element.Attribute(x + "Name") == name);
            Assert.Equal("True", (string?)card.Attribute("AllowDrop"));
            Assert.Equal("OnLocalBoxPreviewDrop", (string?)card.Attribute("PreviewDrop"));
            Assert.Contains(card.Descendants(), element => (string?)element.Attribute("ContentTemplate") == "{StaticResource LocalImageGallery}");
        }
    }

    private static string ResolveViewPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views", "ResolveView.xaml");
}
