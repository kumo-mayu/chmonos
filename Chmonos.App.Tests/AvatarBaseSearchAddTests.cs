using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 共通素体の見方の、探して足す1本の欄（ユーザ判断 2026-11-20・⑤）。候補は素体に入ったアバターの名前（探すための物で、素体の名前は候補にしない）。
/// Enter はアバターの候補ならその名前で絞るだけで書かず、ほかの語は素体として足す。「追加」はいつも足す。名前・ID は作り物。
/// </summary>
public class AvatarBaseSearchAddTests
{
    private const string Aoi = "9800701";
    private const string Momo = "9800702";
    private const string Yuki = "9800703";

    private static AvatarRegistryEntry Avatar(string id, string name, string? baseName = null) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
        AvatarOverride = true,
        BaseName = baseName,
    };

    /// <summary>アオイとモモは「作り物素体」に入り、ユキはどの素体にも入っていない。</summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "アオイ", "作り物素体"), Avatar(Momo, "モモ", "作り物素体"), Avatar(Yuki, "ユキ")],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }, new AvatarBaseGroup { Name = "別の素体" }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowBaseModeCommand.Execute(null);
        return avatars;
    }

    [Fact]
    public Task 候補は素体に入ったアバターの名前だけで_素体の名前と素体に入っていないアバターは出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        Assert.Equal(["アオイ", "モモ"], avatars.BaseSearchCandidates);
        Assert.NotNull(avatars.BaseSearchInfoSelector("アオイ"));
    });

    [Fact]
    public Task Enterでアバターの候補を決めると_その名前で絞るだけで書かない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var writes = app.Store.Avatars.WriteCount;

        avatars.BaseSearchCommitCommand.Execute("アオイ");
        await app.SettleAsync();

        Assert.Equal("アオイ", avatars.BaseQuery);
        Assert.Equal(["作り物素体"], avatars.ShownBases.Select(row => row.Name));
        Assert.Equal(writes, app.Store.Avatars.WriteCount);
        Assert.Equal(2, app.Store.Avatars.Load().BaseGroups.Count);
    });

    [Fact]
    public Task Enterで候補に無い語を決めると_素体として足し_欄を空ける() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.BaseQuery = "新しい素体";
        avatars.BaseSearchCommitCommand.Execute("新しい素体");
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 3, "足した素体が並ぶ");

        Assert.Equal("新しい素体", avatars.SelectedBase?.Name);
        Assert.Equal(string.Empty, avatars.BaseQuery);
    });

    [Fact]
    public Task Enterで今ある素体の名前を決めると_書かずに選んで既にありますと言い_語は残す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var writes = app.Store.Avatars.WriteCount;

        avatars.BaseQuery = "別の素体";
        avatars.BaseSearchCommitCommand.Execute("別の素体");
        await app.SettleAsync();

        Assert.Equal("共通素体「別の素体」は既にあります。", avatars.AddBaseNote.Text);
        Assert.Equal("別の素体", avatars.SelectedBase?.Name);
        Assert.Equal("別の素体", avatars.BaseQuery);
        Assert.Equal(writes, app.Store.Avatars.WriteCount);
    });

    /// <summary>候補のアバターと同じ名前の素体を作りたい時の口。Enter は絞るだけなので、「追加」はいつも足す。</summary>
    [Fact]
    public Task 追加はアバターの候補と同じ名前でも素体として足す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.BaseQuery = "アオイ";
        avatars.AddBaseCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 3, "足した素体が並ぶ");

        Assert.Equal("アオイ", avatars.SelectedBase?.Name);
        Assert.Contains(app.Store.Avatars.Load().BaseGroups, group => group.Name == "アオイ");
    });

    [Fact]
    public Task 二体以上のアバターで当たった行は_ほか何体と添える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.BaseQuery = "作り物のアバター";

        var shown = Assert.Single(avatars.ShownBases);
        Assert.Matches("^アバター「(アオイ|モモ)」ほか 1 体$", shown.MatchNote);
    });

    /// <summary>
    /// 打ってすぐの Enter・「追加」は結び付けの待ち（Delay=200）を先に流し込む（流し込まないと、遅れて届いた語が「既にあります」を消す）。
    /// 画面の側の作りなので、XAML の結び付けで見る
    /// </summary>
    [Fact]
    public void 探して足す欄は_Enterと追加の前に待ちを流し込む()
    {
        var xaml = File.ReadAllText(ViewPath("AvatarsView.xaml"));
        var box = Regex.Match(xaml, @"<controls:SuggestBox x:Name=""BaseSearchBox""[^>]*>").Value;
        var add = Regex.Match(xaml, @"<Button [^>]*AutomationProperties.AutomationId=""AvatarBaseAdd""[^>]*>").Value;
        var code = File.ReadAllText(ViewPath("AvatarsView.xaml.cs"));

        Assert.Contains("Delay=200", box);
        Assert.Contains(@"PreviewKeyDown=""BaseSearchBox_PreviewKeyDown""", box);
        Assert.Contains(@"Click=""BaseAdd_Click""", add);
        Assert.Equal(2, Regex.Matches(code, @"BaseSearchBox\.GetBindingExpression\(SuggestBox\.TextProperty\)\?\.UpdateSource\(\)").Count);
    }

    private static string ViewPath(string name, [CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views", name);
}
