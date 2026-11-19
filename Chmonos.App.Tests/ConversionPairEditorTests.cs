using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 人が足す変換の組の画面（<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）：アバターの管理の「変換」の
/// 「追加した組」（元 → 先・メモ・×・組を追加）、アバターの詳細の読むだけの札、商品ページの「変換経由」の吹き出し。名前・ID は作り物。
/// </summary>
public class ConversionPairEditorTests
{
    private const string Aoi = "9800401";
    private const string Momo = "9800402";
    private const string ClothingId = "9900891";

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
    };

    private static Task SaveRegistryAsync(TestApp app) => app.Store.Avatars.SaveAsync(new AvatarRegistry
    {
        Entries = [Avatar(Aoi, "アオイ"), Avatar(Momo, "モモ")],
        BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
    });

    private static async Task<AvatarsViewModel> OpenAvatarsAsync(TestApp app)
    {
        await SaveRegistryAsync(app);
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == Aoi);
        return avatars;
    }

    private static ConversionPair Pair(ConversionEnd from, ConversionEnd to) => new() { From = from, To = to };

    [Fact]
    public Task 変換で元に共通素体と先にアバターを選んで足し_行に元から先で出し_外すと消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        // 元と先のどちらにも、アバターと「（共通素体）」を添えた共通素体を出す
        Assert.Contains("アオイ", pairs.Suggestions);
        Assert.Contains("作り物素体（共通素体）", pairs.Suggestions);

        pairs.FromText = "作り物素体（共通素体）";
        pairs.ToText = "モモ";
        await pairs.AddAsync();
        pairs.FromText = "アオイ";
        pairs.ToText = "作り物素体（共通素体）";
        await pairs.AddAsync();

        Assert.Equal(["作り物素体（共通素体） → モモ", "アオイ → 作り物素体（共通素体）"], pairs.Rows.Select(row => row.Text));
        Assert.Equal("追加した組（2）", pairs.HeaderText);
        Assert.Equal(
            [Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar(Momo)), Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Base("作り物素体"))],
            app.Store.ConversionPairs.Load().Pairs);

        // 同じ元から続けて足せるように、元は残して先だけ空にする
        Assert.Equal("アオイ", pairs.FromText);
        Assert.Equal(string.Empty, pairs.ToText);

        // 足した組を選んで右に出す（同じ画面の共通素体の「追加」と同じ）
        Assert.Equal("アオイ → 作り物素体（共通素体）", pairs.SelectedRow?.Text);
        Assert.True(avatars.ShowsConversionPairDetail);

        pairs.Rows[0].RemoveCommand.Execute(null);
        await UiThread.Until(() => pairs.Rows.Count == 1, "外した行が消える");
        Assert.Equal(Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Base("作り物素体")), Assert.Single(app.Store.ConversionPairs.Load().Pairs));
    });

    [Fact]
    public Task 候補に無い_同じ物同士_既にある組は足さずに理由を言う() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile { Pairs = [Pair(ConversionEnd.Avatar(Momo), ConversionEnd.Avatar(Aoi))] });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        pairs.FromText = "作り物の知らないアバター";
        pairs.ToText = "アオイ";
        await pairs.AddAsync();
        Assert.Equal("候補からアバターか共通素体を選んでください。", pairs.Notice.Text);

        pairs.FromText = "アオイ";
        await pairs.AddAsync();
        Assert.Equal("元と先に同じ物は選べません。", pairs.Notice.Text);

        pairs.FromText = "モモ";
        await pairs.AddAsync();
        Assert.Equal("「モモ → アオイ」は既にあります。", pairs.Notice.Text);
        Assert.Same(pairs.Rows[0], pairs.SelectedRow);

        // 打ち直すと消える
        pairs.ToText = "アオ";
        Assert.False(pairs.Notice.HasText);
        Assert.Single(app.Store.ConversionPairs.Load().Pairs);
    });

    [Fact]
    public Task 行のメモは押さずに組へ残り_ツールは残して行は作り直さない() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)) with { Tool = "作り物のツール" }],
        });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;
        var row = Assert.Single(pairs.Rows);
        Assert.Equal(string.Empty, row.MemoText);

        row.MemoText = "袖は直す";
        Assert.True(pairs.IsMemoPending);
        await avatars.FlushPendingWritesAsync();

        var saved = Assert.Single(app.Store.ConversionPairs.Load().Pairs);
        Assert.Equal("袖は直す", saved.Memo);
        Assert.Equal("作り物のツール", saved.Tool);
        Assert.Same(row, Assert.Single(pairs.Rows));

        // アバターの詳細の札は、メモを吹き出しに持つ
        Assert.Equal("袖は直す", Assert.Single(avatars.ConversionPairSummary.Chips).Memo);
    });

    [Fact]
    public Task メモを打ってすぐ別の組を外しても_打ったメモは画面にもファイルにも残る() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)), Pair(ConversionEnd.Avatar(Momo), ConversionEnd.Avatar(Aoi))],
        });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        // 0.8 秒の待ちの間に外す（待ちを書き切る前に作り直すと、行が古いメモに戻っていた）
        pairs.Rows[0].MemoText = "袖は直す";
        await pairs.RemoveAsync(pairs.Rows[1]);

        Assert.Equal("袖は直す", Assert.Single(pairs.Rows).MemoText);
        Assert.Equal("袖は直す", Assert.Single(app.Store.ConversionPairs.Load().Pairs).Memo);
    });

    /// <summary>
    /// 足すのは「追加」を押した時だけ（ユーザ判断 2026-11-20：先の候補を選んだ瞬間に足されていた）。候補を決めると欄の
    /// <c>CommitCommand</c> が走るので、元と先の欄に付けない事と、Enter が「追加」と同じ命令に結ばれている事を画面の元の文で見る
    /// （試験の中では画面を作れない）。候補が出ている間の Enter は欄が受けて候補を決めるだけ（<c>SuggestBox</c>）
    /// </summary>
    [Fact]
    public void 組を追加の欄は候補を決めても足さず_追加のボタンとEnterだけが足す()
    {
        var document = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(ViewsFolder(), "AvatarsView.xaml"));
        System.Xml.Linq.XElement ById(string id) => document.Descendants()
            .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId" && attribute.Value == id));

        foreach (var id in new[] { "AvatarsConversionPairFrom", "AvatarsConversionPairTo" })
        {
            var box = ById(id);
            Assert.Equal("SuggestBox", box.Name.LocalName);
            Assert.Null(box.Attribute("CommitCommand"));
            Assert.Contains(box, ById("AvatarsConversionPairAddArea").Descendants());
        }

        Assert.Equal("{Binding AddCommand}", (string?)ById("AvatarsConversionPairAdd").Attribute("Command"));
        var enter = Assert.Single(ById("AvatarsConversionPairAddArea").Descendants(), element => element.Name.LocalName == "KeyBinding");
        Assert.Equal("Enter", (string?)enter.Attribute("Key"));
        Assert.Null(enter.Attribute("Modifiers"));
        Assert.Equal("{Binding AddCommand}", (string?)enter.Attribute("Command"));
    }

    [Fact]
    public Task 欄に元と先を入れただけでは足さず_追加で足す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        pairs.FromText = "アオイ";
        pairs.ToText = "モモ";
        await app.SettleAsync();
        Assert.Empty(app.Store.ConversionPairs.Load().Pairs);
        Assert.Empty(pairs.Rows);

        pairs.AddCommand.Execute(null);
        await UiThread.Until(() => pairs.Rows.Count == 1, "追加で足す");
        Assert.Equal(Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)), Assert.Single(app.Store.ConversionPairs.Load().Pairs));
    });

    [Fact]
    public Task 組を選んでメモを打ちすぐ別の組を選んで打っても_両方の組にメモが残る() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)), Pair(ConversionEnd.Avatar(Momo), ConversionEnd.Avatar(Aoi))],
        });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        // 右の欄は選んだ組のメモに結ぶ。0.8 秒の待ちは1つしか持てないので、選び直す前に書き切らないと前の組に打った文が消える
        pairs.SelectedRow = pairs.Rows[0];
        pairs.SelectedRow.MemoText = "袖は直す";
        pairs.SelectedRow = pairs.Rows[1];
        pairs.SelectedRow.MemoText = "裾を詰める";
        await avatars.FlushPendingWritesAsync();
        await app.SettleAsync();

        Assert.Equal(["袖は直す", "裾を詰める"], app.Store.ConversionPairs.Load().Pairs.Select(pair => pair.Memo));
        Assert.Equal("袖は直す", pairs.Rows[0].MemoPreview);
    });

    [Fact]
    public Task 選んだ組を外すと次の組を選び_最後の組を外すと選びが外れる() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)), Pair(ConversionEnd.Avatar(Momo), ConversionEnd.Avatar(Aoi))],
        });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var pairs = avatars.ConversionPairs;

        pairs.SelectedRow = pairs.Rows[0];
        await pairs.RemoveAsync(pairs.SelectedRow);
        Assert.Equal("モモ → アオイ", pairs.SelectedRow?.Text);

        await pairs.RemoveAsync(pairs.SelectedRow!);
        Assert.Null(pairs.SelectedRow);
        Assert.False(avatars.ShowsConversionPairDetail);
        Assert.Empty(app.Store.ConversionPairs.Load().Pairs);
    });

    [Fact]
    public Task 組の元の共通素体を押すと_共通素体の見方でその素体を選ぶ() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar(Momo))],
        });
        var avatars = await OpenAvatarsAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        var row = Assert.Single(avatars.ConversionPairs.Rows);
        avatars.ConversionPairs.SelectedRow = row;

        Assert.True(row.OpenFromCommand.CanExecute(null));
        row.OpenFromCommand.Execute(null);

        Assert.True(avatars.IsBaseMode);
        Assert.Equal("作り物素体", avatars.SelectedBase?.Name);
    });

    /// <summary>試験を組んだ所ではなく、元の文の置き場から引く（作業用のフォルダへ組んでも同じ物を読む）。</summary>
    private static string ViewsFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(here))!, "Chmonos.App", "Views");

    [Fact]
    public Task アバターの詳細はそのアバターが元か先にある組だけを読むだけで並べ_変換で編集で変換へ移る() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs =
            [
                Pair(ConversionEnd.Avatar(Aoi), ConversionEnd.Avatar(Momo)),
                Pair(ConversionEnd.Avatar(Momo), ConversionEnd.Avatar(Aoi)),
                Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar(Momo)),
            ],
        });
        var avatars = await OpenAvatarsAsync(app);
        var summary = avatars.ConversionPairSummary;

        Assert.Equal(["アオイ → モモ", "モモ → アオイ"], summary.Chips.Select(chip => chip.Text));

        summary.EditCommand.Execute(null);
        Assert.True(avatars.IsConversionMode);
        Assert.Equal(3, avatars.ConversionPairs.Rows.Count);

        // 「変換」で外すと、詳細の札も読み直す
        await avatars.ConversionPairs.RemoveAsync(avatars.ConversionPairs.Rows[1]);
        Assert.Equal(["アオイ → モモ"], summary.Chips.Select(chip => chip.Text));
    });

    [Fact]
    public Task 組から来た変換経由の札は_吹き出しで足した組からと言う() => TestApp.Run(async app =>
    {
        await SaveRegistryAsync(app);
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [new ConversionPair { From = ConversionEnd.Avatar(Aoi), To = ConversionEnd.Avatar(Momo) }],
        });
        var clothing = Make.Item(ClothingId, "作り物の衣装");
        clothing = clothing with
        {
            Local = clothing.Local with
            {
                Avatars = [new AvatarLink { AvatarItemId = Aoi, Name = "アオイ", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(clothing);
        var main = await app.StartAsync();

        var page = new ItemViewModel(clothing, app.Services, main, main.Thumbnails);

        var row = Assert.Single(page.ViaConversionAvatars);
        Assert.Equal("モモ", row.Name);
        Assert.Contains("「アオイ」用を、追加した組で変換できる見込みです。", row.ViaTooltip, StringComparison.Ordinal);
    });
}
