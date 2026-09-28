using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ネットにつながっていないときの取り込み（ユーザ判断 2026-09-29）。
///
/// 1件ごとに再試行で長く待ち、全件を回るので止まって見えた。
/// **応答の無い失敗が3件続いたら、その回の問い合わせを打ち切り、残りは「続きから」に残す。**
/// 通信はしない（作り物の応答）。再試行の待ちは差し替えて待たない。
/// </summary>
public class ImportOfflineTests : IDisposable
{
    private enum Answer
    {
        Ok,

        /// <summary>接続できない（ネットにつながっていない）。</summary>
        Unreachable,

        /// <summary>429（混雑）。BOOTH は応答している。</summary>
        Busy,
    }

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    /// <summary>①で n 番目に問い合わせた商品への答え。足りない分は Ok。</summary>
    private readonly List<Answer> _jsonAnswers = [];

    /// <summary>②で n 番目に問い合わせた商品への答え。足りない分は Ok。</summary>
    private readonly List<Answer> _htmlAnswers = [];

    private readonly List<string> _jsonAsked = [];
    private readonly List<string> _htmlAsked = [];
    private int _jsonRequests;
    private int _htmlRequests;

    public ImportOfflineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        // 画像は取らない設定にする（問い合わせを①②だけにして数えやすくする）
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(
            new HttpClient(new Handler(this)),
            settings,
            delay: (_, _) => Task.CompletedTask);

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

    private sealed class Handler(ImportOfflineTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var isJson = url.EndsWith(".json", StringComparison.Ordinal);
            var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);

            lock (owner)
            {
                var asked = isJson ? owner._jsonAsked : owner._htmlAsked;
                var answers = isJson ? owner._jsonAnswers : owner._htmlAnswers;
                if (isJson)
                {
                    owner._jsonRequests++;
                }
                else
                {
                    owner._htmlRequests++;
                }

                // 何番目の商品かで答えを決める（走査の順に左右されないように）
                if (!asked.Contains(id))
                {
                    asked.Add(id);
                }

                var index = asked.IndexOf(id);
                var answer = index < answers.Count ? answers[index] : Answer.Ok;

                if (answer == Answer.Unreachable)
                {
                    throw new HttpRequestException("つながらない");
                }

                if (answer == Answer.Busy)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isJson
                    ? $$"""
                        { "id": {{id}}, "name": "商品 {{id}}",
                          "url": "https://booth.pm/ja/items/{{id}}",
                          "images": [], "variations": [] }
                        """
                    : "<html><body></body></html>"),
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

    /// <summary>1件あたりの問い合わせの数（最初の1本と、間を空けた再試行2本）。</summary>
    private const int AttemptsPerItem = 3;

    [Fact]
    public async Task StopsAskingAfterThreeUnreachableItemsInARow()
    {
        _jsonAnswers.AddRange(Enumerable.Repeat(Answer.Unreachable, 5));
        var source = CreateSource("111", "222", "333", "444", "555");

        var summary = await _pipeline.RunAsync([source]);

        // 3件目で打ち切り、4件目・5件目は問い合わせない
        Assert.Equal(3, _jsonAsked.Count);
        Assert.Equal(3 * AttemptsPerItem, _jsonRequests);
        Assert.True(summary.StoppedOffline);
        Assert.Equal(5, summary.TemporaryFailures);
    }

    /// <summary>打ち切った分も「続きから」に残り、つながってから押せば取れる。</summary>
    [Fact]
    public async Task KeepsWhatItStoppedOnForResuming()
    {
        _jsonAnswers.AddRange(Enumerable.Repeat(Answer.Unreachable, 5));
        var source = CreateSource("111", "222", "333", "444", "555");

        await _pipeline.RunAsync([source]);

        var state = _store.ImportState.Load();
        Assert.True(state.HasProgress);
        Assert.True(state.StoppedOffline);
        Assert.Equal(
            ["111", "222", "333", "444", "555"],
            state.UnfetchedItems.Select(item => item.ItemId).ToArray());
        Assert.Contains("ネットにつながっていない", state.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("不調", state.Text, StringComparison.Ordinal);

        // つながった
        _jsonAnswers.Clear();
        _jsonAsked.Clear();
        await _pipeline.RunAsync(state.ResumeTargets);

        foreach (var id in new[] { "111", "222", "333", "444", "555" })
        {
            Assert.NotNull(await _store.Items.LoadAsync(id));
        }

        Assert.False(_store.ImportState.Load().HasProgress);
    }

    /// <summary>「続けて」なので、途中で1件でも取れたら数え直す。</summary>
    [Fact]
    public async Task CountsAgainAfterAnItemIsFetched()
    {
        _jsonAnswers.AddRange(
            [Answer.Unreachable, Answer.Unreachable, Answer.Ok, Answer.Unreachable, Answer.Unreachable]);
        var source = CreateSource("111", "222", "333", "444", "555");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(5, _jsonAsked.Count);
        Assert.False(summary.StoppedOffline);
        Assert.Equal(4, summary.TemporaryFailures);

        var state = _store.ImportState.Load();
        Assert.False(state.StoppedOffline);
        Assert.Equal(4, state.UnfetchedItems.Count);
    }

    /// <summary>429（混雑）は BOOTH が応答しているので、打ち切りの数に入れない（今の扱いのまま）。</summary>
    [Fact]
    public async Task DoesNotCountBusyAnswers()
    {
        _jsonAnswers.AddRange(Enumerable.Repeat(Answer.Busy, 4));
        var source = CreateSource("111", "222", "333", "444");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(4, _jsonAsked.Count);
        Assert.False(summary.StoppedOffline);
        Assert.Contains("不調", _store.ImportState.Load().Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 429 を挟むと「続けて」ではない。応答の無い失敗を2件・混雑・2件と並べても打ち切らない。
    /// </summary>
    [Fact]
    public async Task BusyAnswerBreaksTheStreak()
    {
        _jsonAnswers.AddRange(
            [Answer.Unreachable, Answer.Unreachable, Answer.Busy, Answer.Unreachable, Answer.Unreachable]);
        var source = CreateSource("111", "222", "333", "444", "555");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(5, _jsonAsked.Count);
        Assert.False(summary.StoppedOffline);
    }

    /// <summary>
    /// ②（説明文）で止めた商品は①が済んでいて、取れなかった商品に載らない。
    /// 対象を残して、「続きから進む」で走査し直せば②へ戻る。
    /// </summary>
    [Fact]
    public async Task StopsThePageStageTooAndResumesIt()
    {
        _htmlAnswers.AddRange(Enumerable.Repeat(Answer.Unreachable, 5));
        var source = CreateSource("111", "222", "333", "444", "555");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(5, _jsonAsked.Count);
        Assert.Equal(3, _htmlAsked.Count);
        Assert.Equal(3 * AttemptsPerItem, _htmlRequests);
        Assert.True(summary.StoppedOffline);

        var state = _store.ImportState.Load();
        Assert.True(state.HasProgress);
        Assert.Empty(state.UnfetchedItems);
        Assert.Equal([source], state.ResumeTargets);

        // つながった：説明の無い商品を②で取り直す
        _htmlAnswers.Clear();
        _htmlAsked.Clear();
        await _pipeline.RunAsync(state.ResumeTargets);

        Assert.Equal(5, _htmlAsked.Count);
        foreach (var id in new[] { "111", "222", "333", "444", "555" })
        {
            Assert.True(File.Exists(_paths.ItemHtmlFile(id)));
        }

        Assert.False(_store.ImportState.Load().HasProgress);
    }

    [Fact]
    public async Task WritesThatItStoppedOffline()
    {
        _jsonAnswers.AddRange(Enumerable.Repeat(Answer.Unreachable, 3));
        await _pipeline.RunAsync([CreateSource("111", "222", "333")]);

        var json = await File.ReadAllTextAsync(_paths.ImportStateFile);

        Assert.Contains("\"stoppedOffline\": true", json, StringComparison.Ordinal);
    }
}
