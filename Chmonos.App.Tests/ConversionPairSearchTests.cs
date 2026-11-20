using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」の組を探す欄と、共通素体の見方の探す欄・畳んだ左の欄の名前（2026-11-20・open.md「変換：組を探す欄・右の欄の絵・畳んだ左の欄の名前」）。
/// 組の当て方は「アバター」の見方の探す欄と同じ（名前・ID・正式名・呼び方、共通素体は名前）で、探す所は両方・元・先。
/// 絞っても畳んだ事の記録（ui-state）は書き換えない。名前・ID は作り物。
/// </summary>
public class ConversionPairSearchTests
{
    private const string Aoi = "9800601";
    private const string Momo = "9800602";
    private const string Yuki = "9800603";

    private static AvatarRegistryEntry Avatar(string id, string name, string? alias = null) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」正式版",
        DisplayName = name,
        Category = "3Dキャラクター",
        Aliases = alias is null ? [] : [new AvatarAlias { Text = alias }],
    };

    private static ConversionPair Pair(string from, string to) => new() { From = ConversionEnd.Avatar(from), To = ConversionEnd.Avatar(to) };

    /// <summary>足した順：モモ→アオイ、アオイ→ユキ、モモ→ユキ、アオイ→モモ、作り物素体→アオイ。</summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "アオイ", alias: "Aoi-chan"), Avatar(Momo, "モモ"), Avatar(Yuki, "ユキ")],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }, new AvatarBaseGroup { Name = "別の素体" }],
        });
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs =
            [
                Pair(Momo, Aoi), Pair(Aoi, Yuki), Pair(Momo, Yuki), Pair(Aoi, Momo),
                new ConversionPair { From = ConversionEnd.Base("作り物素体"), To = ConversionEnd.Avatar(Aoi) },
            ],
        });

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowConversionModeCommand.Execute(null);
        await avatars.ConversionPairs.ReloadAsync();
        return avatars;
    }

    private static IEnumerable<string> Shown(ConversionPairEditor pairs) => pairs.Entries.Select(entry => entry switch
    {
        ConversionPairGroup group => $"[{group.Name}{group.CountText}]",
        ConversionPairRow row => row.Text,
        _ => "?",
    });

    [Fact]
    public Task 両方は元か先に当たる組_元は元だけ_先は先だけで当てる() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;

        Assert.Equal(ConversionPairSearchScope.Both, pairs.Scope);
        pairs.Query = "ユキ";
        Assert.Equal(["[アオイ（1）]", "アオイ → ユキ", "[モモ（1）]", "モモ → ユキ"], Shown(pairs));
        Assert.Equal("追加した組（2/5）", pairs.HeaderText);

        // 元だけ：ユキが元の組は無い
        pairs.SetScopeCommand.Execute("from");
        Assert.True(pairs.IsScopeFrom);
        Assert.Empty(pairs.Entries);
        Assert.True(pairs.ShowsNoMatches);

        pairs.Query = "アオイ";
        Assert.Equal(["[アオイ（2）]", "アオイ → モモ", "アオイ → ユキ"], Shown(pairs));

        // 先だけ：アオイが先の組
        pairs.SetScopeCommand.Execute("to");
        Assert.Equal(["[モモ（1）]", "モモ → アオイ", "[作り物素体（共通素体）（1）]", "作り物素体（共通素体） → アオイ"], Shown(pairs));

        // 消すと全部に戻る
        pairs.ClearQueryCommand.Execute(null);
        Assert.Equal(5, pairs.VisibleRows.Count);
        Assert.Equal("追加した組（5）", pairs.HeaderText);
        Assert.False(pairs.ShowsNoMatches);
    });

    [Fact]
    public Task アバターは正式名_呼び方_商品IDでも当たり_共通素体は名前で当たる() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.SetScopeCommand.Execute("from");

        pairs.Query = "aoi-CHAN";
        Assert.Equal(["アオイ → モモ", "アオイ → ユキ"], pairs.VisibleRows.Select(row => row.Text));

        pairs.Query = "正式版";
        Assert.Equal(4, pairs.VisibleRows.Count);

        pairs.Query = Momo;
        Assert.Equal(["モモ → アオイ", "モモ → ユキ"], pairs.VisibleRows.Select(row => row.Text));

        pairs.Query = "作り物素";
        Assert.Equal(["作り物素体（共通素体） → アオイ"], pairs.VisibleRows.Select(row => row.Text));

        // 画面に添える「（共通素体）」では当てない（打つだけで素体の組が全部当たる）
        pairs.Query = "共通素体";
        Assert.Empty(pairs.VisibleRows);

        // 空白だけでは絞らない
        pairs.Query = "  ";
        Assert.False(pairs.IsFiltering);
        Assert.Equal(5, pairs.VisibleRows.Count);
    });

    [Fact]
    public Task 絞ると畳んだまとまりの当たった組を開いて見せるが_畳んだ記録は書き換えず_消すと畳んだ姿に戻る() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        var aoi = pairs.Groups.Single(group => group.Name == "アオイ");
        aoi.IsExpanded = false;
        await app.SettleAsync();
        Assert.Equal(ConversionEnd.Avatar(Aoi), Assert.Single(app.Services.UiState.ConversionPairCollapsed ?? []));

        pairs.Query = "ユキ";
        Assert.True(aoi.IsExpanded);
        Assert.Contains("アオイ → ユキ", Shown(pairs));

        // 絞りの間に畳み・開き直しても覚えない
        aoi.IsExpanded = false;
        Assert.DoesNotContain("アオイ → ユキ", Shown(pairs));
        aoi.IsExpanded = true;
        await app.SettleAsync();
        Assert.Equal(ConversionEnd.Avatar(Aoi), Assert.Single(app.Services.UiState.ConversionPairCollapsed ?? []));

        // 語を消すと、覚えた畳んだ姿に戻る
        pairs.Query = string.Empty;
        await app.SettleAsync();
        Assert.False(aoi.IsExpanded);
        Assert.Equal(["[アオイ（2）]", "[モモ（2）]", "モモ → アオイ", "モモ → ユキ", "[作り物素体（共通素体）（1）]", "作り物素体（共通素体） → アオイ"], Shown(pairs));
        Assert.Equal(ConversionEnd.Avatar(Aoi), Assert.Single(app.Services.UiState.ConversionPairCollapsed ?? []));
    });

    [Fact]
    public Task 絞りに当たらない組を選んでいても_選びは残り右は商品へ移らない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        pairs.SelectedRow = pairs.Rows.Single(row => row.Text == "アオイ → モモ");
        ConversionPairListBox.Mimic(pairs);

        pairs.Query = "ユキ";

        Assert.Equal("アオイ → モモ", pairs.SelectedRow?.Text);
        Assert.Null(pairs.SelectedEntry);
        Assert.Null(avatars.SelectedConversion);
        Assert.True(avatars.ShowsConversionPairDetail);
        Assert.Equal("ユキ", pairs.Query);
    });

    [Fact]
    public Task 絞りに当たらない組を足すと_絞りを外して足した組を選ぶ() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.Query = "ユキ";

        pairs.FromText = "ユキ";
        pairs.ToText = "作り物素体（共通素体）";
        await pairs.AddAsync();
        Assert.Equal("ユキ", pairs.Query);
        Assert.Equal("ユキ → 作り物素体（共通素体）", pairs.SelectedRow?.Text);

        // 当たる組はそのまま絞りの中に出る。当たらない組（既にある組も）は絞りを外して見せる
        pairs.SetScopeCommand.Execute("to");
        pairs.FromText = "モモ";
        pairs.ToText = "アオイ";
        await pairs.AddAsync();
        Assert.Equal(string.Empty, pairs.Query);
        Assert.Equal("モモ → アオイ", pairs.SelectedRow?.Text);
        Assert.Contains(pairs.SelectedRow!, pairs.Entries);
    });

    [Fact]
    public Task 絞っている間に組を外すと_次は見えている組から選ぶ() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.Query = "ユキ";
        pairs.SelectedRow = pairs.VisibleRows.Single(row => row.Text == "アオイ → ユキ");

        await pairs.RemoveAsync(pairs.SelectedRow);

        Assert.Equal("モモ → ユキ", pairs.SelectedRow?.Text);
        Assert.Equal("ユキ", pairs.Query);
    });

    [Fact]
    public Task 共通素体の見方の探す欄は素体の名前か入ったアバターで絞り_アバターで当たった行には何で当たったかを添える() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "アオイ") with { BaseName = "作り物素体" }, Avatar(Momo, "モモ")],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }, new AvatarBaseGroup { Name = "別の素体" }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowBaseModeCommand.Execute(null);
        Assert.Equal(2, avatars.ShownBases.Count);
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == "作り物素体");

        avatars.BaseQuery = "別の";
        Assert.Equal(["別の素体"], avatars.ShownBases.Select(row => row.Name));
        Assert.Equal("別の素体", avatars.SelectedBase?.Name);

        // 素体の数（切り替えの「共通素体（n）」）は絞っても全部の数
        Assert.Equal("共通素体（2）", avatars.BaseModeText);
        Assert.False(avatars.ShownBases.Single().HasMatchNote);

        // 入ったアバターの名前でも当たり、何で当たったかを行に添える（ユーザ判断 2026-11-20・⑤）
        avatars.BaseQuery = "アオイ";
        var shown = Assert.Single(avatars.ShownBases);
        Assert.Equal("作り物素体", shown.Name);
        Assert.Equal("アバター「アオイ」", shown.MatchNote);

        avatars.BaseQuery = "当たらない語";
        Assert.Empty(avatars.ShownBases);
        Assert.True(avatars.ShowsNoMatchingBases);
        Assert.False(avatars.ShowsNoBases);

        // 絞りで外れた素体を選ぶ（組の端から移る時など）と、絞りを外す
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == "作り物素体");
        Assert.Equal(string.Empty, avatars.BaseQuery);
        Assert.Equal(2, avatars.ShownBases.Count);
    });

    [Fact]
    public Task 左の欄を畳んでいる間は_左にしか無い空の文を右に出す() => TestApp.Run(async app =>
    {
        // アバターは1体、共通素体は0、プロファイルの商品も0
        await app.Store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar(Aoi, "アオイ")] });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");

        var changed = new List<string?>();
        avatars.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // 広げている間は出さない（左で言う）
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.True(avatars.ShowsNoConversionItems);
        Assert.Equal(string.Empty, avatars.CollapsedEmptyText);
        Assert.False(avatars.HasCollapsedEmpty);

        // 畳むと、変換の「商品がまだ無い」を右に出す
        avatars.ListPane.IsCollapsed = true;
        Assert.Contains(nameof(AvatarsViewModel.CollapsedEmptyText), changed);
        Assert.StartsWith("変換ツールのプロファイルを含む商品はまだありません", avatars.CollapsedEmptyText);
        Assert.True(avatars.HasCollapsedEmpty);
        Assert.False(avatars.CollapsedEmptyCanClear);

        // 共通素体：1つも無い（作り方を言う）
        avatars.ShowBaseModeCommand.Execute(null);
        Assert.StartsWith("共通素体はまだありません", avatars.CollapsedEmptyText);
        Assert.False(avatars.CollapsedEmptyCanClear);

        // 「アバター」の見方は右に元からある空の文なので、ここでは出さない
        avatars.ShowAvatarModeCommand.Execute(null);
        Assert.Equal(string.Empty, avatars.CollapsedEmptyText);

        // 広げ直すと消える
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.ListPane.IsCollapsed = false;
        Assert.Equal(string.Empty, avatars.CollapsedEmptyText);
    });

    [Fact]
    public Task 畳んだ左の欄で探して0件なら_その文と絞り込みを消すボタンを右に出す() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "アオイ")],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.ListPane.IsCollapsed = true;
        Assert.Equal(string.Empty, avatars.CollapsedEmptyText);

        avatars.BaseQuery = "当たらない語";
        Assert.Equal("打ち込んだ言葉に当てはまる共通素体がありません。", avatars.CollapsedEmptyText);
        Assert.True(avatars.CollapsedEmptyCanClear);

        // 畳んだまま右のボタンで消せる（探す欄も畳まれて見えない）
        avatars.CollapsedClearCommand.Execute(null);
        Assert.Equal(string.Empty, avatars.BaseQuery);
        Assert.Equal(string.Empty, avatars.CollapsedEmptyText);
        Assert.False(avatars.CollapsedEmptyCanClear);
    });

    [Fact]
    public Task 畳んだ左の欄の名前は今の見方の名前() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        Assert.Equal("変換", avatars.ListPaneLabel);

        var changed = new List<string?>();
        avatars.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        avatars.ShowBaseModeCommand.Execute(null);
        Assert.Equal("共通素体", avatars.ListPaneLabel);
        Assert.Contains(nameof(AvatarsViewModel.ListPaneLabel), changed);

        avatars.ShowAvatarModeCommand.Execute(null);
        Assert.Equal("アバター", avatars.ListPaneLabel);
    });
}
