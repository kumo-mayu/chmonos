using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定に残した BOOTH の「無い」の答え（<see cref="UnresolvedFile.NotOnBooth"/>）を、人が「情報を確認」で聞き直した答えに合わせる（ユーザ判断 2026-10-06）。
/// 今も無ければ日時を新しくし、公開されていれば外す（外さないと一覧の札が「BOOTHで非公開」のまま残る）。
/// </summary>
public sealed class NotOnBoothAnswerTests : IDisposable
{
    private const string MarkedId = "9900002";
    private const string OtherId = "9900004";

    private static readonly DateTimeOffset LongAgo = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;
    private readonly Handler _handler = new();

    public NotOnBoothAnswerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-notonbooth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_handler), settings, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths, settings), settings);
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
    }

    /// <summary>既定は404。<see cref="Published"/> なら商品JSONを返し、<see cref="Down"/> なら 503。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public bool Published { get; set; }

        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            if (!Published)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {
                      "id": {{MarkedId}},
                      "name": "作り物の季節の衣装",
                      "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                      "images": [],
                      "variations": [ { "id": 1, "name": null, "price": 500 } ]
                    }
                    """),
            });
        }
    }

    private static UnresolvedFile Row(string hash, string? markedId) => new()
    {
        Hash = hash,
        Paths = [$@"D:\作り物\{hash}.zip"],
        SizeBytes = 1,
        ModifiedAtUtc = LongAgo,
        FirstSeenAt = LongAgo,
        CandidateItemIds = [MarkedId],
        NotOnBooth = markedId is null ? null : new BoothNotFoundNote { ItemId = markedId, CheckedAt = LongAgo },
    };

    private UnresolvedFile Load(string hash) => _store.Unresolved.Load().Single(file => file.Hash == hash);

    [Fact]
    public async Task 聞き直しても無ければ_印は残り日時が新しくなる()
    {
        await _store.Unresolved.SaveAsync([Row("A", MarkedId)]);
        var before = DateTimeOffset.Now;

        var (_, _, notOnBooth) = await _service.PreviewWithReasonAsync(MarkedId);

        Assert.True(notOnBooth);
        var note = Load("A").NotOnBooth;
        Assert.Equal(MarkedId, note?.ItemId);
        Assert.InRange(note!.CheckedAt, before, DateTimeOffset.Now);
    }

    [Fact]
    public async Task 聞き直して公開されていれば_印を外す()
    {
        await _store.Unresolved.SaveAsync([Row("A", MarkedId), Row("B", OtherId)]);
        _handler.Published = true;

        var (preview, _, _) = await _service.PreviewWithReasonAsync(MarkedId);

        Assert.NotNull(preview);
        Assert.Null(Load("A").NotOnBooth);

        // ほかのIDの答えは触らない。行のほかの欄もそのまま
        Assert.Equal(OtherId, Load("B").NotOnBooth?.ItemId);
        Assert.Equal([MarkedId], Load("A").CandidateItemIds);
        Assert.Equal([@"D:\作り物\A.zip"], Load("A").Paths);
    }

    /// <summary>一時的に届かないときは確かめられていないだけなので、残した答えを変えない。</summary>
    [Fact]
    public async Task 一時的に届かなければ_印も日時も変えない()
    {
        await _store.Unresolved.SaveAsync([Row("A", MarkedId)]);
        _handler.Down = true;

        await _service.PreviewWithReasonAsync(MarkedId);

        Assert.Equal(LongAgo, Load("A").NotOnBooth?.CheckedAt);
    }

    /// <summary>人が打ったIDで「無い」と出ても、記録の無い行には付けない（打ち間違いかもしれず、どの行の物かも決まらない）。</summary>
    [Fact]
    public async Task 印の無い行には_聞き直しの答えを付けない()
    {
        await _store.Unresolved.SaveAsync([Row("A", null)]);

        await _service.PreviewWithReasonAsync(MarkedId);

        Assert.Null(Load("A").NotOnBooth);
    }
}
