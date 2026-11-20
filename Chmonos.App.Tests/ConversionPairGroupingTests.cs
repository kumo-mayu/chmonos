using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」の追加した組の並びとまとめ（ユーザ判断 2026-11-20・open.md「変換：追加した組の行に…」③④）：
/// 元でまとめた見出し・4つの並び・並びと畳んだかを ui-state に覚える・足した組のまとまりを開いて選ぶ・見出しを渡るキー。名前・ID は作り物。
/// </summary>
public class ConversionPairGroupingTests
{
    private const string Aoi = "9800501";
    private const string Momo = "9800502";
    private const string Yuki = "9800503";

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
    };

    private static ConversionPair Pair(string from, string to) => new() { From = ConversionEnd.Avatar(from), To = ConversionEnd.Avatar(to) };

    /// <summary>
    /// 足した順：0 モモ→アオイ、1 アオイ→ユキ、2 モモ→ユキ、3 アオイ→モモ。
    /// 元の名前の順はアオイ・モモ、追加した順の新しいまとまりはアオイ（中のいちばん新しい組が 3）
    /// </summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app, bool withPairs = true)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "アオイ"), Avatar(Momo, "モモ"), Avatar(Yuki, "ユキ")],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
        });
        if (withPairs)
        {
            await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
            {
                Pairs = [Pair(Momo, Aoi), Pair(Aoi, Yuki), Pair(Momo, Yuki), Pair(Aoi, Momo)],
            });
        }

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowConversionModeCommand.Execute(null);
        await avatars.ConversionPairs.ReloadAsync();
        return avatars;
    }

    /// <summary>一覧に出す物を、見出しは「名前（数）」、行は「元 → 先」の字で。</summary>
    private static IEnumerable<string> Shown(ConversionPairEditor pairs) => pairs.Entries.Select(entry => entry switch
    {
        ConversionPairGroup group => $"[{group.Name}{group.CountText}]",
        ConversionPairRow row => row.Text,
        _ => "?",
    });

    private static void SortBy(ConversionPairEditor pairs, ConversionPairSort sort)
        => pairs.SelectedSortChoice = pairs.SortChoices.Single(choice => choice.Sort == sort);

    [Fact]
    public Task 組はいつも元でまとめ_既定は元の名前順で_中は先の名前順() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;

        Assert.Equal(ConversionPairSort.Name, pairs.Sort);
        Assert.Equal(
            ["[アオイ（2）]", "アオイ → モモ", "アオイ → ユキ", "[モモ（2）]", "モモ → アオイ", "モモ → ユキ"],
            Shown(pairs));
        Assert.Equal("アオイの組", pairs.Groups[0].Subject);
    });

    [Fact]
    public Task 並びは元の名前の逆順_追加が新しい順_古い順に替えられる() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;

        // 逆順はまとまりだけを逆にし、中は先の名前順のまま
        SortBy(pairs, ConversionPairSort.NameDescending);
        Assert.Equal(
            ["[モモ（2）]", "モモ → アオイ", "モモ → ユキ", "[アオイ（2）]", "アオイ → モモ", "アオイ → ユキ"],
            Shown(pairs));

        // 新しい順：まとまりは中のいちばん新しい組の順（アオイは 3、モモは 2）、中も新しい順
        SortBy(pairs, ConversionPairSort.Newest);
        Assert.Equal(
            ["[アオイ（2）]", "アオイ → モモ", "アオイ → ユキ", "[モモ（2）]", "モモ → ユキ", "モモ → アオイ"],
            Shown(pairs));

        // 古い順は新しい順のちょうど逆
        SortBy(pairs, ConversionPairSort.Oldest);
        Assert.Equal(
            ["[モモ（2）]", "モモ → アオイ", "モモ → ユキ", "[アオイ（2）]", "アオイ → ユキ", "アオイ → モモ"],
            Shown(pairs));

        // 外した後の次の組も、今の並びの順で引く
        Assert.Equal(["モモ → アオイ", "モモ → ユキ", "アオイ → ユキ", "アオイ → モモ"], pairs.Rows.Select(row => row.Text));
    });

    [Fact]
    public Task 選んだ並びはui_stateに覚え_次に開いた時もその並びで始まる() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        SortBy(pairs, ConversionPairSort.Newest);
        await app.SettleAsync();

        Assert.Equal("newest", app.Services.UiState.ConversionPairSort);
        var again = new ConversionPairEditor(app.Services);
        await again.ReloadAsync();
        Assert.Equal(ConversionPairSort.Newest, again.Sort);
        Assert.Equal("アオイ → モモ", again.Rows[0].Text);
    });

    [Fact]
    public void 覚えた並びが知らない値なら_元の名前順で始める()
    {
        Assert.Equal(ConversionPairSort.Name, ConversionPairSortChoice.FromKey("byColor"));
        Assert.Equal(ConversionPairSort.Name, ConversionPairSortChoice.FromKey(null));
        Assert.Equal(ConversionPairSort.Oldest, ConversionPairSortChoice.FromKey("oldest"));
    }

    [Fact]
    public Task 見出しで畳むと中の組を一覧から隠し_畳んだかを覚えて次に開いた時も畳んだまま() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;

        pairs.Groups.Single(group => group.Name == "アオイ").IsExpanded = false;
        Assert.Equal(["[アオイ（2）]", "[モモ（2）]", "モモ → アオイ", "モモ → ユキ"], Shown(pairs));

        // 畳んでも組の数と、外した後の次の組を引く並びには残る
        Assert.Equal(4, pairs.Rows.Count);

        await app.SettleAsync();
        Assert.Equal(ConversionEnd.Avatar(Aoi), Assert.Single(app.Services.UiState.ConversionPairCollapsed ?? []));

        var again = new ConversionPairEditor(app.Services);
        await again.ReloadAsync();
        Assert.Equal(["[アオイ（2）]", "[モモ（2）]", "モモ → アオイ", "モモ → ユキ"], Shown(again));

        // 開き直すと覚えた物からも消す
        again.Groups.Single(group => group.Name == "アオイ").IsExpanded = true;
        await app.SettleAsync();
        Assert.Empty(app.Services.UiState.ConversionPairCollapsed ?? []);
        Assert.Equal(6, again.Entries.Count);
    });

    [Fact]
    public Task 畳んだまとまりの元から組を足すと_そのまとまりを開いて足した組を選び_開いた事も覚える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        pairs.Groups.Single(group => group.Name == "アオイ").IsExpanded = false;
        await app.SettleAsync();

        pairs.FromText = "アオイ";
        pairs.ToText = "作り物素体（共通素体）";
        await pairs.AddAsync();

        Assert.Equal("アオイ → 作り物素体（共通素体）", pairs.SelectedRow?.Text);
        Assert.Same(pairs.SelectedRow, pairs.SelectedEntry);
        Assert.True(pairs.Groups.Single(group => group.Name == "アオイ").IsExpanded);
        Assert.Contains(pairs.SelectedRow!, pairs.Entries);
        Assert.True(avatars.ShowsConversionPairDetail);

        // 覚えた物からも消す（「変換」に入り直すたびの読み直しで、また畳まれて選んだ組が隠れないように）
        await app.SettleAsync();
        Assert.Empty(app.Services.UiState.ConversionPairCollapsed ?? []);
        await pairs.ReloadAsync();
        Assert.Contains(pairs.SelectedRow!, pairs.Entries);
    });

    [Fact]
    public Task 既にある組を足そうとすると_畳んだまとまりを開いてその組を選ぶ() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.Groups.Single(group => group.Name == "モモ").IsExpanded = false;

        pairs.FromText = "モモ";
        pairs.ToText = "ユキ";
        await pairs.AddAsync();

        Assert.Equal("モモ → ユキ", pairs.SelectedRow?.Text);
        Assert.Contains(pairs.SelectedRow!, pairs.Entries);
    });

    /// <summary>
    /// 選んだ組のまとまりを畳むと、ListBox は消えた行の選びに null を書き戻す（試験には ListBox が無いので <see cref="ConversionPairListBox"/> で同じ事をする）。
    /// それを受けると、右が決めていない商品へ移る（右はプロファイルの商品か組のどちらか1つ・一覧の作り直しで選びを商品へ移さない）
    /// </summary>
    [Fact]
    public Task 選んだ組のまとまりを畳んでも_組の選びは残り_右は商品へ移らない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var pairs = avatars.ConversionPairs;
        pairs.SelectedRow = pairs.Rows.Single(row => row.Text == "アオイ → ユキ");
        ConversionPairListBox.Mimic(pairs);

        pairs.Groups.Single(group => group.Name == "アオイ").IsExpanded = false;

        Assert.Equal("アオイ → ユキ", pairs.SelectedRow?.Text);
        Assert.Null(pairs.SelectedEntry);
        Assert.Null(avatars.SelectedConversion);
        Assert.True(avatars.ShowsConversionPairDetail);

        // 並びを替えても同じ（行は出し入れするだけ）
        SortBy(pairs, ConversionPairSort.Newest);
        Assert.Equal("アオイ → ユキ", pairs.SelectedRow?.Text);
        Assert.Null(avatars.SelectedConversion);
    });

    [Fact]
    public Task 選んだ組を外した後の次の組が畳んだまとまりにあれば_開いて選ぶ() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.Groups.Single(group => group.Name == "モモ").IsExpanded = false;
        pairs.SelectedRow = pairs.Rows.Single(row => row.Text == "アオイ → ユキ");

        await pairs.RemoveAsync(pairs.SelectedRow);

        Assert.Equal("モモ → アオイ", pairs.SelectedRow?.Text);
        Assert.Contains(pairs.SelectedRow!, pairs.Entries);
    });

    [Fact]
    public Task キーの上下は見出しと行を1つの並びとして渡り_行に来た時だけ選ぶ_端では止まる() => TestApp.Run(async app =>
    {
        var pairs = (await OpenAsync(app)).ConversionPairs;
        pairs.Groups.Single(group => group.Name == "アオイ").IsExpanded = false;

        // [アオイ（2）] [モモ（2）] モモ → アオイ / モモ → ユキ
        var first = pairs.MoveEntry(null, 1);
        Assert.Same(pairs.Groups[0], first);
        Assert.Null(pairs.SelectedRow);

        var second = pairs.MoveEntry(first, 1);
        Assert.Same(pairs.Groups[1], second);
        Assert.Null(pairs.SelectedRow);

        var row = pairs.MoveEntry(second, 1);
        Assert.Equal("モモ → アオイ", pairs.SelectedRow?.Text);
        Assert.Same(pairs.SelectedRow, row);

        var last = pairs.MoveEntry(row, 1, toEdge: true);
        Assert.Equal("モモ → ユキ", pairs.SelectedRow?.Text);
        Assert.Null(pairs.MoveEntry(last, 1));

        // 見出しへ戻っても組の選びはそのまま（見出しは選ばない）
        Assert.Same(pairs.Groups[0], pairs.MoveEntry(last, -1, toEdge: true));
        Assert.Equal("モモ → ユキ", pairs.SelectedRow?.Text);
        Assert.Null(pairs.MoveEntry(pairs.Groups[0], -1));
    });

    [Fact]
    public Task 共通素体が元の組は_素体の名前の頭の字を印にし_同じ名前のアバターとは別のまとまりにする() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar(Aoi, "シロ"), Avatar(Momo, "モモ")],
            BaseGroups = [new AvatarBaseGroup { Name = "シロ" }],
        });
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs =
            [
                new ConversionPair { From = ConversionEnd.Base("シロ"), To = ConversionEnd.Avatar(Momo) },
                Pair(Aoi, Momo),
            ],
        });
        await app.StartAsync();
        var pairs = new ConversionPairEditor(app.Services);
        await pairs.ReloadAsync();

        Assert.Equal(["シロ", "シロ（共通素体）"], pairs.Groups.Select(group => group.Name));
        var baseGroup = pairs.Groups.Single(group => group.From.BaseName == "シロ");
        Assert.Equal("シ", baseGroup.Icon?.Initial);
        Assert.False(baseGroup.Icon?.HasIcon);

        // 畳んだかは端で覚えるので、同じ名前のアバターのまとまりは畳まない
        baseGroup.IsExpanded = false;
        await app.SettleAsync();
        var again = new ConversionPairEditor(app.Services);
        await again.ReloadAsync();
        Assert.False(again.Groups.Single(group => group.From.BaseName == "シロ").IsExpanded);
        Assert.True(again.Groups.Single(group => group.From.AvatarItemId == Aoi).IsExpanded);
    });
}

/// <summary>
/// 一覧（ListBox）の選びの書き戻しを試験で真似る：一覧に出す物から選んでいた物が消えたら、ListBox は選びに null を書く。
/// 試験には ListBox が無いので、<see cref="ConversionPairEditor.Entries"/> の出し入れを見て同じ事をする
/// </summary>
internal static class ConversionPairListBox
{
    public static void Mimic(ConversionPairEditor pairs)
    {
        var shown = pairs.SelectedEntry;
        pairs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionPairEditor.SelectedEntry))
            {
                shown = pairs.SelectedEntry;
            }
        };
        pairs.Entries.CollectionChanged += (_, _) =>
        {
            if (shown is not null && !pairs.Entries.Contains(shown))
            {
                shown = null;
                pairs.SelectedEntry = null;
            }
        };
    }
}
