using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定で選んだ物（「このフォルダを選択」・チェック）を、まとめて1つの「BOOTHに無い商品」として登録する（ユーザ指示 2026-10-02：
/// 「このフォルダを選択で選んでも仮IDをまとめて登録できない」）。あわせて、自動検索のボタンを「商品IDを決める」と「候補」にも置いたこと。
/// </summary>
public class ResolveLocalCheckedTests
{
    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveAsync(TestApp app, params string[] names)
    {
        await app.Store.Unresolved.SaveAsync([.. names.Select(name => Unresolved(app.NewFile(name)))]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.True(resolve.IsLoaded);
        return (main, resolve);
    }

    private static UnresolvedRow RowOf(ResolveViewModel resolve, string fileName)
        => resolve.Files.Single(row => row.FileName == fileName);

    [Fact]
    public Task このフォルダを選択で選んだ物は_1つのBOOTHに無い商品にまとめて登録でき_未確定とナビの数から消える() => TestApp.Run(async app =>
    {
        // zip 自身は束に入らないので、フォルダで束ねる psd を使う
        var (main, resolve) = await OpenResolveAsync(app, @"pack\body.psd", @"pack\extra.psd", @"other\shoes.zip");
        var body = RowOf(resolve, "body.psd");

        resolve.SelectFolderCommand.Execute(body.GroupKey);

        // 選んだ物が今の対象になり、上の帯が件数を言う（その他の「この名前で登録する」はこの対象に効く。メモ22）
        Assert.Equal(2, resolve.CheckedCount);
        Assert.Equal("選択した 2 件", resolve.TargetText);
        Assert.True(resolve.RegisterLocalCommand.CanExecute(null));

        // 仮IDは選んだ1件目から決まり、押す前に見せたIDと登録したIDが合う
        var lead = resolve.Files.First(row => row.IsSelected);
        Assert.Equal(LocalItemId.For(lead.File.Hash), resolve.LocalIdPreview);

        resolve.LocalNameInput = "作り物のまとめた衣装";
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        var asked = Assert.Single(app.Notices);
        Assert.Equal("BOOTHに無い商品として登録する", asked.Caption);
        Assert.StartsWith("選択した 2 件 を「作り物のまとめた衣装」として登録します。", asked.Text, StringComparison.Ordinal);

        var item = await app.Store.Items.LoadAsync(LocalItemId.For(lead.File.Hash));
        Assert.NotNull(item);
        Assert.Equal("作り物のまとめた衣装", item!.Local.DisplayName);
        Assert.Equal(
            ["body.psd", "extra.psd"],
            item.Local.LocalFiles.Select(file => Path.GetFileName(file.Paths[0])).Order(StringComparer.Ordinal).ToArray());

        // 選んだ全部が未確定から消え、選んでいない物は残る。ナビの数も減る
        Assert.Equal("shoes.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("shoes.zip", Path.GetFileName(Assert.Single(app.Store.Unresolved.Load()).Paths[0]));
        Assert.False(resolve.HasChecked);
        Assert.Equal("2 件を登録しました。", resolve.ListNoticeText);
        await UiThread.Until(() => main.UnresolvedCount == 1, "ナビの未確定の数が合う");
    });

    [Fact]
    public Task 一覧のチェックで選んだ物も_同じくまとめて登録する() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\hat.zip", @"b\hat_texture.zip", @"c\other.zip");

        RowOf(resolve, "hat.zip").IsSelected = true;
        RowOf(resolve, "hat_texture.zip").IsSelected = true;
        var lead = resolve.Files.First(row => row.IsSelected).File.Hash;
        resolve.LocalNameInput = "作り物の帽子";
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(LocalItemId.For(lead));
        Assert.Equal(2, item!.Local.LocalFiles.Count);
        Assert.Equal("other.zip", Assert.Single(resolve.Files).FileName);
    });

    [Fact]
    public Task 選んでいないときは_今まで通り選んでいる1件だけを登録し_次の行へ移る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\gift.zip", @"b\other.zip");
        var gift = RowOf(resolve, "gift.zip");
        resolve.Selected = gift;

        Assert.False(resolve.HasChecked);
        Assert.Equal("gift.zip", resolve.TargetText);

        resolve.LocalNameInput = "作り物の贈り物";
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        Assert.StartsWith("gift.zip を「作り物の贈り物」として登録します。", Assert.Single(app.Notices).Text, StringComparison.Ordinal);
        Assert.Single((await app.Store.Items.LoadAsync(LocalItemId.For(gift.File.Hash)))!.Local.LocalFiles);
        Assert.Equal("other.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("other.zip", resolve.Selected?.FileName);
    });

    [Fact]
    public Task 名前は書き換えていなければ選んだ物に合わせて下書きし直し_書き換えた名前は消さない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\ribbon_v1.2.zip", @"b\skirt.zip", @"c\socks.zip");
        resolve.Selected = RowOf(resolve, "socks.zip");
        Assert.Equal("socks", resolve.LocalNameInput);

        // 選ぶと、選んだ1件目（一覧の順）の名前に合わせる（1件のときと同じく、版番号などは落とす）
        RowOf(resolve, "skirt.zip").IsSelected = true;
        RowOf(resolve, "ribbon_v1.2.zip").IsSelected = true;
        var first = resolve.Files.First(row => row.IsSelected);
        Assert.Equal(FileNameQuery.ToNameDraft(first.FileName), resolve.LocalNameInput);

        // 人が書いた名前は、選び足しても消さない
        resolve.LocalNameInput = "手で付けた名前";
        RowOf(resolve, "socks.zip").IsSelected = true;
        Assert.Equal("手で付けた名前", resolve.LocalNameInput);
    });

    [Fact]
    public Task zipが無いフォルダをこのフォルダを選択で選ぶと_名前の下書きはフォルダの名前になり_中身全部を1つに登録する() => TestApp.Run(async app =>
    {
        // 親は6段までたどる。5段掘った中に置けば、置き場より上（実マシンの一時フォルダ）の中身に答えが左右されない
        const string inside = @"1\2\3\4\5";
        var texture = app.NewFile(inside + @"\outfit_v1\outfit\texture\t.png");
        var package = app.NewFile(inside + @"\outfit_v1\outfit\outfit.unitypackage");
        app.NewFile(inside + @"\other\readme.txt");
        var importFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(texture))))!;
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [importFolder] });
        await app.Store.Unresolved.SaveAsync([Unresolved(texture), Unresolved(package)]);

        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        var row = resolve.Files.First();
        Assert.True(row.IsArchiveContent);

        resolve.SelectFolderCommand.Execute(row.GroupKey);

        Assert.Equal(FileNameQuery.ToNameDraft("outfit_v1"), resolve.LocalNameInput);

        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        Assert.Empty(resolve.Files);
        Assert.Empty(app.Store.Unresolved.Load());
        var ids = app.Store.Items.EnumerateItemIds().ToList();
        var item = await app.Store.Items.LoadAsync(Assert.Single(ids));
        Assert.Equal(2, item!.Local.LocalFiles.Count);
        Assert.Equal(FileNameQuery.ToNameDraft("outfit_v1"), item.Local.DisplayName);
        await UiThread.Until(() => main.UnresolvedCount == 0, "ナビの未確定の数が合う");
    });

    // ---- 自動検索のボタン（ユーザ指示 2026-10-02：「商品IDを決める」と候補にも重なってあってよい）----

    [Fact]
    public void 自動検索のボタンは_商品IDを決める_候補の2か所にあり_分かっていることには無い()
    {
        // 置き場所は XAML の作りで決まる。部品を組んで測るにはアプリの見た目の資源が要るので、原文を読む（ItemPageTagsTests と同じ）。見た目は ViewShot の絵で見る
        var xaml = XDocument.Load(ResolveViewPath());
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var propose = xaml.Descendants()
            .Where(element => element.Name.LocalName == "Button" && (string?)element.Attribute("Command") == "{Binding ProposeCommand}")
            .ToList();
        Assert.Equal(2, propose.Count);
        Assert.All(propose, button => Assert.Equal("自動検索", (string?)button.Attribute("Content")));

        string? CardOf(XElement button) => button.Ancestors()
            .Select(ancestor => (string?)ancestor.Attribute(x + "Name"))
            .FirstOrDefault(name => name is "DecisionCard" or "CandidatesCard");

        Assert.Contains("DecisionCard", propose.Select(CardOf));
        Assert.Contains("CandidatesCard", propose.Select(CardOf));
        // 分かっていることの見出しの横からは外した（ユーザ判断 2026-10-02）
        Assert.All(propose, button => Assert.NotNull(CardOf(button)));
    }

    [Fact]
    public void 右の欄の見出しの横のボタンは_狭いと次の行へ送る部品に入れ_右の列には最小の幅がある()
    {
        // 前は見出しとボタンを1つの Grid に重ねて右寄せしていて、狭い窓でボタンが見出しの上に乗った（ユーザ 2026-10-02 メモ12）。
        // 送るかどうかは幅で決まるので絵（ViewShot の resolve-unpacked-folder・幅700/900）で見る。ここでは重ねる作りに戻っていないことを見る
        var xaml = XDocument.Load(ResolveViewPath());
        string[] ids = ["ResolveProposeInDecision", "ResolveProposeInCandidates", "ResolveCopyFileName", "ResolveReveal", "ResolveSearchInBrowser"];
        foreach (var id in ids)
        {
            var button = Assert.Single(xaml.Descendants(), element => (string?)element.Attribute("AutomationProperties.AutomationId") == id);
            Assert.Contains(button.Ancestors().Take(2), ancestor => ancestor.Name.LocalName == "SplitRowPanel");
        }

        var paneGrid = Assert.Single(xaml.Descendants(), element => element.Name.LocalName == "PaneGrid");
        Assert.Contains(paneGrid.Descendants(), element => element.Name.LocalName == "ColumnDefinition"
            && (string?)element.Attribute("Width") == "*" && (string?)element.Attribute("MinWidth") == "360");
    }

    [Fact]
    public Task 下の自動検索を押しても_見出しの横の物と同じく候補を探す() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\作り物の衣装.zip");

        // どのボタンも同じ ProposeCommand を押す。押せる条件と、押すとBOOTHを探しに行くことを確かめる
        Assert.True(resolve.ProposeCommand.CanExecute(null));
        var asked = app.Booth.Requests.Count;
        resolve.ProposeCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(app.Booth.Requests.Count > asked);
        Assert.True(resolve.HasSearched);
    });

    private static string ResolveViewPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views", "ResolveView.xaml");
}
