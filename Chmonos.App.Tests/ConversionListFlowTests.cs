using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」の左の流れに、組のまとまりの見出しと組の行を、外の一覧（<see cref="AvatarsViewModel.ConversionList"/>）の行として平らに並べる
/// （ユーザ判断 2026-11-20「組は大きくなる箇所なので b で」。節の中の入れ子の一覧だと組の数だけ行を全部作った）。
/// 選び（組か商品のどちらか1つ）・キーの渡り・畳む・並び替え・絞り・商品が無い時の1行。名前・ID は作り物
/// </summary>
public class ConversionListFlowTests
{
    private const string Aoi = "9800401";
    private const string Momo = "9800402";

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
    };

    private static ConversionPair Pair(string from, string to) => new() { From = ConversionEnd.Avatar(from), To = ConversionEnd.Avatar(to) };

    /// <summary>組3つ（元アオイ2つ・元モモ1つ）と、プロファイルの商品2つ（決めていない）。</summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app, bool withItems = true)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar(Aoi, "アオイ"), Avatar(Momo, "モモ")] });
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile { Pairs = [Pair(Aoi, Momo), Pair(Momo, Aoi), Pair(Aoi, Aoi + "x")] });
        if (withItems)
        {
            await ConversionItemGroupTests.SaveProfilesAsync(app, "9900871", "作り物のプロファイルあ", null, "fakea");
            await ConversionItemGroupTests.SaveProfilesAsync(app, "9900872", "作り物のプロファイルい", null, "fakeb");
        }

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems == withItems && avatars.Rows.Count > 0, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);
        await UiThread.Until(() => avatars.ConversionPairs.Rows.Count == 3, "組の欄を読む");
        return avatars;
    }

    private static List<object> Flow(AvatarsViewModel avatars) => avatars.ConversionList.Cast<object>().ToList();

    /// <summary>流れのうち、組の見出しと組の行（節の後ろに並ぶ物）。</summary>
    private static List<object> PairPart(AvatarsViewModel avatars)
        => [.. Flow(avatars).Where(entry => entry is ConversionPairGroup or ConversionPairRow)];

    private static IReadOnlyList<ConversionItemRowViewModel> Items(AvatarsViewModel avatars) => [.. Flow(avatars).OfType<ConversionItemRowViewModel>()];

    [Fact]
    public Task 組の見出しと行は_節の後ろに外の一覧の行として並び_商品の行が続く() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        var flow = Flow(avatars);

        Assert.Same(avatars.PairsSection, flow[0]);
        Assert.Equal(pairs.Entries, flow.Skip(1).Take(pairs.Entries.Count));
        Assert.Equal(5, pairs.Entries.Count); // 見出し2つと組の行3つ
        Assert.Equal(avatars.ConversionItems.Cast<object>(), flow.Skip(1 + pairs.Entries.Count));
        Assert.DoesNotContain(ConversionListEnd.Instance, flow);

        // 組の物は節の群（見出しを出さない群）に入り、商品の群は今のまま
        Assert.Equal(["追加した組", "決めていない（2）"],
            avatars.ConversionList.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(group => group.Name.ToString()));
    });

    [Fact]
    public Task 見出しを畳むと中の組の行が一覧から消え_広げると同じ所に戻る_節を畳むと組の物が全部消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        var aoi = pairs.Groups.Single(group => group.From.AvatarItemId == Aoi);

        aoi.IsExpanded = false;
        Assert.DoesNotContain(PairPart(avatars), entry => entry is ConversionPairRow row && aoi.Rows.Contains(row));
        Assert.Contains(aoi, PairPart(avatars));
        Assert.Equal(pairs.Entries, PairPart(avatars));

        aoi.IsExpanded = true;
        Assert.Equal(5, PairPart(avatars).Count);
        Assert.Equal(pairs.Entries, PairPart(avatars));

        pairs.IsExpanded = false;
        Assert.Empty(PairPart(avatars));
        Assert.Same(avatars.PairsSection, Flow(avatars)[0]);
        Assert.Equal(avatars.ConversionItems.Cast<object>(), Flow(avatars).Skip(1));

        pairs.IsExpanded = true;
        Assert.Equal(pairs.Entries, PairPart(avatars));
    });

    [Fact]
    public Task 並び替えと絞りは_流れの組の所にも同じ順で当たる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;

        pairs.SelectedSortChoice = ConversionPairSortChoice.All.Single(choice => choice.Sort == ConversionPairSort.NameDescending);
        Assert.Equal("モモ", Assert.IsType<ConversionPairGroup>(PairPart(avatars)[0]).Name);
        Assert.Equal(pairs.Entries, PairPart(avatars));

        pairs.Query = "モモ";
        Assert.Equal(pairs.Entries, PairPart(avatars));
        Assert.Equal(pairs.Entries, Flow(avatars).Skip(1).Take(pairs.Entries.Count));

        pairs.Query = string.Empty;
        Assert.Equal(pairs.Entries, PairPart(avatars));
        Assert.Equal(5, PairPart(avatars).Count);
    });

    [Fact]
    public Task 組の行を選ぶと右は組_商品の行を選ぶと右は商品_見出しと節は選ばない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        var row = pairs.Rows[0];

        avatars.SelectedConversionEntry = row;
        Assert.Same(row, pairs.SelectedRow);
        Assert.Null(avatars.SelectedConversion);
        Assert.True(avatars.ShowsConversionPairDetail);
        Assert.False(avatars.ShowsConversionDetail);
        Assert.Same(row, avatars.SelectedConversionEntry);

        // 見出し・節を選んでも選びは替わらない
        avatars.SelectedConversionEntry = pairs.Groups[0];
        avatars.SelectedConversionEntry = avatars.PairsSection;
        Assert.Same(row, pairs.SelectedRow);

        var item = Items(avatars)[1];
        avatars.SelectedConversionEntry = item;
        Assert.Same(item, avatars.SelectedConversion);
        Assert.Null(pairs.SelectedRow);
        Assert.True(avatars.ShowsConversionDetail);
        Assert.False(avatars.ShowsConversionPairDetail);
        Assert.Same(item, avatars.SelectedConversionEntry);
    });

    [Fact]
    public Task 組を選んだまま語を打っても_一覧が書き戻すnullで右が商品へ替わらない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        var row = pairs.Rows.First(other => other.FromName == "アオイ");
        avatars.SelectedConversionEntry = row;

        // ListBox は選んだ物が消えると null を書き戻す。語を打つと流れを作り直すので、その null を真似る
        pairs.Query = "当たらない語";
        avatars.SelectedConversionEntry = null;
        Assert.Same(row, pairs.SelectedRow);
        Assert.True(avatars.ShowsConversionPairDetail);

        pairs.Query = string.Empty;
        Assert.Same(row, pairs.SelectedRow);
        Assert.Same(row, avatars.SelectedConversionEntry);
    });

    [Fact]
    public Task キーは組の見出し_組の行_商品の行を1つの並びとして渡り_組から商品へ移ると右が商品になる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        var lastPair = pairs.Entries[^1];
        var firstItem = Items(avatars)[0];

        // 先頭の見出しの上で ↑ は止まる（回り込まない）。見出しから ↓ で組の行を選ぶ
        Assert.Null(avatars.MoveConversionEntry(pairs.Entries[0], -1));
        Assert.Same(pairs.Entries[1], avatars.MoveConversionEntry(pairs.Entries[0], 1));
        Assert.Same(pairs.Entries[1], pairs.SelectedRow);

        // 最後の組の行から ↓ で最初の商品の行へ渡り、右は商品になる
        avatars.SelectedConversionEntry = lastPair;
        Assert.Same(firstItem, avatars.MoveConversionEntry(lastPair, 1));
        Assert.Same(firstItem, avatars.SelectedConversion);
        Assert.Null(pairs.SelectedRow);
        Assert.True(avatars.ShowsConversionDetail);
        Assert.False(avatars.ShowsConversionPairDetail);

        // 最初の商品の行から ↑ で最後の組の行へ戻る（商品の間は ListBox の既定に任せ、組の物へ移る時だけ受ける）
        Assert.Same(lastPair, avatars.ConversionEntryTarget(firstItem, -1));
        Assert.Same(pairs.Entries[0], avatars.ConversionEntryTarget(firstItem, -1, toEdge: true));
        Assert.IsType<ConversionItemRowViewModel>(avatars.ConversionEntryTarget(firstItem, 1));

        // 最後の組のまとまりを畳むと、その見出しから ↓ で商品へ渡る
        var lastGroup = pairs.Groups[^1];
        lastGroup.IsExpanded = false;
        Assert.Same(firstItem, avatars.ConversionEntryTarget(lastGroup, 1));
    });

    [Fact]
    public Task 商品が1件も見えない時だけ_組の行の後ろに空表示の1行を置く() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        Assert.DoesNotContain(ConversionListEnd.Instance, Flow(avatars));

        // 組には当たるが商品には当たらない語：組の行の後ろ、商品の行が来る所に1行
        pairs.Query = "モモ";
        await UiThread.Until(() => avatars.ShowsNoMatchingConversionItems, "商品が絞りで0件になる");
        var flow = Flow(avatars);
        Assert.Same(ConversionListEnd.Instance, flow[^1]);
        Assert.Equal(pairs.Entries, flow.Skip(1).Take(pairs.Entries.Count));
        Assert.Equal(1 + pairs.Entries.Count + 1, flow.Count);

        pairs.Query = string.Empty;
        await UiThread.Until(() => !avatars.ShowsNoMatchingConversionItems, "絞りを消す");
        Assert.DoesNotContain(ConversionListEnd.Instance, Flow(avatars));
    });

    [Fact]
    public Task 商品が無くても組の行は並び_空表示の1行は組の後ろ() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, withItems: false);
        var pairs = avatars.ConversionPairs;
        var flow = Flow(avatars);

        Assert.True(avatars.ShowsNoConversionItems);
        Assert.Same(avatars.PairsSection, flow[0]);
        Assert.Equal(pairs.Entries, flow.Skip(1).Take(pairs.Entries.Count));
        Assert.Same(ConversionListEnd.Instance, flow[^1]);
    });

    [Fact]
    public void 組の行は節の中の入れ子の一覧に置かず_外の一覧の行の見た目で出す()
    {
        var xaml = File.ReadAllText(AvatarsViewPath());

        // 入れ子の一覧（Entries に結んだ ListBox）は見える行だけ作るのが効かず、組の数だけ行を全部作った
        Assert.DoesNotContain(@"ItemsSource=""{Binding Entries}""", xaml);
        var list = Regex.Match(xaml, @"<ListBox ItemsSource=""\{Binding ConversionList\}""[^>]*>").Value;
        Assert.Contains(@"SelectedItem=""{Binding SelectedConversionEntry, Mode=TwoWay}""", list);
        Assert.Contains(@"PreviewKeyDown=""ConversionList_PreviewKeyDown""", list);
    }

    private static string AvatarsViewPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views", "AvatarsView.xaml");
}
