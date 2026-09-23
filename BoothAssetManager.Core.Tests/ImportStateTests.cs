using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 中断した取り込みの記録。
///
/// **中断は黙って起きる。**閉じた時に何件残っていたかをユーザは覚えていないので、
/// 次に開いたときに思い出せる材料を置く。
/// </summary>
public class ImportStateTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    /// <summary>この本数を超えたら中断する。①の途中で閉じた状況を作る。</summary>
    private int _failAfter = int.MaxValue;

    /// <summary>①で読めない応答を返す商品。BOOTH が不調な状況を作る（#10）。</summary>
    private readonly HashSet<string> _broken = [];

    public ImportStateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new Handler(this)), settings);

        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Handler(ImportStateTests owner) : HttpMessageHandler
    {
        private int _seen;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _seen) > owner._failAfter)
            {
                throw new OperationCanceledException("ここで閉じた");
            }

            var url = request.RequestUri!.ToString();

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);

                // BOOTH の不調の代わり：200 でも読めない応答（5xx は間隔を空けて2回試すので、試験が遅くなる）
                if (owner._broken.Contains(id))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html>メンテナンス中</html>"),
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        { "id": {{id}}, "name": "商品 {{id}}",
                          "url": "https://booth.pm/ja/items/{{id}}",
                          "images": [], "variations": [] }
                        """),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body></body></html>"),
            });
        }
    }

    private string CreateSource(params string[] itemIds)
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        foreach (var itemId in itemIds)
        {
            var path = Path.Combine(folder, $"item_{itemId}.zip");
            File.WriteAllText(path, itemId);
            File.WriteAllText(
                path + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return folder;
    }

    /// <summary>最後まで終われば記録は残さない。残すと次の起動で「中断した」と嘘をつく。</summary>
    [Fact]
    public async Task LeavesNoTraceWhenTheImportFinished()
    {
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        Assert.False(_store.ImportState.Load().HasProgress);
    }

    /// <summary>
    /// **①の途中で閉じたら、どこまで進んだかが残る。**
    /// 「IDは分かったがまだ取得していない商品」の一覧はメモリにしか無いので、
    /// 件数だけでも残しておかないと中断したこと自体が伝わらない。
    /// </summary>
    [Fact]
    public async Task RemembersHowFarItGotWhenInterrupted()
    {
        // 1商品目のJSONだけ通し、2商品目で閉じる
        _failAfter = 1;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _pipeline.RunAsync([CreateSource("111", "222", "333")]));

        var state = _store.ImportState.Load();

        Assert.True(state.HasProgress);
        Assert.Equal(1, state.Done);
        Assert.Equal(3, state.Total);
        Assert.Equal("前回は 1 / 3 件まで進んで中断しました", state.Text);
    }

    /// <summary>0件で中断しても伝えることが無い。</summary>
    [Fact]
    public void SaysNothingWhenThereIsNoProgressToReport()
    {
        Assert.False(new ImportState().HasProgress);
        Assert.False(new ImportState { Done = 0, Total = 0 }.HasProgress);
    }

    /// <summary>
    /// ①が全部終わった後で閉じた場合は出さない。
    ///
    /// この記録が warn しているのは「まだ取得していない商品が残っている」ことで、
    /// ②以降で閉じても失われたものは無い（説明文も画像も次の起動で続きから取る）。
    /// 「2 / 2 件まで進んで中断しました」は、何も起きていないことを知らせているだけになる。
    /// </summary>
    [Fact]
    public void SaysNothingWhenEveryItemWasAlreadyFetched()
    {
        Assert.False(new ImportState { Done = 2, Total = 2 }.HasProgress);
        Assert.True(new ImportState { Done = 1, Total = 2 }.HasProgress);
    }

    /// <summary>
    /// 導ける値は書き出さない。数と文が食い違ったときに
    /// どちらが正しいか分からなくなる（<c>isOwnSpending</c> と同じ理由）。
    /// </summary>
    [Fact]
    public async Task WritesOnlyTheThreeThingsItActuallyHolds()
    {
        _failAfter = 1;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _pipeline.RunAsync([CreateSource("111", "222")]));

        var json = await File.ReadAllTextAsync(_paths.ImportStateFile);

        Assert.Contains("\"done\"", json, StringComparison.Ordinal);
        Assert.Contains("\"total\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stoppedAt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hasProgress", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\"", json, StringComparison.Ordinal);
    }

    /// <summary>次に走らせて最後まで行けば、記録は消える。</summary>
    [Fact]
    public async Task ClearsTheTraceOnTheNextCompleteRun()
    {
        _failAfter = 1;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _pipeline.RunAsync([CreateSource("111", "222")]));

        Assert.True(_store.ImportState.Load().HasProgress);

        _failAfter = int.MaxValue;
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        Assert.False(_store.ImportState.Load().HasProgress);
    }

    // ── BOOTH の不調で①が取れなかった商品（ユーザ判断 2026-09-23・#10「続きから」に残して取り直す）──

    private string CreateSourceIn(string name, string itemId)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"item_{itemId}.zip");
        File.WriteAllText(path, itemId);
        File.WriteAllText(
            path + ":Zone.Identifier",
            $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        return folder;
    }

    /// <summary>
    /// 最後まで走っても、取れなかった商品とそのファイルは記録に残る。
    /// 前は数えるだけで、ファイルは商品にも未確定にも入らず黙って消えたように見えた。
    /// </summary>
    [Fact]
    public async Task KeepsItemsBoothFailedToServeAfterTheRunFinishes()
    {
        _broken.Add("222");
        var source = CreateSource("111", "222");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(1, summary.TemporaryFailures);
        var state = _store.ImportState.Load();
        Assert.True(state.HasProgress);
        Assert.False(state.WasInterrupted);
        var unfetched = Assert.Single(state.UnfetchedItems);
        Assert.Equal("222", unfetched.ItemId);
        Assert.Equal([Path.Combine(source, "item_222.zip")], unfetched.PathList);
        Assert.Equal(
            "前回の取り込みで 1 件は BOOTH の不調で取れませんでした。少し待ってから「続きから進む」で取り直せます",
            state.Text);

        // 「続きから進む」は、最後まで走った回なら取れなかったファイルだけを積む（全部を走査し直さない）
        Assert.Equal([Path.Combine(source, "item_222.zip")], state.ResumeTargets);
    }

    [Fact]
    public async Task ResumingFetchesTheFailedItemAndClearsTheRecord()
    {
        _broken.Add("222");
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        _broken.Clear();
        await _pipeline.RunAsync(_store.ImportState.Load().ResumeTargets);

        Assert.NotNull(await _store.Items.LoadAsync("222"));
        Assert.False(_store.ImportState.Load().HasProgress);
    }

    /// <summary>別のフォルダを取り込んだだけで消すと、取り直す手が無くなる。</summary>
    [Fact]
    public async Task AnotherImportCarriesTheFailedItemsOver()
    {
        _broken.Add("222");
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        await _pipeline.RunAsync([CreateSourceIn("other", "333")]);

        Assert.Equal("222", Assert.Single(_store.ImportState.Load().UnfetchedItems).ItemId);
    }

    /// <summary>ハッシュは控えに載っているが、取り直すまでは監視から「新しいファイル」に見える。</summary>
    [Fact]
    public async Task WatchCountsFailedFilesAsNewUntilFetched()
    {
        _broken.Add("222");
        var source = CreateSource("111", "222");
        await _pipeline.RunAsync([source]);

        var watch = await new FolderWatch(_store).FindNewAsync([source]);

        Assert.Equal([Path.Combine(source, "item_222.zip")], watch.NewFiles);
    }

    [Fact]
    public void InterruptedTextMentionsFailuresToo()
    {
        var state = new ImportState
        {
            Done = 1,
            Total = 3,
            Unfetched = [new UnfetchedItem { ItemId = "222", Paths = [@"D:\a.zip"] }],
        };

        Assert.Equal("前回は 1 / 3 件まで進んで中断しました（うち 1 件は BOOTH の不調で取れませんでした）", state.Text);
    }

    [Fact]
    public async Task WritesFailedItemsButNotWhatCanBeDerived()
    {
        _broken.Add("222");
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        var json = await File.ReadAllTextAsync(_paths.ImportStateFile);

        Assert.Contains("\"unfetched\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("resumeTargets", json, StringComparison.Ordinal);
        Assert.DoesNotContain("wasInterrupted", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pathList", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unfetchedItems", json, StringComparison.Ordinal);
    }
}
