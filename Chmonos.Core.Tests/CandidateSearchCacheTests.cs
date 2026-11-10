using System.Text.Json;
using Chmonos.Core.Resolution;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定の自動検索の控え（<c>unresolved-searches.json</c>・ユーザ判断 2026-11-11）。
/// 先回りは無いときだけ書き、再検索は在るときだけ置き換え、届かなかった結果は書かず、片付いたファイルの分は消える。
/// 保存先の直下の .json なので、引越し・書き出しと戻す・形式を上げる前の控えに付いていく。
/// </summary>
public sealed class CandidateSearchCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-searches-" + Guid.NewGuid().ToString("N")[..8]);

    private string Root => Path.Combine(_dir, "store");

    private readonly AppPaths _paths;
    private readonly CandidateSearchCache _cache;

    public CandidateSearchCacheTests()
    {
        Directory.CreateDirectory(Root);
        _paths = new AppPaths(Root);
        _cache = new CandidateSearchCache(new DataStore(_paths).UnresolvedSearches);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly DateTimeOffset First = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset Later = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(9));

    private static ResolutionProposal Found(params string[] ids) => new(
        [.. ids.Select(id => new ResolutionCandidate { ItemId = id, Name = $"作り物の商品{id}", Score = 8, Reasons = ["商品名の語"] })],
        BoothUnreachable: false);

    [Fact]
    public async Task 先回りは控えの無い対象にだけ書き_古くなっても書き直さない()
    {
        Assert.True(await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], Found("100"), First));
        Assert.False(await _cache.AddIfMissingAsync(@"d:\DL\衣装.zip", ["h1"], Found("200"), Later));

        var entry = Assert.IsType<CandidateSearchEntry>(_cache.Find(@"D:\dl\衣装.zip"));
        Assert.Equal("100", Assert.Single(entry.Candidates).ItemId);
        Assert.Equal(First, entry.SearchedAt);
    }

    [Fact]
    public async Task 再検索は控えのある対象だけ置き換え_控えの無い対象には書かない()
    {
        Assert.False(await _cache.ReplaceIfPresentAsync(@"D:\dl\靴.zip", ["h2"], Found("300"), Later));
        Assert.Null(_cache.Find(@"D:\dl\靴.zip"));
        Assert.False(File.Exists(_paths.UnresolvedSearchesFile));

        await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], Found("100"), First);
        Assert.True(await _cache.ReplaceIfPresentAsync(@"D:\dl\衣装.zip", ["h1", "h3"], Found("200", "201"), Later));

        var entry = _cache.Find(@"D:\dl\衣装.zip")!;
        Assert.Equal(["200", "201"], entry.Candidates.Select(candidate => candidate.ItemId));
        Assert.Equal(["h1", "h3"], entry.Files);
        Assert.Equal(Later, entry.SearchedAt);
    }

    [Fact]
    public async Task BOOTHに届かなかった結果は_先回りでも再検索でも書かない()
    {
        var unreachable = new ResolutionProposal([], BoothUnreachable: true);
        Assert.False(await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], unreachable, First));
        Assert.Null(_cache.Find(@"D:\dl\衣装.zip"));

        await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], Found("100"), First);
        Assert.False(await _cache.ReplaceIfPresentAsync(@"D:\dl\衣装.zip", ["h1"], unreachable, Later));
        Assert.Equal("100", Assert.Single(_cache.Find(@"D:\dl\衣装.zip")!.Candidates).ItemId);
    }

    [Fact]
    public async Task 決まったファイルを外し_持ち主が居なくなった控えは消える()
    {
        await _cache.AddIfMissingAsync(@"D:\dl\中身.zip", ["h1", "h2"], Found("100"), First);
        await _cache.AddIfMissingAsync(@"D:\dl\靴.zip", ["h3"], Found("300"), First);

        await _cache.ForgetAsync(["H1"]);
        Assert.Equal(["h2"], _cache.Find(@"D:\dl\中身.zip")!.Files);

        await _cache.ForgetAsync(["h2"]);
        Assert.Null(_cache.Find(@"D:\dl\中身.zip"));
        Assert.NotNull(_cache.Find(@"D:\dl\靴.zip"));

        // 未確定に残っていない持ち主の突き合わせ（全件を読み直したとき・先回りの頭）
        await _cache.KeepOnlyAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "h9" });
        Assert.Null(_cache.Find(@"D:\dl\靴.zip"));
    }

    [Fact]
    public async Task 書いた控えは人が読める形で_計算で出せる値と版の決まりを守る()
    {
        await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], Found("100"), First);

        var text = await File.ReadAllTextAsync(_paths.UnresolvedSearchesFile);
        using var json = JsonDocument.Parse(text);
        var rootObject = json.RootElement;

        // いちばん外側はオブジェクトで、最初の欄に形式の版（新しいファイルを足すだけなので版は上げない）
        Assert.Equal("formatVersion", rootObject.EnumerateObject().First().Name);
        Assert.Equal(StoreFormat.Current, rootObject.GetProperty("formatVersion").GetInt32());

        var entry = rootObject.GetProperty("searches")[0];
        Assert.Equal(@"D:\dl\衣装.zip", entry.GetProperty("target").GetString());
        Assert.Equal("h1", entry.GetProperty("files")[0].GetString());
        var candidate = entry.GetProperty("candidates")[0];
        Assert.Equal("100", candidate.GetProperty("itemId").GetString());

        // 確度が高いかは点数から計算で出るので書かない
        Assert.False(candidate.TryGetProperty("isStrong", out _));
        Assert.Contains("\n", text);
    }

    [Fact]
    public void 手で書いた控えの欠けた配列は空として読む()
    {
        File.WriteAllText(_paths.UnresolvedSearchesFile,
            """{ "formatVersion": 2, "searches": [ { "target": "D:\\dl\\a.zip", "files": null, "candidates": null } ] }""");

        var entry = Assert.IsType<CandidateSearchEntry>(_cache.Find(@"D:\dl\a.zip"));
        Assert.Empty(entry.Files);
        Assert.Empty(entry.Candidates);
    }

    [Fact]
    public async Task 控えは引越し_書き出しと戻す_形式を上げる前の控えに付いていく()
    {
        await _cache.AddIfMissingAsync(@"D:\dl\衣装.zip", ["h1"], Found("100"), First);
        File.WriteAllText(Path.Combine(Root, "settings.json"), "{}");
        var name = Path.GetFileName(_paths.UnresolvedSearchesFile);

        // 書き出しと戻す（すべてを戻す）
        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Root, zip, includeImages: false);
        var restored = Path.Combine(_dir, "restored");
        BackupArchive.Restore(zip, restored);
        Assert.True(File.Exists(Path.Combine(restored, name)));

        // 形式を上げる前の控え
        var backup = StoreFormat.Backup(Root, fromVersion: 1, DateTimeOffset.Now);
        Assert.True(File.Exists(Path.Combine(backup, name)));

        // 引越し
        var moved = Path.Combine(_dir, "moved");
        Assert.True(StoreMover.Move(Root, moved).Succeeded);
        Assert.True(File.Exists(Path.Combine(moved, name)));
    }
}
