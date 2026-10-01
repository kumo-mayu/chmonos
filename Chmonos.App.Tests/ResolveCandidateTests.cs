using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の画面：候補の行（同じ場所にあった商品）と、一覧の札「自動候補」の数。
///
/// 前は「商品に結んだファイルを同じ名前で上書きして取り込み直す」保存先を作って、画面を撮って確かめていた。
/// 候補の行は未確定の記録（<see cref="UnresolvedFile.SamePathItemIds"/>）と手元の商品から組むだけで、通信はしない。
/// </summary>
public class ResolveCandidateTests
{
    private static UnresolvedFile Unresolved(
        string path,
        IReadOnlyList<string>? samePath = null,
        IReadOnlyList<string>? candidates = null,
        bool broken = false) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        SamePathItemIds = samePath ?? [],
        CandidateItemIds = candidates ?? [],
        ArchiveBroken = broken,
    };

    private static UnresolvedRow RowOf(UnresolvedFile file) => new()
    {
        File = file,
        FileName = System.IO.Path.GetFileName(file.Paths[0]),
        SizeText = "3 B",
        DirectoryText = System.IO.Path.GetDirectoryName(file.Paths[0]) ?? string.Empty,
    };

    private static async Task<ResolveViewModel> OpenResolveAsync(TestApp app, params UnresolvedFile[] files)
    {
        await app.Store.Unresolved.SaveAsync([.. files]);
        var main = await app.StartAsync();
        var resolve = new ResolveViewModel(app.Services, main);
        await UiThread.Until(() => resolve.IsLoaded, "未確定の一覧の読み込みが済む");
        return resolve;
    }

    // ---- 札「自動候補」の数 ----

    [Fact]
    public void 候補が無くても_札は同じ形で0件と出す()
    {
        var row = RowOf(Unresolved(@"D:\files\sample.zip"));

        Assert.Equal(0, row.CandidateCount);
        Assert.False(row.HasCandidates);
        Assert.Equal("自動候補:0 件", row.CandidateText);
    }

    [Fact]
    public void 同じ場所にあった商品も_自動候補に数える()
    {
        // 数えないと、上書きした物の行に札が出ず、候補があることに一覧で気付けない
        var row = RowOf(Unresolved(@"D:\files\sample.zip", samePath: ["1000001"]));

        Assert.Equal(1, row.CandidateCount);
        Assert.True(row.HasCandidates);
        Assert.Equal("自動候補:1 件", row.CandidateText);
    }

    [Fact]
    public void 手掛かりの候補と同じ場所の候補が同じ商品なら_1件と数える()
    {
        var row = RowOf(Unresolved(
            @"D:\files\sample.zip", samePath: ["1000001", "1000002"], candidates: ["1000002", "1000003"]));

        Assert.Equal(3, row.CandidateCount);
        Assert.Equal("自動候補:3 件", row.CandidateText);
    }

    [Fact]
    public void 壊れたzipの札は_取り込みが記録に書いた印で出す()
    {
        Assert.True(RowOf(Unresolved(@"D:\files\sample.zip", broken: true)).IsBrokenArchive);
        Assert.False(RowOf(Unresolved(@"D:\files\sample.zip")).IsBrokenArchive);
    }

    // ---- 候補の行 ----

    [Fact]
    public Task 同じ場所にあった商品を_名前と理由を付けて候補の先頭に出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var path = app.NewFile("costume.zip");

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: ["1000001"], candidates: ["1000009"]));

        Assert.Collection(
            resolve.Candidates,
            same =>
            {
                Assert.Equal("1000001", same.ItemId);
                Assert.Equal("作り物の衣装", same.Title);
                Assert.Equal("前に同じ場所にあったzipを、この商品に登録していました", same.Detail);
                Assert.Equal("手元の商品の記録", same.Source);
            },
            hinted =>
            {
                Assert.Equal("1000009", hinted.ItemId);
                Assert.Equal("商品ID 1000009", hinted.Title);
                Assert.Equal("取り込み時に読み取った情報", hinted.Source);
            });

        // 候補を出すだけでは BOOTH へ行かない（通信するのは「自動検索」と「確認」を押したとき）
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task zipでないファイルの候補は_理由でファイルと言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var path = app.NewFile("costume.unitypackage");

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: ["1000001"]));

        var candidate = Assert.Single(resolve.Candidates);
        Assert.Equal("前に同じ場所にあったファイルを、この商品に登録していました", candidate.Detail);
    });

    [Fact]
    public Task 同じ商品が両方の候補にあれば_同じ場所の行の1つだけを出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var path = app.NewFile("costume.zip");

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: ["1000001"], candidates: ["1000001"]));

        // 同じ場所にあった商品の行の方が、名前と理由が分かる
        var candidate = Assert.Single(resolve.Candidates);
        Assert.Equal("手元の商品の記録", candidate.Source);
    });

    [Fact]
    public Task 手元から消したBOOTHの商品は_商品IDで候補に出す() => TestApp.Run(async app =>
    {
        // 消した後でも、選べば取り直して登録できる
        var path = app.NewFile("costume.zip");

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: ["1000007"]));

        var candidate = Assert.Single(resolve.Candidates);
        Assert.Equal("商品ID 1000007", candidate.Title);
        Assert.True(candidate.HasBoothPage);
    });

    [Fact]
    public Task 手元から消したBOOTHに無い商品は_候補に出さない() => TestApp.Run(async app =>
    {
        // 仮のIDはBOOTHに無いので、選んでも登録する先が無い
        var path = app.NewFile("costume.zip");
        var localId = LocalItemId.For(Make.HashOf("自分で名付けた商品"));

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: [localId]));

        Assert.Empty(resolve.Candidates);
    });

    [Fact]
    public Task 手元にあるBOOTHに無い商品は_候補に出すが_BOOTHのページは無い() => TestApp.Run(async app =>
    {
        var localId = LocalItemId.For(Make.HashOf("自分で名付けた商品"));
        await app.AddItemAsync(new ItemRecord { Id = localId, Local = new LocalBlock { DisplayName = "自分で名付けた商品" } });
        var path = app.NewFile("costume.zip");

        var resolve = await OpenResolveAsync(app, Unresolved(path, samePath: [localId]));

        var candidate = Assert.Single(resolve.Candidates);
        Assert.Equal("自分で名付けた商品", candidate.Title);
        Assert.False(candidate.HasBoothPage);
    });

    [Fact]
    public Task 行を選び直すと_候補はその行の物に入れ替わる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var first = app.NewFile(@"a\first.zip");
        var second = app.NewFile(@"b\second.zip");

        var resolve = await OpenResolveAsync(
            app, Unresolved(first, samePath: ["1000001"]), Unresolved(second));

        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        Assert.Single(resolve.Candidates);

        resolve.Selected = resolve.Files.Single(row => row.FileName == "second.zip");
        Assert.Empty(resolve.Candidates);
    });

    [Fact]
    public Task 一覧の行の札は_記録の候補の数を出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var withCandidate = app.NewFile(@"a\with.zip");
        var without = app.NewFile(@"b\without.zip");

        var resolve = await OpenResolveAsync(
            app, Unresolved(withCandidate, samePath: ["1000001"]), Unresolved(without, broken: true));

        Assert.Equal("自動候補:1 件", resolve.Files.Single(row => row.FileName == "with.zip").CandidateText);

        var plain = resolve.Files.Single(row => row.FileName == "without.zip");
        Assert.Equal("自動候補:0 件", plain.CandidateText);
        Assert.True(plain.IsBrokenArchive);
    });
}
