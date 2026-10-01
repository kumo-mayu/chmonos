using System.Net;
using System.Net.Http;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 通信しない作り物の BOOTH。**既定の一式は外へ出ない**（CLAUDE.md）ので、アプリの通信の出口をこれに差し替える。
///
/// 何も教えなければ、どの問い合わせにも「無い」（404）と答える。来た問い合わせは <see cref="Requests"/> に残るので、
/// 「この操作は BOOTH へ行かない」も確かめられる。
/// </summary>
internal sealed class FakeBooth : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<string> _requests = [];
    private readonly Dictionary<string, string> _itemJson = new(StringComparer.Ordinal);

    /// <summary>来た問い合わせの URL（来た順）。</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>その商品IDの JSON（<c>/items/{id}.json</c>）に、名前とショップだけの商品を答えるようにする。</summary>
    public void HasItem(string itemId, string name, string shop = "sample-shop")
    {
        lock (_gate)
        {
            _itemJson[itemId] = $$"""
                {
                  "id": {{itemId}},
                  "name": "{{name}}",
                  "shop": { "name": "{{shop}}", "subdomain": "{{shop}}", "url": "https://{{shop}}.booth.pm/" },
                  "images": [],
                  "variations": [ { "id": 1, "name": null, "price": 100 } ]
                }
                """;
        }
    }

    private TaskCompletionSource? _held;

    /// <summary>
    /// 答えを止める。次に <see cref="Release"/> を呼ぶまで、来た問い合わせは答えずに待たせる
    /// （「BOOTH が答えていない間」の画面の守りを確かめるため）。
    /// </summary>
    public void Hold()
    {
        lock (_gate)
        {
            _held ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>止めていた答えを返し始める。</summary>
    public void Release()
    {
        TaskCompletionSource? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        held?.TrySetResult();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 来た順は止めている間も残す（答える前に「問い合わせが来た」を確かめられるように）
        Task? waiting;
        lock (_gate)
        {
            _requests.Add(request.RequestUri!.ToString());
            waiting = _held?.Task;
        }

        if (waiting is not null)
        {
            await waiting.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Answer(request);
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        lock (_gate)
        {
            foreach (var (itemId, json) in _itemJson)
            {
                if (url.EndsWith($"/items/{itemId}.json", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                }

                // 商品ページ（説明文の取得）。中身は空でよい
                if (url.EndsWith($"/items/{itemId}", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body></body></html>"),
                    };
                }
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
